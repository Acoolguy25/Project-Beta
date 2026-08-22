using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using System.Linq;
using UnityEngine;

namespace RyanAssets.Shared.Component {
    /// <summary>Owns health, death state, and replicated death notifications for an entity.</summary>
    [RequireComponent(typeof(EffectsComponent))]
    public class HealthComponent : NetworkBehaviour {
        private static readonly DamageType[] InvulDamageTypes = { DamageType.Fall, DamageType.Melee, DamageType.Gun };

        public readonly SyncVar<long> Health = new();
        public readonly SyncVar<long> MaxHealth = new();
        public bool IsDead => Health.Value == 0 && MaxHealth.Value != 0;
        public bool IsFullHealth => Health.Value == MaxHealth.Value && !IsDead;
        public event Action<DamageType, IEntity> OnDamage;
        public event Action<DamageType, IEntity> OnDied;

        private EffectsComponent Effects => GetComponent<EffectsComponent>();
        private IEntity Entity => GetComponent<IEntity>();

#if UNITY_EDITOR
        [SerializeField] private long healthEditor;
        [SerializeField] private long maxHealthEditor;

#if UNITY_SERVER
        protected override void OnValidate() {
            base.OnValidate();
            // Inspector validation also runs for prefab/unspawned objects. SyncVars cannot
            // be changed through [Server] methods until FishNet has initialized this object.
            if (!IsSpawned)
                return;
            MaxHealth.Value = maxHealthEditor;
            TakeDamage(Health.Value - healthEditor);
            healthEditor = Health.Value;
        }
#endif

        protected virtual void OnEnable() {
            Health.OnChange += UpdateEditorOptions;
            MaxHealth.OnChange += UpdateEditorOptions;
        }

        protected virtual void OnDisable() {
            Health.OnChange -= UpdateEditorOptions;
            MaxHealth.OnChange -= UpdateEditorOptions;
        }

        private void UpdateEditorOptions(long _, long __, bool ___) {
            healthEditor = Health.Value;
            maxHealthEditor = MaxHealth.Value;
        }
#endif

#if UNITY_SERVER
        [Server]
        public virtual bool IsProtected(IEntity sourceEntity = null, DamageType damageType = DamageType.None) {
            IEntity entity = Entity;
            return ((Effects.IsEffectActive(CharacterEffect.Invul) || SharedGlobalEvents.Instance.GlobalInvul)
                    && InvulDamageTypes.Contains(damageType))
                || (sourceEntity != null
                    && entity != null
                    && sourceEntity.Team.team == entity.Team.team
                    && SharedGlobalEvents.Instance.TeamKillEnabled)
                || IsEntityDead(sourceEntity);
        }

        [Server]
        public virtual bool TakeDamage(long damage, DamageType source = DamageType.None, IEntity sourceEntity = null) {
            if (Health.Value == 0 || IsProtected(sourceEntity, source))
                return false;

            if (damage < 0) {
                HealHealth(-damage);
                return true;
            }

            // Raise this before death processing so server-side behaviours can react to the
            // hit while the damaged entity is still valid. Healing intentionally does not
            // count as damage.
            if (damage > 0)
                OnDamage?.Invoke(source, sourceEntity);

            if (damage >= Health.Value && MaxHealth.Value >= 0)
                Died(source, sourceEntity);
            else
                SetHealth(Health.Value - damage);
            return true;
        }

        [Server]
        public virtual void HealHealth(long hitpoints) {
            if (Health.Value >= 0 || MaxHealth.Value == 0)
                SetHealth(Health.Value + hitpoints);
        }

        [Server]
        public virtual void HealMaxHealth(long hitpoints) {
            if (MaxHealth.Value >= 0)
                MaxHealth.Value += hitpoints;
            HealHealth(hitpoints);
        }

        [Server]
        public virtual void Init(long hp, long maxHp) {
            MaxHealth.Value = maxHp;
            SetHealth(hp);
        }

        [Server]
        public void Init(long hp) {
            Init(hp, hp);
        }

        [Server]
        public virtual void Kill(DamageType source, IEntity sourceEntity = null) {
            Died(source, sourceEntity);
        }

        [Server]
        protected virtual void Died(DamageType source, IEntity sourceEntity) {
            if (IsDead)
                return;

            SetHealth(0);
            Effects.ClearEffects();
            SharedDied(source, sourceEntity);
            RpcDied(source, GetNetworkObject(sourceEntity));
        }

        [Server]
        protected virtual void SetHealth(long hitpoints) {
            Health.Value = hitpoints;
#if UNITY_EDITOR
            healthEditor = hitpoints;
            maxHealthEditor = MaxHealth.Value;
#endif
        }
#endif

        [ObserversRpc]
        private void RpcDied(DamageType source, NetworkObject sourceObject) {
            SharedDied(source, sourceObject ? sourceObject.GetComponent<IEntity>() : null);
        }

        protected virtual void SharedDied(DamageType source, IEntity sourceEntity) {
            OnDied?.Invoke(source, sourceEntity);
        }

        private static bool IsEntityDead(IEntity entity) {
            return entity is UnityEngine.Component component
                && component.TryGetComponent(out HealthComponent health)
                && health.IsDead;
        }

        private static NetworkObject GetNetworkObject(IEntity entity) {
            return entity is UnityEngine.Component component ? component.GetComponent<NetworkObject>() : null;
        }
    }
}
