using FishNet.Broadcast;
using UnityEngine;
using RyanAssets.Shared.Declarations;

namespace Universes.UniverseData.dot_invaders {
    public static class DI_Rules {
        static readonly TeamColor[] TeamOrder = {
            TeamColor.Blue, TeamColor.Red, TeamColor.Green, TeamColor.Orange,
            TeamColor.Purple, TeamColor.Cyan, TeamColor.Pink, TeamColor.Lime
        };
        static readonly TeamColor[] NpcTeamOrder = {
            TeamColor.Purple, TeamColor.Cyan, TeamColor.Yellow, TeamColor.Grey
        };

        public static TeamColor GetTeamColor(int teamId) =>
            teamId < 0 ? TeamColor.None : teamId >= 100
                ? NpcTeamOrder[(teamId - 100) % NpcTeamOrder.Length]
                : TeamOrder[teamId % TeamOrder.Length];

        public const float DefaultNPCIntelligence = 60f;
        // Only automatic training is capped. Incoming troops may stack without a cap.
        public const int DefaultMaxCapacity = 50;
        public const float DefaultTurretRange = 24f;
        public const float DefaultTurretFireRate = 0.7f;
        public const float DefaultMoveSpeed = 12f;
        public const float DefaultSpeedBaseBonus = 0.25f;
        public const float DefaultProductionSpeed = 1.25f;
        public const float DefaultSendInterval = 0.4f;
        public const float DefaultNpcAggression = 50f;
        public const int DefaultSuperMaxCapacity = 200;
        public const float DefaultSuperProductionSpeed = 12.5f;
        public const float DefaultSuperSpeedupSeconds = 12f;
        public const int BasePower = 25;

        // MoveSpeed and SendInterval are tuned as multipliers of their defaults.
        public const float MinMoveSpeedMultiplier = 0.05f;
        public const float MaxMoveSpeedMultiplier = 10f;
        public const float MinSendIntervalMultiplier = 0.125f;
        public const float MaxSendIntervalMultiplier = 10f;

        public static System.Collections.Generic.SortedDictionary<int, DI_TeamPower> CalculatePower(DI_StateBroadcast state) {
            var teams = new System.Collections.Generic.SortedDictionary<int, DI_TeamPower>();
            if (state.baseTeams != null && state.baseTroops != null) {
                for (int i = 0; i < Mathf.Min(state.baseTeams.Length, state.baseTroops.Length); i++) {
                    int team = state.baseTeams[i];
                    if (team < 0) continue;
                    teams.TryGetValue(team, out DI_TeamPower power);
                    power.bases++;
                    // Pending troops are still in the garrison; do not count them twice.
                    power.troops += state.baseTroops[i];
                    teams[team] = power;
                }
            }
            if (state.dotTeams != null) {
                foreach (int team in state.dotTeams) {
                    if (team < 0) continue;
                    teams.TryGetValue(team, out DI_TeamPower power);
                    power.troops++;
                    teams[team] = power;
                }
            }
            return teams;
        }

        // A team's troops move faster for every speed base it holds. Planning and
        // simulation must agree on this or an NPC mis-times every attack it makes.
        public static float TeamMoveSpeed(float moveSpeed, int speedBases, float speedBaseBonus) =>
            moveSpeed * (1f + Mathf.Max(0, speedBases) * Mathf.Max(0f, speedBaseBonus));

        public static float SuperProductionRate(float uninterruptedSeconds, float peakRate, float speedupSeconds) {
            float startRate = Mathf.Min(1.25f, peakRate);
            float progress = Mathf.Clamp01(uninterruptedSeconds / Mathf.Max(0.01f, speedupSeconds));
            return startRate * Mathf.Pow(peakRate / startRate, progress);
        }

        public static float SuperProductionOverInterval(float uninterruptedSeconds, float duration,
            float peakRate, float speedupSeconds) {
            if (duration <= 0f) return 0f;
            float rampDuration = Mathf.Max(0.01f, speedupSeconds);
            float startRate = Mathf.Min(1.25f, peakRate);
            float ratio = peakRate / startRate;
            float rampEnd = Mathf.Min(rampDuration, uninterruptedSeconds + duration);
            float rampStart = Mathf.Min(rampDuration, uninterruptedSeconds);
            float produced = ratio > 1f
                ? startRate * rampDuration / Mathf.Log(ratio) *
                    (Mathf.Pow(ratio, rampEnd / rampDuration) - Mathf.Pow(ratio, rampStart / rampDuration))
                : startRate * (rampEnd - rampStart);
            return produced + (duration - (rampEnd - rampStart)) * peakRate;
        }

        public static int[] AppendRouteWaypoint(Vector2[] positions, int[] sources, int[] targets, int[] route, int waypoint) {
            if (route == null || route.Length == 0 || positions == null || waypoint < 0 || waypoint >= positions.Length)
                return route ?? System.Array.Empty<int>();
            int existing = System.Array.IndexOf(route, waypoint);
            if (existing >= 0) {
                var shortened = new int[existing + 1];
                System.Array.Copy(route, shortened, shortened.Length);
                return shortened;
            }
            int[] leg = FindRoute(positions, sources, targets, route[route.Length - 1], waypoint, route);
            if (leg.Length < 2)
                return route;
            var extended = new int[route.Length + leg.Length - 1];
            System.Array.Copy(route, extended, route.Length);
            System.Array.Copy(leg, 1, extended, route.Length, leg.Length - 1);
            return extended;
        }

        public static int[] FindRoute(Vector2[] positions, int[] sources, int[] targets, int start, int end, int[] excluded = null) {
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
            if (excluded != null)
                foreach (int baseId in excluded)
                    if (baseId >= 0 && baseId < count && baseId != start)
                        visited[baseId] = true;
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

    public struct DI_TeamPower {
        public int troops;
        public int bases;
        public long Power => (long)troops + (long)bases * DI_Rules.BasePower;
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
        public bool[] baseSuperProducers;
        public bool[] baseSpeedBases;
        public float[] baseProductionCharge;
        public float[] baseProductionRates;
        public float superProductionSpeed;
        public float superSpeedupSeconds;
        public int superMaxCapacity;
        // Appended so older field order stays intact for existing readers.
        public float productionSpeed;
        public float sendInterval;
        public float speedBaseBonus;
        public float npcAggression;
    }
}
