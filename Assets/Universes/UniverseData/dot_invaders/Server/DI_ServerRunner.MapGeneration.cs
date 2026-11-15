#if UNITY_SERVER
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    // Match setup: arena generation, links and special bases.
    public sealed partial class DI_ServerRunner {
        void InitializeMatch() {
            // Match bonuses come only from the newly generated bases.
            teamSpeedBases.Clear();
            playerDamageMultipliers.Clear();
            playerProductionMultipliers.Clear();
            simulationAccumulator = 0f;
            snapshotTimer = 0f;
            bases.Clear();
            links.Clear();
            dots.Clear();
            npcTeams.Clear();
            playerTeams.Clear();
            dotInvadersTeams.Clear();
            announcedEliminations.Clear();
            nextTeamId = 0;
            nextDotId = 0;
            revision = 0;
            winningTeamId = -1;
            secondsRemaining = matchDurationSeconds;
            matchInProgress = false;

            GenerateBases();
            GenerateLinks();
            AssignTurrets();
            AssignSuperBases();
            AssignSpeedBases();
            initialized = true;

            foreach (PlayerData player in PlayerData.Players.Values)
                AssignPlayer(player);

            SpawnNpcTeams();
            matchInProgress = true;
            UpdateTopMessage();
            BroadcastState();
        }

        void GenerateBases() {
            var random = new System.Random(unchecked(Environment.TickCount * 397 ^ DateTime.UtcNow.Ticks.GetHashCode()));
            int attempts = 0;

            while (bases.Count < BaseCount && attempts++ < 5000) {
                var candidate = new Vector2(
                    Mathf.Lerp(-ArenaHalfWidth + ArenaEdgeClearance, ArenaHalfWidth - ArenaEdgeClearance,
                        (float)random.NextDouble()),
                    Mathf.Lerp(-ArenaHalfHeight + ArenaEdgeClearance, ArenaHalfHeight - ArenaEdgeClearance,
                        (float)random.NextDouble()));

                bool overlaps = false;
                for (int i = 0; i < bases.Count; i++) {
                    if (Vector2.Distance(candidate, bases[i].position) < MinimumBaseSpacing) {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                    bases.Add(new BaseState { position = candidate });
            }

            // Extremely unlikely fallback that still guarantees a playable board.
            for (int i = bases.Count; i < BaseCount; i++) {
                float angle = i * Mathf.PI * 2f / BaseCount;
                bases.Add(new BaseState {
                    position = new Vector2(Mathf.Cos(angle) * 82f, Mathf.Sin(angle) * 46f)
                });
            }
        }

        void GenerateLinks() {
            var edgeKeys = new HashSet<ulong>();

            // Prim's minimum spanning tree keeps the board connected without the
            // long, crossing roads introduced by random base creation order.
            var connected = new HashSet<int> { 0 };
            while (connected.Count < bases.Count) {
                int source = -1, target = -1;
                float shortest = float.MaxValue;
                foreach (int i in connected) {
                    for (int j = 0; j < bases.Count; j++) {
                        if (connected.Contains(j))
                            continue;
                        float distance = (bases[i].position - bases[j].position).sqrMagnitude;
                        if (distance < shortest) {
                            shortest = distance;
                            source = i;
                            target = j;
                        }
                    }
                }
                if (target < 0)
                    break;
                AddLink(source, target, edgeKeys);
                connected.Add(target);
            }

            // Dense local alternatives; the spanning tree still connects any isolated
            // clusters. Crossings are allowed, but roads cannot pass through a base.
            for (int i = 0; i < bases.Count; i++) {
                var neighbors = new List<int>();
                for (int j = 0; j < bases.Count; j++) {
                    if (i != j)
                        neighbors.Add(j);
                }
                neighbors.Sort((a, b) =>
                    (bases[i].position - bases[a].position).sqrMagnitude.CompareTo(
                        (bases[i].position - bases[b].position).sqrMagnitude));

                for (int j = 0; j < Mathf.Min(nearbyPathsPerBase, neighbors.Count); j++) {
                    int neighbor = neighbors[j];
                    if (Vector2.Distance(bases[i].position, bases[neighbor].position) <= maximumLocalPathLength &&
                        !PassesThroughBase(i, neighbor))
                        AddLink(i, neighbor, edgeKeys);
                }
            }
        }

        bool PassesThroughBase(int a, int b) {
            Vector2 start = bases[a].position, end = bases[b].position;
            Vector2 direction = end - start;
            for (int i = 0; i < bases.Count; i++) {
                if (i == a || i == b)
                    continue;
                float along = Mathf.Clamp01(Vector2.Dot(bases[i].position - start, direction) / Mathf.Max(0.001f, direction.sqrMagnitude));
                if ((bases[i].position - (start + direction * along)).sqrMagnitude < 4.5f * 4.5f)
                    return true;
            }
            return false;
        }

        void AssignTurrets() {
            // Two separated objectives near the middle of each half of the board.
            int count = Mathf.Clamp(turretBaseCount, 1, 2);
            for (int t = 0; t < count; t++) {
                Vector2 objective = new Vector2(count == 1 ? 0f : (t == 0 ? -35f : 35f), 0f);
                int best = -1;
                float distance = float.MaxValue;
                for (int i = 0; i < bases.Count; i++) {
                    float candidate = (bases[i].position - objective).sqrMagnitude;
                    if (!bases[i].isTurret && candidate < distance) {
                        best = i;
                        distance = candidate;
                    }
                }
                if (best >= 0)
                    bases[best].isTurret = true;
            }
        }

        void AssignSuperBases() {
            for (int t = 0; t < 2; t++) {
                Vector2 objective = new Vector2(0f, t == 0 ? -35f : 35f);
                int best = -1;
                float distance = float.MaxValue;
                for (int i = 0; i < bases.Count; i++) {
                    float candidate = (bases[i].position - objective).sqrMagnitude;
                    if (!bases[i].isTurret && !bases[i].isSuperProducer && !bases[i].isSpeedBase && candidate < distance) {
                        best = i;
                        distance = candidate;
                    }
                }
                if (best >= 0)
                    bases[best].isSuperProducer = true;
            }
        }

        void AssignSpeedBases() {
            // Distribute speed bases across the map
            for (int i = 0; i < SpeedBaseCount; i++) {
                float xPos = 0f;
                if (SpeedBaseCount > 1) {
                    xPos = Mathf.Lerp(-ArenaHalfWidth + 20f, ArenaHalfWidth - 20f, (float)i / (SpeedBaseCount - 1));
                }
                Vector2 objective = new Vector2(xPos, 0f);
                int best = -1;
                float distance = float.MaxValue;
                for (int b = 0; b < bases.Count; b++) {
                    // Skip bases that are already turrets, super producers, or other speed bases
                    if (!bases[b].isTurret && !bases[b].isSuperProducer && !bases[b].isSpeedBase) {
                        float candidate = (bases[b].position - objective).sqrMagnitude;
                        if (candidate < distance) {
                            best = b;
                            distance = candidate;
                        }
                    }
                }
                if (best >= 0)
                    bases[best].isSpeedBase = true;
            }
        }

        int Capacity(BaseState state) => state.isSuperProducer ? superMaxCapacity : maxCapacity;

        float ProductionRate(BaseState state) => state.isSuperProducer
            ? DI_Rules.SuperProductionRate(state.uninterruptedSeconds, superProductionSpeed, superSpeedupSeconds)
            : productionSpeed;

        static void InterruptProduction(BaseState state) {
            state.uninterruptedSeconds = 0f;
            state.actionTimer = 0f;
        }

        void AddLink(int a, int b, HashSet<ulong> edgeKeys) {
            int source = Mathf.Min(a, b);
            int target = Mathf.Max(a, b);
            ulong key = ((ulong)(uint)source << 32) | (uint)target;
            if (edgeKeys.Add(key))
                links.Add(new Vector2Int(source, target));
        }
    }
}
#endif
