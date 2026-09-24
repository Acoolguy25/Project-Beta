using System.Collections.Generic;
using FishNet.Object;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// A research station: the building a commander needs before they can research at all, and one
    /// more of which makes their research faster.
    /// <para>
    /// Each finished station adds <see cref="ResearchRate"/> to its owner's research throughput
    /// (<see cref="WV_TechTree"/> splits that throughput across the projects they are running).
    /// Research is per commander, so a station only ever speeds up the research of whoever built it.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(StructureComponent), typeof(WV_Constructable), typeof(WV_Owned))]
    public sealed class WV_ResearchBuilding : NetworkBehaviour {
        [Tooltip("Research throughput this station adds to its owner. 1 runs a lone project in its " +
                 "authored research time; stations stack linearly.")]
        [SerializeField, Min(0.1f)] float researchRate = 1f;

        static readonly List<WV_ResearchBuilding> all = new();

        /// <summary>Every spawned research station, so research speed is totalled without a scene search.</summary>
        public static IReadOnlyList<WV_ResearchBuilding> All => all;

        StructureComponent structure;
        WV_Constructable constructable;
        WV_Owned owned;

        public float ResearchRate => researchRate;
        public WV_Owned Owned => owned;
        public bool IsOperational => constructable.IsOperational && !structure.IsDead;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        void Awake() {
            structure = GetComponent<StructureComponent>();
            constructable = GetComponent<WV_Constructable>();
            owned = GetComponent<WV_Owned>();
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            all.Add(this);
        }

        public override void OnStopNetwork() {
            all.Remove(this);
            base.OnStopNetwork();
        }

        /// <summary>Combined research throughput of every finished station a commander owns.</summary>
        public static float GetResearchRate(int clientId) {
            float total = 0f;
            foreach (WV_ResearchBuilding station in all) {
                if (station.IsOperational && station.owned.IsOwnedBy(clientId))
                    total += station.researchRate;
            }
            return total;
        }

        /// <summary>How many finished stations a commander owns.</summary>
        public static int CountOperational(int clientId) {
            int count = 0;
            foreach (WV_ResearchBuilding station in all) {
                if (station.IsOperational && station.owned.IsOwnedBy(clientId))
                    count++;
            }
            return count;
        }
    }
}
