using System;
using FishNet.Object.Synchronizing;

namespace RyanAssets.Shared.Declarations {
    /// <summary>Common identity, health, and effects contract for networked entities.</summary>
    public interface IEntity {
        string DisplayName { get; set; }
        TeamConfig Team { get; }

        SyncVar<long> Health { get; }
        SyncVar<long> MaxHealth { get; }
        SyncDictionary<CharacterEffect, float> ActiveEffects { get; }
        bool IsDead { get; }
        bool IsDied { get; }
        bool IsFullHealth { get; }

        event Action<DamageType, IEntity> OnDamage;
        event Action<DamageType, IEntity> OnDied;
        event Action OnRevive;

        bool IsEffectActive(CharacterEffect effect);

#if UNITY_SERVER
        bool IsProtected(IEntity sourceEntity = null, DamageType damageType = DamageType.None);
        bool TakeDamage(long damage, DamageType source = DamageType.None, IEntity sourceEntity = null);
        void HealHealth(long hitpoints);
        void HealMaxHealth(long hitpoints);
        void Init(long hp, long maxHp);
        void Revive(long hp, long maxHp);
        void Kill(DamageType source, IEntity sourceEntity = null);
        void AddEffect(CharacterEffect effect, float duration);
        void RemoveEffect(CharacterEffect effect);
        void ClearEffects();
#endif
    }
}
