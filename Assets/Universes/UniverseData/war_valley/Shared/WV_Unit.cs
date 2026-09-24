using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// A commandable War Valley unit: infantry, ground vehicle, or aircraft.
    /// <para>
    /// This component is the half both builds share - identity, team, replicated health, and the
    /// combat and movement numbers authored on the prefab. The behaviour that acts on those numbers
    /// lives in the server assembly and is attached at spawn, the same way <c>WV_NPC</c> is, so no
    /// server-only script is ever serialized into a prefab that a client build has to load.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(EffectsComponent), typeof(HealthComponent), typeof(WV_Owned))]
    public sealed class WV_Unit : EntityBase, ITeam {
        [Header("Identity")]
        [SerializeField] WV_UnitKind kind = WV_UnitKind.Infantry;
        [SerializeField] string displayName = "Unit";
        [SerializeField] TeamConfig team = new(TeamColor.Blue);

        [Header("Cost")]
        [Tooltip("Funds charged when this unit is queued at its production building.")]
        [SerializeField, Min(0)] int cost = 100;
        [Tooltip("Seconds the production building spends building this unit.")]
        [SerializeField, Min(0.5f)] float buildSeconds = 8f;

        [Header("Survivability")]
        [SerializeField, Min(1)] long maxHealth = 150;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] float moveSpeed = 14f;
        [Tooltip("Degrees per second. Aircraft bank toward their heading at this rate.")]
        [SerializeField, Min(1f)] float turnSpeed = 180f;

        [Header("Combat")]
        [SerializeField, Min(0f)] float attackRange = 12f;
        [Tooltip("Enemies inside this radius are engaged without an explicit attack order.")]
        [SerializeField, Min(0f)] float detectionRadius = 20f;
        [SerializeField, Min(1)] long attackDamage = 20;
        [SerializeField, Min(0.1f)] float attackCooldown = 1.4f;
        [SerializeField] DamageType attackDamageType = DamageType.Gun;
        [Tooltip("Muzzle or bomb-bay transform used as the origin of the attack effect.")]
        [SerializeField] Transform weaponMuzzle;

        [Header("Presentation")]
        [Tooltip("Ring or decal shown under this unit while the local player has it selected.")]
        [SerializeField] GameObject selectionIndicator;
        [Tooltip("Icon drawn for this unit in the production menu. Baked from the unit's own model.")]
        [SerializeField] Sprite icon;

        readonly SyncVar<TeamConfig> teamSync = new(new TeamConfig(TeamColor.Blue));

        static readonly List<WV_Unit> all = new();

        /// <summary>Every spawned unit. The client selection box and server order validation both read this.</summary>
        public static IReadOnlyList<WV_Unit> All => all;

        /// <summary>Raised on the client when a unit spawns or despawns, so HUD counts can refresh.</summary>
        public static event Action RosterChanged;

        WV_Owned owned;
        bool selectedLocally;

        public WV_UnitKind Kind => kind;
        public int Cost => cost;
        public float BuildSeconds => buildSeconds;
        public long UnitMaxHealth => maxHealth;
        public float MoveSpeed => moveSpeed;
        public float TurnSpeed => turnSpeed;
        public float AttackRange => attackRange;
        public float DetectionRadius => Mathf.Max(detectionRadius, attackRange);
        public long AttackDamage => attackDamage;
        public float AttackCooldown => attackCooldown;
        public DamageType AttackDamageType => attackDamageType;
        public Transform WeaponMuzzle => weaponMuzzle != null ? weaponMuzzle : transform;
        public WV_Owned Owned => owned;
        public Sprite Icon => icon;
        public bool IsAircraft => WV_Rules.IsAircraft(kind);

        public override string DisplayName {
            get => displayName;
            set => displayName = value;
        }

        public override TeamConfig Team => teamSync.Value ?? team;

        public TeamConfig GetTeam() => Team;

        /// <summary>Client-only selection state. Selection never leaves the machine that made it.</summary>
        public bool SelectedLocally {
            get => selectedLocally;
            set {
                if (selectedLocally == value)
                    return;
                selectedLocally = value;
                if (selectionIndicator != null)
                    selectionIndicator.SetActive(value);
            }
        }

        protected override void Awake() {
            base.Awake();
            owned = GetComponent<WV_Owned>();
            if (selectionIndicator != null)
                selectionIndicator.SetActive(false);
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            all.Add(this);
            teamSync.OnChange += HandleTeamChanged;
            RosterChanged?.Invoke();
        }

        public override void OnStopNetwork() {
            teamSync.OnChange -= HandleTeamChanged;
            all.Remove(this);
            SelectedLocally = false;
            RosterChanged?.Invoke();
            base.OnStopNetwork();
        }

        void HandleTeamChanged(TeamConfig previous, TeamConfig next, bool asServer) => RaiseTeamChanged();

        /// <summary>Units this client commands, used to seed box selection and control groups.</summary>
        public static void CollectOwnedBy(int clientId, List<WV_Unit> results) {
            results.Clear();
            foreach (WV_Unit unit in all) {
                if (!unit.IsDead && unit.owned != null && unit.owned.IsOwnedBy(clientId))
                    results.Add(unit);
            }
        }

#if UNITY_SERVER
        [Server]
        public void SetTeam(TeamConfig teamConfig) {
            teamSync.Value = teamConfig ?? new TeamConfig(TeamColor.Blue);
        }
#endif
    }
}
