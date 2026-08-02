using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PlasticGui.WorkspaceWindow.Items;
using RyanAssets.DataService;
using RyanAssets.Shared.Player;
using RyanAssets.Tools.Shared;
using System;
using System.Collections.Generic;
using RyanAssets.Core;
using UnityEngine;
using RyanAssets.Shared.Declarations;

namespace RyanAssets.Characters.Shared {
    public class GameCharacter : NetworkBehaviour {
        public static Dictionary<TeamColor, HashSet<GameCharacter>> TeamToCharacter = new();
        public static event Action<GameCharacter> GameCharacterAdded, GameCharacterRemoved;
        public readonly SyncVar<TeamConfig> Team = new(new(), new(WritePermission.ClientUnsynchronized));
        public readonly SyncVar<long> Health = new();
        public readonly SyncVar<long> MaxHealth = new();
        //public readonly SyncVar<bool> Invul = new();
        public readonly SyncDictionary<CharacterEffect, float> ActiveEffects = new();
        public readonly SyncVar<ToolBaseShared> ActiveTool = new(null, new(WritePermission.ClientUnsynchronized));
        public readonly SyncVar<string> DisplayName = new("Anonymous");
        public readonly SyncVar<Vector3> CharacterScale = new(Vector3.one);
        //public readonly SyncVar<float> MaxStamina = new();
        //public readonly SyncVar<float> StaminaRegen = new();
        //public readonly SyncVar<float> StaminaCooldown = new();
        public readonly bool FallHeightEnabled = true;
        public readonly float FallenPartsDestroyHeight = 0.0f;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            GameCharacterAdded = null;
        }
        public void SwitchTool(ToolBaseShared tool) {
            if (tool == ActiveTool.Value || (IsDead() && tool)) return;
#if UNITY_SERVER
            if (ActiveTool.Value) {
                if (gameObject != null)
                    ActiveTool.Value.UnequipServer();
                else
                    ActiveTool.Value.Unequip();
            }
            if (tool)
                tool.EquipServer();
#else
            if (ActiveTool.Value)
                ActiveTool.Value.UnequipClient();
            if (tool)
                tool.EquipClient();
#endif
            ActiveTool.Value = tool;
        }
        public bool Equipped(ToolBaseShared tool) {
            return ActiveTool.Value == tool;
        }
        public bool IsEffectActive(CharacterEffect effect) {
            if (ActiveEffects.TryGetValue(effect, out float timer)) {
                return timer >= InstanceFinder.TimeManager.Tick;
            }
            return false;
        }
#if UNITY_EDITOR
        [SerializeField] private long HealthEditor, MaxHealthEditor;
        [SerializeField] private TeamConfig TeamEditor;
#if UNITY_SERVER
        protected override void OnValidate() {
            base.OnValidate();
            MaxHealth.Value = MaxHealthEditor;
            TakeDamage(Health.Value - HealthEditor);
            SetTeam(TeamEditor);
            HealthEditor = Health.Value;
        }
#endif
        protected void OnEnable() {
            Health.OnChange += UpdateEditorOptions;
            MaxHealth.OnChange += UpdateEditorOptions;
            Team.OnChange += UpdateTeamEditorOptions;
        }
        protected void OnDisable() {
            Health.OnChange -= UpdateEditorOptions;
            MaxHealth.OnChange -= UpdateEditorOptions;
            Team.OnChange -= UpdateTeamEditorOptions;
        }
        protected virtual void UpdateTeamEditorOptions(TeamConfig old, TeamConfig newVal, bool asServer) {
            TeamEditor = newVal;
        }
        protected void UpdateEditorOptions(long oldval, long newval, bool asServer) {
            HealthEditor = Health.Value;
            MaxHealthEditor = MaxHealth.Value;
        }
#endif
        public Action<DamageSource, NetworkObject> OnDied;
#if UNITY_SERVER
        private static DamageSource[] invulSources = {DamageSource.Fall, DamageSource.Melee, DamageSource.Gun};
        [Server]
        public virtual bool IsProtected(GameCharacter sourceCharacter = null, DamageSource damageSource = DamageSource.None) {
            return (
                (IsEffectActive(CharacterEffect.Invul) || SharedGlobalEvents.Instance.GlobalInvul)  // INVUL ACTIVATE
                && Array.Exists(invulSources, s => s == damageSource)) ||

                (sourceCharacter != null && // SOURCE CHARACTER
                    (sourceCharacter.GetTeam().team == GetTeam().team && SharedGlobalEvents.Instance.TeamKillEnabled) ||
                    (sourceCharacter.IsDead())
            );
        }
        [Server]
        public bool IsProtected(GameCharacter damageSource) {
            return IsProtected(damageSource, DamageSource.None);
        }
        public virtual bool TakeDamage(long Damage, DamageSource source = DamageSource.None, NetworkObject sourceObject = null) {
            // If the character is dead or invulnerable, ignore damage
            if (Health.Value == 0)
                return false;
            // Verify the attacker isn't a humanoid or is alive
            GameCharacter gameCharacter = sourceObject?.GetComponent<GameCharacter>();
            if (gameCharacter == null && sourceObject != null) {
                Debug.LogError($"Damage source object {sourceObject.name} does not have a GameCharacter component.");
            }
            if (IsProtected(gameCharacter, source))
                return false;

            if (Damage < 0) {
                HealHealth(-Damage);
                return true;
            }
            if (Damage >= Health.Value && MaxHealth.Value >= 0) {
                Died(source, sourceObject);
            } else {
                SetHealth(Health.Value - Damage);
            }
            return true;
        }
        public virtual void HealHealth(long hitpoints) {
            if (Health.Value >= 0 || MaxHealth.Value == 0)
                SetHealth(Health.Value + hitpoints);
        }
        public virtual void AddEffect(CharacterEffect effect, float duration) {
            if (!IsEffectActive(effect)) {
                ActiveEffects[effect] = NetworkHelper.GetServerTime() + duration;
            }
            else
                ActiveEffects[effect] += duration;
        }
        public virtual void RemoveEffect(CharacterEffect effect) {
            ActiveEffects.Remove(effect);
        }
        protected virtual void SetHealth(long hitpoints) {
            Health.Value = hitpoints;
#if UNITY_EDITOR
            HealthEditor = hitpoints;
            MaxHealthEditor = MaxHealth.Value;
#endif
        }
        [Server]
        protected virtual void Died(DamageSource source, NetworkObject sourceObject) {
            if (IsDead()) return;
            SetHealth(0);
            SharedDied(source, sourceObject);
        }
        public virtual void Init(long hp, long maxHP) {
            MaxHealth.Value = maxHP;
            SetHealth(hp);
        }
        public void Init(long hp){
            Init(hp, hp);
        }
        public void InitDefaultEffects() {
            AddEffect(CharacterEffect.Invul, 50000f);
        }
        [Server]
        public virtual void Kill(DamageSource source, NetworkObject sourceObject = null) {
            Died(source, sourceObject);
        }
        
