using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Tools.Shared;
using System;
using UnityEngine;

namespace RyanAssets.Characters.Shared {
    public enum DamageSource {
        None,
        Fall,
        Melee,
        Gun,
        Despawn,
        
        Reset,
        
        Command
    };
    public class GameCharacter : NetworkBehaviour {
        public readonly SyncVar<long> Health = new();
        public readonly SyncVar<long> MaxHealth = new();
        public readonly SyncVar<ToolBaseShared> ActiveTool = new(new(WritePermission.ClientUnsynchronized));
#if !UNITY_SERVER
        public float Stamina;
#endif
        public readonly SyncVar<float> MaxStamina = new();
        public readonly SyncVar<float> StaminaRegen = new();
        public readonly SyncVar<float> StaminaCooldown = new();
        public void SwitchTool(ToolBaseShared tool) {
            if (tool == ActiveTool.Value || (IsDead() && tool)) return;
#if UNITY_SERVER
            if (ActiveTool.Value)
                ActiveTool.Value.UnequipServer();
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
#if UNITY_EDITOR
        [SerializeField] private long HealthEditor, MaxHealthEditor;
        protected override void OnValidate() {
            base.OnValidate();
            MaxHealth.Value = MaxHealthEditor;
            TakeDamage(Health.Value - HealthEditor);
            HealthEditor = Health.Value;
        }
        protected void OnEnable() {
            Health.OnChange += UpdateEditorOptions;
            MaxHealth.OnChange += UpdateEditorOptions;
        }
        protected void OnDisable() {
            Health.OnChange -= UpdateEditorOptions;
            MaxHealth.OnChange -= UpdateEditorOptions;
        }
        protected void UpdateEditorOptions(long oldval, long newval, bool asServer) {
            HealthEditor = Health.Value;
            MaxHealthEditor = MaxHealth.Value;
        }
#endif
        public Action<DamageSource, NetworkObject> OnDied;
        public virtual void TakeDamage(long Damage, DamageSource source = DamageSource.None, NetworkObject sourceObject = null) {
            if (Health.Value == 0)
                return;
            if (Damage < 0) {
                HealHealth(-Damage);
                return;
            }
            if (Damage >= Health.Value && MaxHealth.Value >= 0) {
                Died(source, sourceObject);
            } else {
                SetHealth(Health.Value - Damage);
            }
        }
        public virtual void HealHealth(long hitpoints) {
            if (Health.Value >= 0 || MaxHealth.Value == 0)
                SetHealth(Health.Value + hitpoints);
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
        protected virtual void SharedDied(DamageSource source, NetworkObject sourceObject) {
            SwitchTool(null);
            if (!transform.tag.StartsWith("Dead"))
                transform.tag = "Dead" + transform.tag;

            OnDied?.Invoke(source, sourceObject);
        }
        [Server]
        public virtual void Kill(DamageSource source, NetworkObject sourceObject = null) {
            Died(source, sourceObject);
        }
        public bool IsDead() {
            return Health.Value == 0 && MaxHealth.Value != 0;
        }
        public bool IsFullHealth() {
            return Health.Value == MaxHealth.Value && !IsDead();
        }

        public virtual void Init(long hp, long maxHP, float maxStamina = 100f, float staminaRegen = 10f, float staminaCooldown = 0.4f) {
            MaxHealth.Value = maxHP;
            MaxStamina.Value = maxStamina;
            StaminaRegen.Value = staminaRegen;
            StaminaCooldown.Value = staminaCooldown;
            SetHealth(hp);
        }
        public void Init(long hp){
            Init(hp, hp);
        }
        public override void OnStopNetwork() {
            SwitchTool(null);
        }
        public override void OnStartNetwork() {
#if !UNITY_SERVER
            if (ActiveTool.Value)
                ActiveTool.Value.EquipClient();
#endif
            if (Health.Value == 0 && MaxHealth.Value > 0)
#if UNITY_SERVER
                Kill(DamageSource.None);
#else
                SharedDied(DamageSource.None, null);
#endif
            
        }
    }
}