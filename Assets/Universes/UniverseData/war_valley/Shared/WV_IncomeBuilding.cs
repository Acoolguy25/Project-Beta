using System.Collections.Generic;
using FishNet.Object;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// An economy structure. Mineshafts and refineries pay their commander a fixed sum on a shared
    /// cadence once they finish building, which is the only way funds enter the match.
    /// </summary>
    [RequireComponent(typeof(StructureComponent), typeof(WV_Constructable), typeof(WV_Owned))]
    public sealed class WV_IncomeBuilding : NetworkBehaviour {
        [Tooltip("Funds paid to the owner every WV_Rules.IncomeTickSeconds once operational.")]
        [SerializeField, Min(1)] int incomePerTick = 25;

        static readonly List<WV_IncomeBuilding> all = new();

        /// <summary>Every spawned income building, so the server can total a player's rate without a scene search.</summary>
        public static IReadOnlyList<WV_IncomeBuilding> All => all;

        WV_Constructable constructable;
        WV_Owned owned;
        StructureComponent structure;
        float nextPayoutTime;

        public int IncomePerTick => incomePerTick;

        /// <summary>The headline rate the HUD shows, derived from the single per-tick setting.</summary>
        public int IncomePerMinute => Mathf.RoundToInt(incomePerTick * (60f / WV_Rules.IncomeTickSeconds));

        public bool IsPaying => constructable.IsOperational && !structure.IsDead;

        void Awake() {
            constructable = GetComponent<WV_Constructable>();
            owned = GetComponent<WV_Owned>();
            structure = GetComponent<StructureComponent>();
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            all.Add(this);
            IncomeChanged?.Invoke();
        }

        public override void OnStopNetwork() {
            all.Remove(this);
            IncomeChanged?.Invoke();
            base.OnStopNetwork();
        }

        /// <summary>Raised whenever the set of income buildings changes, so totals can be republished.</summary>
        public static event System.Action IncomeChanged;

        /// <summary>Total per-minute rate a commander is currently earning across all their sites.</summary>
        public static int GetIncomePerMinute(int clientId) {
            int total = 0;
            foreach (WV_IncomeBuilding building in all) {
                if (building.IsPaying && building.owned.IsOwnedBy(clientId))
                    total += building.IncomePerMinute;
            }
            return total;
        }

#if UNITY_SERVER
        void Update() {
            if (!IsServerStarted || !IsPaying)
                return;

            float now = NetworkHelper.ServerTime;
            if (nextPayoutTime <= 0f) {
                // Stagger the first payout from completion rather than from spawn, so a site does not
                // pay out the instant its scaffold comes down.
                nextPayoutTime = now + WV_Rules.IncomeTickSeconds;
                return;
            }

            if (now < nextPayoutTime)
                return;

            nextPayoutTime = now + WV_Rules.IncomeTickSeconds;
            WV_Economy.Instance?.Credit(owned.OwnerClientId, incomePerTick);
        }
#endif
    }
}