        [Server]
        public virtual void SetTeam(TeamConfig teamConfig) {
            if (IsDead()) {
                Debug.LogWarning($"Cannot set team for dead character {gameObject.name}");
                return;
            }
            UpdateTeamRegistry(Team.Value, teamConfig);
            Team.Value = teamConfig;
#if UNITY_EDITOR
            UpdateTeamEditorOptions(default, teamConfig, true);
#endif
        }
        protected void UpdateTeamRegistry(TeamConfig oldTeam, TeamConfig newTeam) {
            if (oldTeam != null && TeamToCharacter.ContainsKey(oldTeam.team))
                TeamToCharacter[oldTeam.team].Remove(this);
            if (!TeamToCharacter.ContainsKey(newTeam.team))
                TeamToCharacter[newTeam.team] = new HashSet<GameCharacter>();
            TeamToCharacter[newTeam.team].Add(this);
        }
        private void FixedUpdate() {
            if (FallHeightEnabled && IsSpawned) {
                if (transform.position.y < FallenPartsDestroyHeight) {
                    Kill(DamageSource.Fall, null);
                    InstanceFinder.ServerManager.Despawn(gameObject);

                    //Vector3 newPositon = transform.position;
                    //newPositon.y = FallenPartsDestroyHeight;
                    //transform.position = newPositon;
                    //Time.timeScale = 0f;
                    //Debug.Log("Fallen character");
                }
            }
        }
#endif
        public virtual TeamConfig GetTeam() {
            return Team.Value;
        }
        protected virtual void SharedDied(DamageSource source, NetworkObject sourceObject) {
            SwitchTool(null);
            if (!transform.tag.StartsWith("Dead"))
                transform.tag = "Dead" + transform.tag;

            OnDied?.Invoke(source, sourceObject);
        }
        public bool IsDead() {
            return Health.Value == 0 && MaxHealth.Value != 0;
        }
        public bool IsFullHealth() {
            return Health.Value == MaxHealth.Value && !IsDead();
        }

        public override void OnStopNetwork() {
            SwitchTool(null);
            GameCharacterRemoved?.Invoke(this);
        }
        public override void OnStartNetwork() {
#if !UNITY_SERVER
            if (ActiveTool.Value)
                ActiveTool.Value.EquipClient();
#else
            
#endif
            if (Health.Value == 0 && MaxHealth.Value > 0) {
#if UNITY_SERVER
                Kill(DamageSource.None);
#else
                SharedDied(DamageSource.None, null);
#endif
            }
            else {

            }
            gameObject.name = $"{DisplayName.Value} ({NetworkObject.ObjectId})";
            GameCharacterAdded?.Invoke(this);
        }
        
    }
}
