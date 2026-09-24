using System.Collections.Generic;
using FishNet.Object;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// A research station: the building a side needs before it can research at all, and one more of
    /// which makes research faster.
    /// <para>
    /// Each finished station adds <see cref="ResearchRate"/> to its side's research throughput
    /// (<see cref="WV_TechTree"/> splits that throughput across the projects running). Stations are
    /// counted per real team rather than per commander, because research is shared by allies: a
    /// station an ally built speeds up everyone's projects.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(StructureComponent), typeof(WV_Constructable), typeof(WV_Owned))]
    public sealed class WV_ResearchBuilding : NetworkBehaviour {
        [Tooltip("Research throughput this station adds to its side. 1 runs a lone project in its " +
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

        /// <summary>The real team this station researches for.</summary>
        public TeamColor Side => structure.Team != null ? structure.Team.realTeam : TeamColor.None;

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

        /// <summary>Combined research throughput of every finished station on <paramref name="side"/>.</summary>
        public static float GetResearchRate(TeamColor side) {
            float total = 0f;
            foreach (WV_ResearchBuilding station in all) {
                if (station.IsOperational && station.Side == side)
                    total += station.researchRate;
            }
            return total;
        }

        /// <summary>How many finished stations <paramref name="side"/> has.</summary>
        public static int CountOperational(TeamColor side) {
            int count = 0;
            foreach (WV_ResearchBuilding station in all) {
                if (station.IsOperational && station.Side == side)
                    count++;
            }
            return count;
        }
    }
}
