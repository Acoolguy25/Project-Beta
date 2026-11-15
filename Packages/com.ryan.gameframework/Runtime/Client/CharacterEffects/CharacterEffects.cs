using FishNet.Object.Synchronizing;
using RyanAssets.Characters.Shared;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RyanAssets.Client.CharacterEffects {
    internal struct CharacterEffectParticle {
        public ParticleSystem particleSystem;
    }
    internal class GameCharacterEffectManager : IDisposable {
        private const float CameraFacingOffset = 0.5f;
        public GameCharacter character;
        public GameObject root;
        Dictionary<CharacterEffect, CharacterEffectParticle> activeEffectParticles = new();
        readonly List<CharacterEffect> expiredEffects = new();
        List<GameObject> PositiveEffectsPrefab, NegativeEffectsPrefab;
        public GameCharacterEffectManager(GameCharacter character, GameObject root, List<GameObject> positiveEffectsPrefab, List<GameObject> negativeEffectsPrefab) {
            this.character = character;
            this.root = root;
            this.PositiveEffectsPrefab = positiveEffectsPrefab;
            this.NegativeEffectsPrefab = negativeEffectsPrefab;
            character.ActiveEffects.OnChange += OnActiveEffectsChanged;
            // SyncDictionary contents may already be present when this character is registered.
            foreach (var effect in character.ActiveEffects)
                if (effect.Value > NetworkHelper.GetServerTime()) AddEffect(effect.Key);
        }
        public void OnActiveEffectsChanged(SyncDictionaryOperation op, CharacterEffect key, float timeEnd, bool asServer) {
            switch (op) {
                case SyncDictionaryOperation.Add:
                case SyncDictionaryOperation.Set:
                case SyncDictionaryOperation.Remove:
                    if (op == SyncDictionaryOperation.Remove || timeEnd <= NetworkHelper.GetServerTime()) {
                        RemoveEffect(key);
                    } else {
                        AddEffect(key);
                    }
                    break;
                case SyncDictionaryOperation.Clear:
                    // Handle effects cleared
                    //Debug.Log($"All effects cleared from character {character.name}");
                    ClearEffects();
                    break;

            }
        }
        public void ClearEffects() {
            foreach (var effect in activeEffectParticles.Keys.ToList()) {
                RemoveEffect(effect);
            }
        }
        public void Update() {
            // Expiry does not generate a SyncDictionary change. Read the same server
            // clock as damage protection so a shield cannot outlive its effect.
            if (character == null || !character.IsSpawned) {
                ClearEffects();
                return;
            }
            expiredEffects.Clear();
            foreach (CharacterEffect effect in activeEffectParticles.Keys)
                if (!character.IsEffectActive(effect)) expiredEffects.Add(effect);
            foreach (CharacterEffect effect in expiredEffects)
                RemoveEffect(effect);
        }
        public void AddEffect(CharacterEffect effect) {
            if (!activeEffectParticles.ContainsKey(effect)) {
                int effectIdx = Mathf.Abs((int)effect) - 1;
                GameObject effectClone = GameObject.Instantiate((((int)effect > 0) ? PositiveEffectsPrefab[effectIdx] : NegativeEffectsPrefab[effectIdx]));
                effectClone.transform.SetParent(root.transform, false);
                activeEffectParticles[effect] = new CharacterEffectParticle {
                    particleSystem = effectClone.GetComponent<ParticleSystem>()
                };
            }
        }
        public void RemoveEffect(CharacterEffect effect) {
            if (activeEffectParticles.TryGetValue(effect, out CharacterEffectParticle particle)) {
                if (particle.particleSystem)
                    GameObject.Destroy(particle.particleSystem.gameObject);
                activeEffectParticles.Remove(effect);
            }
        }
        public void Dispose() {
            character.ActiveEffects.OnChange -= OnActiveEffectsChanged;
            ClearEffects();
        }
    }
    public class CharacterEffects : MonoBehaviour {
        [SerializeField]
        public List<GameObject> PositiveEffectsPrefab = new(), NegativeEffectsPrefab = new();

        List<GameCharacterEffectManager> effectInstances = new();
        void Start() {
            GameCharacter.GameCharacterAdded += OnGameCharacterAdded;
            GameCharacter.GameCharacterRemoved += OnGameCharacterRemoved;
        }
        void Update() {
            foreach (GameCharacterEffectManager manager in effectInstances)
                manager.Update();
        }
        void OnGameCharacterAdded(GameCharacter character) {
            // Effects should follow the character as a whole. Parenting to Hips makes the
            // shield inherit the animated bone's rotation and leaves it looking offset or
            // behind the character during movement.
            effectInstances.Add(new GameCharacterEffectManager(character, character.gameObject, PositiveEffectsPrefab, NegativeEffectsPrefab));
        }
        void OnGameCharacterRemoved(GameCharacter character) {
            foreach (GameCharacterEffectManager manager in effectInstances.Where(e => e.character == character).ToList()) {
                manager.Dispose();
                effectInstances.Remove(manager);
            }
        }
        void OnDestroy() {
            GameCharacter.GameCharacterAdded -= OnGameCharacterAdded;
            GameCharacter.GameCharacterRemoved -= OnGameCharacterRemoved;
            foreach (GameCharacterEffectManager manager in effectInstances) {
                manager.Dispose();
            }
            effectInstances.Clear();
        }
    }
}
