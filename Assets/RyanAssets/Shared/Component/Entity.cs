using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace RyanAssets.Shared.Component {
    /// <summary>
    /// Base NetworkBehaviour for entities which own replicated health and effects.
    /// HealthComponent remains the authoritative owner; this class exposes its common API.
    /// </summary>
    [RequireComponent(typeof(EffectsComponent), typeof(HealthComponent))]
    public abstract class Entity : NetworkBehaviour, IEntity {
        private EffectsComponent effectsComponent;
        private HealthComponent healthComponent;

        public abstract string DisplayName { get; set; }
        public abstract TeamConfig Team { get; }

        public EffectsComponent EffectsComponent => effectsComponent;
        public HealthComponent HealthComponent => healthComponent;
        public bool IsDead => HealthComponent.IsDead;
        public bool IsDied => IsDead;
        public bool IsFullHealth => HealthComponent.IsFullHealth;
        public SyncVar<long> Health => HealthComponent.Health;
        public SyncVar<long> MaxHealth => HealthComponent.MaxHealth;
        public SyncDictionary<CharacterEffect, float> ActiveEffects => EffectsComponent.ActiveEffects;

        public event Action<DamageType, IEntity> OnDamage {
            add => HealthComponent.OnDamage += value;
            remove => HealthComponent.OnDamage -= value;
        }

        public event Action<DamageType, IEntity> OnDied {
            add => HealthComponent.OnDied += value;
            remove => HealthComponent.OnDied -= value;
        }

        public event Action OnRevive {
            add => HealthComponent.OnRevive += value;
            remove => HealthComponent.OnRevive -= value;
        }

        public bool IsEffectActive(CharacterEffect effect) => EffectsComponent.IsEffectActive(effect);

        protected virtual void Awake() {
            effectsComponent ??= GetComponent<EffectsComponent>();
            healthComponent ??= GetComponent<HealthComponent>();
            if (effectsComponent == null || healthComponent == null)
                throw new MissingComponentException($"{GetType().Name} requires {nameof(EffectsComponent)} and {nameof(HealthComponent)}.");
        }

#if UNITY_SERVER
        [Server]
        public virtual bool IsProtected(IEntity sourceEntity = null, DamageType damageType = DamageType.None) =>
            HealthComponent.IsProtected(sourceEntity, damageType);

        [Server]
        public virtual bool TakeDamage(long damage, DamageType source = DamageType.None, IEntity sourceEntity = null) =>
            HealthComponent.TakeDamage(damage, source, sourceEntity);

        [Server]
        public bool TakeDamage(long damage, DamageType source, NetworkObject sourceObject) =>
            TakeDamage(damage, source, GetEntity(sourceObject));

        [Server] public virtual void HealHealth(long hitpoints) => HealthComponent.HealHealth(hitpoints);
        [Server] public virtual void HealMaxHealth(long hitpoints) => HealthComponent.HealMaxHealth(hitpoints);
        [Server] public virtual void Init(long hp, long maxHp) => HealthComponent.Init(hp, maxHp);
        [Server] public void Init(long hp) => Init(hp, hp);
        [Server] public virtual void Revive(long hp, long maxHp) => HealthComponent.Revive(hp, maxHp);
        [Server] public void Revive(long hp) => Revive(hp, hp);
        [Server] public virtual void Kill(DamageType source, IEntity sourceEntity = null) => HealthComponent.Kill(source, sourceEntity);
        [Server] public void Kill(DamageType source, NetworkObject sourceObject) => Kill(source, GetEntity(sourceObject));
        [Server] public virtual void AddEffect(CharacterEffect effect, float duration) => EffectsComponent.AddEffect(effect, duration);
        [Server] public virtual void RemoveEffect(CharacterEffect effect) => EffectsComponent.RemoveEffect(effect);
        [Server] public virtual void ClearEffects() => EffectsComponent.ClearEffects();

        private static IEntity GetEntity(NetworkObject sourceObject) {
            if (!sourceObject)
                return null;

            IEntity sourceEntity = sourceObject.GetComponent<IEntity>();
            if (sourceEntity == null)
                Debug.LogError($"Damage source object {sourceObject.name} does not implement {nameof(IEntity)}.");
            return sourceEntity;
        }
#endif
    }
}
