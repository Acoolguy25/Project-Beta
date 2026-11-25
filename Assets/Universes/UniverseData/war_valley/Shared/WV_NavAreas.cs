using RyanAssets.Shared.Declarations;
using UnityEngine;
using UnityEngine.AI;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// The NavMesh areas War Valley's walls, gates and map frontier use to decide who may path where.
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

        /// <summary>
        /// The outer band of the map between the enemy spawns and the commanders' ground, marked by
        /// <see cref="WV_PlayArea"/>'s volumes. Only the waves path across it, so the commanders'
        /// troops and vehicles never wander out to the map edge.
        /// </summary>
        public const int Frontier = 5;

        /// <summary>The areas an agent fighting for <paramref name="side"/> may path across.</summary>
        public static int GetAreaMask(TeamColor side) =>
            side == WV_Alliances.GetWaveSide()
                ? NavMesh.AllAreas & ~(1 << GatePassage)
                : NavMesh.AllAreas & ~(1 << Breach) & ~(1 << Frontier);

        /// <summary>
        /// Restricts an agent to its side's routes. Called once a spawned agent has been given its
        /// team, before it is first sent anywhere.
        /// </summary>
        public static void Apply(NavMeshAgent agent, TeamConfig team) {
            if (agent != null && team != null)
                agent.areaMask = GetAreaMask(team.realTeam);
        }

        /// <summary>Attempts at a random spot before a spawn falls back to the middle of its muster point.</summary>
        const int SpawnAttempts = 12;

        /// <summary>
        /// A spot on the NavMesh within <paramref name="radius"/> of <paramref name="center"/> for a
        /// new soldier. A random point near a map edge can land beyond the mesh (or the map), which
        /// strands whoever spawns there; each candidate is snapped onto the mesh for
        /// <paramref name="areaMask"/> and rejected if it falls outside the map, and the muster point
        /// itself is the last resort. Returns false only when no mesh exists near the centre.
        /// </summary>
        public static bool TryFindSpawnPosition(Vector3 center, float radius, int areaMask, out Vector3 position) {
            WV_PlayArea playArea = WV_PlayArea.Current;
            for (int attempt = 0; attempt < SpawnAttempts; attempt++) {
                Vector2 offset = Random.insideUnitCircle * radius;
                var candidate = new Vector3(center.x + offset.x, center.y, center.z + offset.y);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, SpawnSnapDistance, areaMask))
                    continue;
                if (playArea != null && !playArea.MapArea.Contains(new Vector2(hit.position.x, hit.position.z)))
                    continue;
                position = hit.position;
                return true;
            }
            if (NavMesh.SamplePosition(center, out NavMeshHit fallback, radius + SpawnSnapDistance, areaMask)) {
                position = fallback.position;
                return true;
            }
            position = center;
            return false;
        }

        /// <summary>How far from its spawn point a new soldier may be moved to stand on the NavMesh (m).</summary>
        public const float SpawnSnapDistance = 10f;

        /// <summary>
        /// Puts a freshly spawned soldier's agent on the NavMesh at its spawn point. The agent wakes up
        /// where the prefab was instantiated and is only then moved by its transform, which leaves it
        /// off the NavMesh: it never gets a path, so a wave would stand at the valley edge forever.
        /// Returns false (and warns) when there is no NavMesh near the point.
        /// </summary>
        public static bool PlaceOnNavMesh(NavMeshAgent agent, Vector3 spawnPosition) {
            if (agent == null || !agent.enabled || agent.isOnNavMesh)
                return agent != null && agent.isOnNavMesh;
            if (NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, SpawnSnapDistance, agent.areaMask))
                return agent.Warp(hit.position);
            Debug.LogWarning($"War Valley: no NavMesh within {SpawnSnapDistance} m of {spawnPosition}; {agent.name} cannot move.", agent);
            return false;
        }
    }
}
