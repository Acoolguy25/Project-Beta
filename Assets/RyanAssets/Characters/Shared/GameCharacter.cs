using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.DataService;
using RyanAssets.Shared.Player;
using RyanAssets.Tools.Shared;
using System;
using System.Collections.Generic;
using RyanAssets.Core;
using UnityEngine;
using RyanAssets.Shared.Declarations;
using System.Linq;

namespace RyanAssets.Characters.Shared {
    public class GameCharacter : NetworkBehaviour {
        [SerializeField]
        public Transform CharacterCamera;

        public static Dictionary<TeamColor, HashSet<GameCharacter>> TeamToCharacter = new();
        public static event Action<GameCharacter> GameCharacterAdded, GameCharacterRemoved;
        public event Action<GameCharacter> MyGameCharacterRemoved;
        public readonly SyncVar<TeamConfig> Team = new(new(), new(WritePermission.ClientUnsynchronized));
        public readonly SyncVar<long> Health = new();
        public readonly SyncVar<long> MaxHealth = new();
        //public readonly SyncVar<bool> Invul = new();
        public readonly SyncDictionary<CharacterEffect, float> ActiveEffects = new();
        public readonly SyncVar<ToolBaseShared> ActiveTool = new(null, new(WritePermission.ClientUnsynchronized));
        public readonly SyncVar<string> DisplayName = new("Anonymous");
        public readonly SyncVar<Vector3> CharacterScale = new(Vector3.one);
        public readonly SyncVar<bool> CanSpectate = new(true);
        //public readonly SyncVar<float> MaxStamina = new();
        //public readonly SyncVar<float> StaminaRegen = new();
        //public readonly SyncVar<float> StaminaCooldown = new();
        public readonly bool FallHeightEnabled = true;
        public readonly float FallenPartsDestroyHeight = 0.0f;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            GameCharacterAdded = null;
            GameCharacterRemoved = null;
            TeamToCharacter.Clear();
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
                return timer >= NetworkHelper.GetServerTime();
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
        public Action<DamageType, NetworkObject> OnDied;
#if UNITY_SERVER
        private static DamageType[] invulSources = {DamageType.Fall, DamageType.Melee, DamageType.Gun};
        [Server]
        public virtual bool IsProtected(GameCharacter sourceCharacter = null, DamageType damageType = DamageType.None) {
            return (
                (IsEffectActive(CharacterEffect.Invul) || SharedGlobalEvents.Instance.GlobalInvul)  // INVUL ACTIVATE
                && invulSources.Contains(damageType)) ||

                (sourceCharacter != null && // SOURCE CHARACTER
                    (sourceCharacter.GetTeam().team == GetTeam().team && SharedGlobalEvents.Instance.TeamKillEnabled) ||
                    (sourceCharacter.IsDead())
            );
        }
        [Server]
        public bool IsProtected(GameCharacter sourceCharacter) {
            return IsProtected(sourceCharacter, DamageType.None);
        }
        public virtual bool TakeDamage(long Damage, DamageType source = DamageType.None, NetworkObject sourceObject = null) {
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
        public virtual void HealMaxHealth(long hitpoints) {
            if (MaxHealth.Value >= 0)
                MaxHealth.Value += hitpoints;
            HealHealth(hitpoints);
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
        public virtual void ClearEffects() {
            ActiveEffects.Clear();
        }
        protected virtual void SetHealth(long hitpoints) {
            Health.Value = hitpoints;
#if UNITY_EDITOR
            HealthEditor = hitpoints;
            MaxHealthEditor = MaxHealth.Value;
#endif
        }
        [Server]
        protected virtual void Died(DamageType source, NetworkObject sourceObject) {
            if (IsDead()) return;
            SetHealth(0);
            ClearEffects();
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
            AddEffect(CharacterEffect.Invul, 5f);
        }
        [Server]
        public virtual void Kill(DamageType source, NetworkObject sourceObject = null) {
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
        [Server]
        private void FixedUpdate() {
            if (FallHeightEnabled && IsSpawned) {
                if (transform.position.y < FallenPartsDestroyHeight) {
                    Kill(DamageType.Fall, null);
                    InstanceFinder.ServerManager.Despawn(gameObject);

                    //Vector3 newPositon = transform.position;
                    //newPositon.y = FallenPartsDestroyHeight;
                    //transform.position = newPositon;
                    //Time.timeScale = 0f;
                    //Debug.Log("Fallen character");
                }
            }
        }
        [Server]
        public override void OnStopServer() {
            base.OnStopServer();
            foreach (ToolBaseShared tool in GetComponentsInChildren<ToolBaseShared>(true)) {
                if (tool.IsSpawned)
                    InstanceFinder.ServerManager.Despawn(tool.gameObject);
            }
        }
#else
        public override void OnStartClient() {
            Team.OnChange += UpdateTeamClient;
            if (!IsDead())
                UpdateTeamRegistry(default, Team.Value);
            // Client consumers (including spectating) can now safely look this
            // character up in TeamToCharacter.
            GameCharacterAdded?.Invoke(this);
        }
        void UpdateTeamClient(TeamConfig oldTeam, TeamConfig newTeam, bool asServer) {
            UpdateTeamRegistry(oldTeam, newTeam);
        }
#endif
        protected void RemoveTeamRegistry(TeamConfig team) {
            if (team != null && TeamToCharacter.ContainsKey(team.team))
                TeamToCharacter[team.team].Remove(this);
        }
        protected void UpdateTeamRegistry(TeamConfig oldTeam, TeamConfig newTeam) {
            RemoveTeamRegistry(oldTeam);
            if (!TeamToCharacter.ContainsKey(newTeam.team))
                TeamToCharacter[newTeam.team] = new HashSet<GameCharacter>();
            TeamToCharacter[newTeam.team].Add(this);
        }
        public virtual TeamConfig GetTeam() {
            return Team.Value;
        }
        protected virtual void SharedDied(DamageType source, NetworkObject sourceObject) {
            SwitchTool(null);
            if (!transform.tag.StartsWith("Dead"))
                transform.tag = "Dead" + transform.tag;

            RemoveTeamRegistry(GetTeam());
            OnDied?.Invoke(source, sourceObject);
        }
        public bool IsDead() {
            return Health.Value == 0 && MaxHealth.Value != 0;
        }
        public bool IsFullHealth() {
            return Health.Value == MaxHealth.Value && !IsDead();
        }

        public override void OnStopNetwork() {
            // Consumers of the removal events must not be able to select this character
            // from the client-side registry while it is being despawned.
            RemoveTeamRegistry(GetTeam());
            SwitchTool(null);
            GameCharacterRemoved?.Invoke(this);
            MyGameCharacterRemoved?.Invoke(this);
        }
        public override void OnStartNetwork() {
#if !UNITY_SERVER
            if (ActiveTool.Value)
                ActiveTool.Value.EquipClient();
#else
            
#endif
            if (Health.Value == 0 && MaxHealth.Value > 0) {
#if UNITY_SERVER
                Kill(DamageType.None);
#else
                SharedDied(DamageType.None, null);
#endif
            }
            else {

            }
            gameObject.name = $"{DisplayName.Value} ({NetworkObject.ObjectId})";
        }
        
    }
}
