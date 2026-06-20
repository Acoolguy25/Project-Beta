using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using FishNet.Object.Synchronizing;

namespace RyanAssets.Characters.Shared {
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
        public Action OnDied;
        public virtual void TakeDamage(long Damage) {
            if (Health.Value == 0)
                return;
            if (Damage < 0) {
                HealHealth(-Damage);
                return;
            }
            if (Damage >= Health.Value && MaxHealth.Value >= 0) {
                Died();
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
        protected virtual void Died() {
            SetHealth(0);
            OnDied?.Invoke();
        }
        public virtual void Kill() {
            Died();
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
                Kill();
        }
    }
}