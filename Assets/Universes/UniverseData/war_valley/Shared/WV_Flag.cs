using System.Collections;
using FishNet.Object;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>The networked War Valley objective attacked by hostile teams.</summary>
    [RequireComponent(typeof(EffectsComponent), typeof(HealthComponent))]
    public sealed class WV_Flag : Entity, ITeam {
        [SerializeField] private string displayName = "War Valley Flag";
        [SerializeField] private TeamConfig team = new(TeamColor.Blue);
        [SerializeField, Min(1)] private long maxHealth = 1000;

        public static WV_Flag Instance { get; private set; }

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
            Init(maxHealth);
        }

        [Server]
        public void SetTeam(TeamConfig teamConfig) {
            team = teamConfig ?? new TeamConfig(TeamColor.Blue);
        }
#endif

        public TeamConfig GetTeam() => Team;

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
