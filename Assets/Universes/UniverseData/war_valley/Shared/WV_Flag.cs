using System.Collections;
using FishNet.Object;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>The networked War Valley objective attacked by hostile teams.</summary>
    [RequireComponent(typeof(EffectsComponent), typeof(HealthComponent))]
    public sealed class WV_Flag : EntityBase, ITeam {
        [SerializeField] private string displayName = "War Valley Flag";
        [SerializeField] private TeamConfig team = new(WV_Alliances.Defenders);
        [SerializeField, Min(1)] private long maxHealth = 1000;
        [SerializeField, Tooltip("How the flag soaks a siege: each hit is cut, and it can lose only so much health a second.")]
        private WV_FlagArmorTuning armor = new();
        [SerializeField, Tooltip("Where free troops (the Veteran Squad, a front's reinforcements) muster, from the flag.")]
        private Vector3 musterOffset = new(14f, 0f, 14f);

#if UNITY_SERVER
        private WV_FlagArmor armorState;
#endif

        private long InitialMaxHealth => maxHealth;

        public static WV_Flag Instance { get; private set; }

        /// <summary>Where free troops muster beside the flag, in world space.</summary>
        public Vector3 MusterPoint => transform.position + musterOffset;

        public override string DisplayName {
            get => displayName;
            set => displayName = value;
        }

        public override TeamConfig Team => team;

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            Instance = this;
            OnDied += HandleDied;
        }

        public override void OnStopNetwork() {
            OnDied -= HandleDied;
            if (Instance == this)
                Instance = null;
            base.OnStopNetwork();
        }

#if UNITY_SERVER
        public override void OnStartServer() {
            base.OnStartServer();
            Init(InitialMaxHealth);
        }

        [Server]
        public void SetTeam(TeamConfig teamConfig) {
            team = teamConfig ?? new TeamConfig(WV_Alliances.Defenders);
        }
#endif

        public TeamConfig GetTeam() => Team;

        /// <summary>The flag's authored health, before any doctrine raised it.</summary>
        public long BaseMaxHealth => maxHealth;

        public WV_FlagArmorTuning Armor => armor;

        /// <summary>
        /// Scales both the share of each hit the flag takes and its per-second cap, from the round's
        /// difficulty. Set by the server runner before the first wave.
        /// </summary>
        public float DamageMultiplier { get; set; } = 1f;

        /// <summary>0 to 1: the flag's health against its current maximum.</summary>
        public float HealthFraction => MaxHealth.Value <= 0 ? 0f : Mathf.Clamp01(Health.Value / (float)MaxHealth.Value);

#if UNITY_SERVER
        /// <summary>Every hit goes through the siege armor before it reaches the flag's health.</summary>
        public override bool TakeDamage(long damage, DamageType source = DamageType.None, IEntity sourceEntity = null) {
            if (damage > 0 && source is not (DamageType.Reset or DamageType.Despawn)) {
                damage = armorState.Absorb(damage, Time.time, MaxHealth.Value, armor, DamageMultiplier);
                if (damage <= 0)
                    return !IsDead;
            }
            return base.TakeDamage(damage, source, sourceEntity);
        }

        /// <summary>Repairs <paramref name="fraction"/> of the flag's maximum health. Returns the health restored.</summary>
        [Server]
        public long Repair(float fraction) {
            if (IsDead || fraction <= 0f)
                return 0;
            long missing = MaxHealth.Value - Health.Value;
            long heal = System.Math.Min(missing, (long)Mathf.Ceil(MaxHealth.Value * Mathf.Min(1f, fraction)));
            if (heal > 0)
                HealHealth(heal);
            return System.Math.Max(0, heal);
        }

        /// <summary>Raises the flag's maximum health by <paramref name="fraction"/> of its authored health, healing the same amount.</summary>
        [Server]
        public long Reinforce(float fraction) {
            if (IsDead || fraction <= 0f)
                return 0;
            long extra = (long)Mathf.Round(maxHealth * fraction);
            if (extra > 0)
                HealMaxHealth(extra);
            return extra;
        }
#endif

        private void HandleDied(DamageType source, IEntity attacker) {
#if UNITY_SERVER
            if (IsServerStarted && IsSpawned)
                StartCoroutine(DespawnAfterDeath());
#endif
        }

#if UNITY_SERVER
        private IEnumerator DespawnAfterDeath() {
            // HealthComponent sends its death RPC after invoking OnDied. Waiting one frame
            // lets observers receive that state before the objective leaves the network.
            yield return null;
            if (IsSpawned)
                Despawn();
        }
#endif
    }
}
