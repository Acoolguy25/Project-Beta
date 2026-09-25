using System;
using System.Collections.Generic;
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
    public abstract class EntityBase : NetworkBehaviour, IEntity {
        [SerializeField]
        private EffectsComponent effectsComponent;
        [SerializeField]
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

        /// <summary>
        /// Raised on every build when <see cref="Team"/> changes after spawn. Presentation that
        /// colours an entity by its team - the overhead tag, for one - binds to this rather than
        /// reading the team once and going stale when a game mode assigns it a frame later.
        /// </summary>
        public event Action<EntityBase> TeamChanged;

        /// <summary>For subclasses whose team is replicated: call from the team's change callback.</summary>
        protected void RaiseTeamChanged() => TeamChanged?.Invoke(this);

        /// <summary>
        /// Raised on every build when <see cref="DisplayName"/> changes after spawn, for entities whose
        /// name is derived from replicated state - a vehicle named after the commander who owns it.
        /// </summary>
        public event Action<EntityBase> DisplayNameChanged;

        /// <summary>For subclasses whose name follows replicated state: call when that state changes.</summary>
        protected void RaiseDisplayNameChanged() => DisplayNameChanged?.Invoke(this);

        /// <summary>
        /// Whether the shared overhead tag stays up for this entity the way a player's does. Most
        /// world entities - a wall, a refinery - only show theirs once damaged, so an untouched base
        /// stays clean; something a player commands and needs to pick out, such as a vehicle, shows
        /// its name all the time.
        /// </summary>
        public virtual bool AlwaysShowNameTag => false;

        public bool IsEffectActive(CharacterEffect effect) => EffectsComponent.IsEffectActive(effect);

        static readonly List<EntityBase> all = new();

        /// <summary>
        /// Every spawned entity that routes its network callbacks through this base: structures,
        /// objectives, and vehicles. Characters deliberately do not appear here - they never chain to
        /// <see cref="OnStartNetwork"/> and publish their own roster through
        /// <c>GameCharacter.GameCharacterAdded</c>.
        /// </summary>
        public static IReadOnlyList<EntityBase> All => all;

        /// <summary>
        /// Raised on every build as an entity spawns or despawns. Shared presentation - the overhead
        /// health tag, for one - binds to this instead of sweeping the scene for damageable objects.
        /// </summary>
        public static event Action<EntityBase> EntityAdded, EntityRemoved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            all.Clear();
            EntityAdded = null;
            EntityRemoved = null;
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            all.Add(this);
            EntityAdded?.Invoke(this);
        }

        public override void OnStopNetwork() {
            if (all.Remove(this))
                EntityRemoved?.Invoke(this);
            base.OnStopNetwork();
        }

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
        [Server] public void Init(long hp = 100) => Init(hp, hp);
        [Server] public virtual void Revive(long hp, long maxHp) => HealthComponent.Revive(hp, maxHp);
        [Server] public void Revive(long hp = 100) => Revive(hp, hp);
        [Server] public virtual void Kill(DamageType source, IEntity sourceEntity = null) => HealthComponent.Kill(source, sourceEntity);
        [Server] public void Kill(DamageType source, NetworkObject sourceObject) => Kill(source, GetEntity(sourceObject));
        [Server] public virtual void AddEffect(CharacterEffect effect, float duration) => EffectsComponent.AddEffect(effect, duration);
        [Server] public virtual void SetEffect(CharacterEffect effect, float duration) => EffectsComponent.SetEffect(effect, duration);
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
