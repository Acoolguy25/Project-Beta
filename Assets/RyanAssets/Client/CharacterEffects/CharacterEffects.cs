using Cysharp.Threading.Tasks;
using FishNet.Object.Synchronizing;
using NUnit.Framework.Internal;
using RyanAssets.Characters.Shared;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using static UnityEngine.ParticleSystem;

namespace RyanAssets.Client.CharacterEffects {
    internal struct CharacterEffectParticle {
        public ParticleSystem particleSystem;
        public CancellationTokenSource cancellationTokenSource;
        public Vector3 localPosition;
    }
    internal class GameCharacterEffectManager : IDisposable {
        private const float CameraFacingOffset = 0.5f;
        public GameCharacter character;
        public GameObject root;
        Dictionary<CharacterEffect, CharacterEffectParticle> activeEffectParticles = new();
        List<GameObject> PositiveEffectsPrefab, NegativeEffectsPrefab;
        public GameCharacterEffectManager(GameCharacter character, GameObject root, List<GameObject> positiveEffectsPrefab, List<GameObject> negativeEffectsPrefab) {
            this.character = character;
            this.root = root;
            this.PositiveEffectsPrefab = positiveEffectsPrefab;
            this.NegativeEffectsPrefab = negativeEffectsPrefab;
            character.ActiveEffects.OnChange += OnActiveEffectsChanged;
        }
        public void OnActiveEffectsChanged(SyncDictionaryOperation op, CharacterEffect key, float timeEnd, bool asServer) {
            switch (op) {
                case SyncDictionaryOperation.Add:
                case SyncDictionaryOperation.Set:
                case SyncDictionaryOperation.Remove:
                    float serverTime = NetworkHelper.GetServerTime();
                    if (op == SyncDictionaryOperation.Remove || timeEnd < serverTime) {
                        RemoveEffect(key);
                    } else {
                        AddEffect(key, timeEnd - serverTime);
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
        private static void CancelAndDispose(CancellationTokenSource cts) {
            if (cts == null)
                return;
            try {
                cts.Cancel();
                cts.Dispose();
            } catch (ObjectDisposedException) {
                // Cleaned up already
            }
        }
        private async UniTask RemoveEffectAfterDelay(CharacterEffect effect, float duration, CancellationToken token) {
            bool isCancelled = await UniTask.Delay(
                TimeSpan.FromSeconds(duration),
                cancellationToken: token
            ).SuppressCancellationThrow();

            if (!isCancelled)
                RemoveEffect(effect);
        }
        public void AddEffect(CharacterEffect effect, float duration) {
            CancellationTokenSource cts = new CancellationTokenSource();
            if (!activeEffectParticles.TryGetValue(effect, out CharacterEffectParticle existingParticle)) {
                int effectIdx = Mathf.Abs((int)effect) - 1;
                GameObject effectClone = GameObject.Instantiate((((int)effect > 0) ? PositiveEffectsPrefab[effectIdx] : NegativeEffectsPrefab[effectIdx]));
                effectClone.transform.SetParent(root.transform, false);
                activeEffectParticles[effect] = new CharacterEffectParticle {
                    particleSystem = effectClone.GetComponent<ParticleSystem>(),
                    cancellationTokenSource = cts,
                    localPosition = effectClone.transform.localPosition
                };
            } else {
                CancelAndDispose(existingParticle.cancellationTokenSource);
                existingParticle.cancellationTokenSource = cts;
                activeEffectParticles[effect] = existingParticle;
            }
            RemoveEffectAfterDelay(effect, duration, cts.Token).Forget();
        }
        public void RemoveEffect(CharacterEffect effect) {
            if (activeEffectParticles.TryGetValue(effect, out CharacterEffectParticle particle)) {
                if (particle.particleSystem)
                    GameObject.Destroy(particle.particleSystem.gameObject);
                CancelAndDispose(particle.cancellationTokenSource);
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
