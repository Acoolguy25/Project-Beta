using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    public static class DI_Rules {
        public const float DefaultNPCIntelligence = 60f;
        // Only automatic training is capped. Incoming troops may stack without a cap.
        public const int DefaultMaxCapacity = 50;
        public const float DefaultTurretRange = 24f;
        public const float DefaultTurretFireRate = 0.7f;
        public const float DefaultMoveSpeed = 12f;

        public static int[] FindRoute(Vector2[] positions, int[] sources, int[] targets, int start, int end) {
            if (positions == null || sources == null || targets == null || start < 0 || end < 0 ||
                start >= positions.Length || end >= positions.Length || start == end)
                return System.Array.Empty<int>();
            int count = positions.Length;
            var distances = new float[count];
            var previous = new int[count];
            var visited = new bool[count];
            for (int i = 0; i < count; i++) {
                distances[i] = float.PositiveInfinity;
                previous[i] = -1;
            }
            distances[start] = 0f;
            for (int step = 0; step < count; step++) {
                int current = -1;
                for (int i = 0; i < count; i++)
                    if (!visited[i] && (current < 0 || distances[i] < distances[current]))
                        current = i;
                if (current < 0 || float.IsPositiveInfinity(distances[current]))
                    break;
                if (current == end) {
                    var path = new System.Collections.Generic.List<int>();
                    for (int at = end; at >= 0; at = previous[at])
                        path.Add(at);
                    path.Reverse();
                    return path.ToArray();
                }
                visited[current] = true;
                for (int i = 0; i < Mathf.Min(sources.Length, targets.Length); i++) {
                    int neighbor = sources[i] == current ? targets[i] : targets[i] == current ? sources[i] : -1;
                    if (neighbor < 0 || neighbor >= count || visited[neighbor])
                        continue;
                    float distance = distances[current] + Vector2.Distance(positions[current], positions[neighbor]);
                    if (distance < distances[neighbor]) {
                        distances[neighbor] = distance;
                        previous[neighbor] = current;
                    }
                }
            }
            return System.Array.Empty<int>();
        }
    }

    // Both builds must use the same protocol version and field order.
    public struct DI_SendRequest : IBroadcast {
        public int sourceBaseId;
        public int targetBaseId;
        public int[] route;
    }

    public struct DI_Route {
        public int[] baseIds;
    }

    public struct DI_StateBroadcast : IBroadcast {
        public int revision;
        public int yourClientId;
        public int yourTeamId;
        public int secondsRemaining;
        public bool matchEnded;
        public int winningTeamId;
        public float npcIntelligence;
        public float turretRange;
        public float turretFireRate;
        public int maxCapacity;
        public float moveSpeed;
        public Vector2[] basePositions;
        public int[] baseTroops;
        public int[] baseOwners;
        public int[] baseTeams;
        public int[] basePendingTroops;
        public int[] linkSources;
        public int[] linkTargets;
        public int[] dotIds;
        public Vector2[] dotPositions;
        public int[] dotTeams;
        public bool[] baseTurrets;
        public int[] turretShotSequences;
        public Vector2[] turretShotPositions;
        public DI_Route[] baseRoutes;
    }
}
