using RyanAssets.Shared.Declarations;
using UnityEngine.AI;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// The NavMesh areas War Valley's walls and gates use to decide who may path through them.
    /// <para>
    /// A wall or gate carves its footprint out of the NavMesh and bridges it with a link. Which
    /// agents may use a link is decided by its area and each agent's area mask, so one wall offers
    /// two different routes: a <see cref="Breach"/> link the waves may take - stopping to destroy
    /// the wall first - and, on a gate, a <see cref="GatePassage"/> link the commanders' own troops
    /// and vehicles may take while the gate lets them through. Neither side can use the other's.
    /// </para>
    /// <para>
    /// The indices match the names given in Project Settings &gt; Navigation &gt; Areas. Walls and
    /// gates are always built by commanders, so the split follows the two Survival sides from
    /// <see cref="WV_Alliances"/>.
    /// </para>
    /// </summary>
    public static class WV_NavAreas {
        /// <summary>Link area through an open gate. Only the commanders' side paths over it.</summary>
        public const int GatePassage = 3;

        /// <summary>Costly link area across a wall or gate that has to be destroyed first. Only the waves path over it.</summary>
        public const int Breach = 4;

        /// <summary>The areas an agent fighting for <paramref name="side"/> may path across.</summary>
        public static int GetAreaMask(TeamColor side) =>
            side == WV_Alliances.GetWaveSide()
                ? NavMesh.AllAreas & ~(1 << GatePassage)
                : NavMesh.AllAreas & ~(1 << Breach);

        /// <summary>
        /// Restricts an agent to its side's routes. Called once a spawned agent has been given its
        /// team, before it is first sent anywhere.
        /// </summary>
        public static void Apply(NavMeshAgent agent, TeamConfig team) {
            if (agent != null && team != null)
                agent.areaMask = GetAreaMask(team.realTeam);
        }
    }
}
