using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using FishNet.Object.Synchronizing;

namespace RyanAssets.Characters.Shared {
    public enum DamageSource {
        None,
        Fall,
        Firearm,
        Kill
    };
    public class GameCharacter : NetworkBehaviour {
        public readonly SyncVar<long> Health = new();
        public readonly SyncVar<long> MaxHealth = new();
#if UNITY_EDITOR
        [SerializeField] private long HealthEditor, MaxHealthEditor;
        protected override void OnValidate() {
            base.OnValidate();
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
        public Action<DamageSource> OnDied;
        public virtual void TakeDamage(long Damage, DamageSource source = DamageSource.None) {
            if (Health.Value == 0)
                return;
            if (Damage < 0) {
                HealHealth(-Damage);
                return;
            }
            if (Damage >= Health.Value && MaxHealth.Value >= 0) {
                Died(source);
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
        protected virtual void Died(DamageSource source) {
            SetHealth(0);
            OnDied?.Invoke(source);
        }
        public virtual void Kill(DamageSource source) {
            Died(source);
        }
        public bool IsDead() {
            return Health.Value == 0 && MaxHealth.Value != 0;
        }
        public bool IsFullHealth() {
            return Health.Value == MaxHealth.Value && !IsDead();
        }

        public virtual void Init(long hp, long maxHP) {
            MaxHealth.Value = maxHP;
            SetHealth(hp);
        }
        public void Init(long hp){
            Init(hp, hp);
        }
        protected virtual void Start() {
            if (Health.Value == 0 && MaxHealth.Value > 0)
                Kill(DamageSource.Kill);
        }
    }
}