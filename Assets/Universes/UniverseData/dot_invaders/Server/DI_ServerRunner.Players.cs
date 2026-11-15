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
    // Player/NPC team assignment, send orders and route validation.
    public sealed partial class DI_ServerRunner {
        void AssignPlayer(PlayerData player) {
            if (!initialized || player == null || !player.Owner.IsValid)
                return;

            int clientId = player.Owner.ClientId;
            if (!playerTeams.TryGetValue(clientId, out int teamId)) {
                teamId = nextTeamId++;
                playerTeams.Add(clientId, teamId);
            }
            dotInvadersTeams.Add(teamId);

            player.SetPlayerTeam(new TeamConfig(DI_Rules.GetTeamColor(teamId)));

            for (int i = 0; i < bases.Count; i++) {
                if (bases[i].ownerClientId == clientId)
                    return;
            }

            int baseId = FindSpawnBase();
            if (baseId < 0)
                return;

            BaseState home = bases[baseId];
            home.ownerClientId = clientId;
            home.teamId = teamId;
            home.troops = StartingTroops;
            home.damageTaken = 0f;
            home.pendingTarget = -1;
            home.pendingTroops = 0;
            home.pendingRoute = null;
            home.actionTimer = 0f;
            home.productionDelay = 0f;
            home.uninterruptedSeconds = 0f;
            if (matchInProgress)
                BroadcastState();
        }

        void SpawnNpcTeams() {
            for (int teamIndex = 0; teamIndex < NpcTeamCount; teamIndex++) {
                var npcTeam = new NpcTeamState {
                    ownerClientId = -2 - teamIndex,
                    teamId = NpcTeamIdStart + teamIndex,
                    thinkTimer = teamIndex * 0.35f
                };
                npcTeams.Add(npcTeam);
                dotInvadersTeams.Add(npcTeam.teamId);

                for (int baseIndex = 0; baseIndex < NpcBasesPerTeam; baseIndex++) {
                    int spawnBase = FindSpawnBase();
                    if (spawnBase < 0)
                        break;

                    BaseState state = bases[spawnBase];
                    state.ownerClientId = npcTeam.ownerClientId;
                    state.teamId = npcTeam.teamId;
                    state.troops = NpcStartingTroops;
                    state.pendingTarget = -1;
                    state.pendingTroops = 0;
                    state.pendingRoute = null;
                    state.actionTimer = 0f;
                    state.productionDelay = 0f;
                    state.uninterruptedSeconds = 0f;
                }
            }
        }

        int FindSpawnBase() {
            int best = -1;
            float bestDistance = float.MinValue;

            for (int i = 0; i < bases.Count; i++) {
                if (bases[i].teamId >= 0 || bases[i].isTurret || bases[i].isSuperProducer || bases[i].isSpeedBase)
                    continue;

                float nearestOwnedDistance = float.MaxValue;
                for (int j = 0; j < bases.Count; j++) {
                    if (bases[j].teamId >= 0) {
                        nearestOwnedDistance = Mathf.Min(nearestOwnedDistance,
                            (bases[i].position - bases[j].position).sqrMagnitude);
                    }
                }

                if (nearestOwnedDistance > bestDistance) {
                    bestDistance = nearestOwnedDistance;
                    best = i;
                }
            }

            return best;
        }

        void OnPlayerRemoved(PlayerData player) {
            if (player == null)
                return;

            int clientId = player.Owner.ClientId;
            playerTeams.Remove(clientId);
            playerDamageMultipliers.Remove(clientId);
            playerProductionMultipliers.Remove(clientId);

            for (int i = 0; i < bases.Count; i++) {
                BaseState state = bases[i];
                if (state.ownerClientId != clientId)
                    continue;

                state.ownerClientId = -1;
                state.teamId = -1;
                state.troops = NeutralTroops;
                state.damageTaken = 0f;
                state.pendingTarget = -1;
                state.pendingTroops = 0;
                state.pendingRoute = null;
                state.actionTimer = 0f;
                state.productionDelay = 0f;
                state.uninterruptedSeconds = 0f;
            }

            dots.RemoveAll(dot => dot.ownerClientId == clientId);
            CheckEliminatedTeams();
            BroadcastState();
            CheckEndConditions();
        }

        void OnSendRequest(NetworkConnection connection, DI_SendRequest request, FishNet.Transporting.Channel channel) {
            if (!initialized || !matchInProgress || !connection.IsValid ||
                request.sourceBaseId < 0 || request.sourceBaseId >= bases.Count)
                return;

            BaseState source = bases[request.sourceBaseId];
            if (source.ownerClientId != connection.ClientId)
                return;

            if (request.targetBaseId == -1) {
                CancelOrders(request.sourceBaseId, connection.ClientId);
                BroadcastState();
                return;
            }

            int[] route = request.route ?? new[] { request.sourceBaseId, request.targetBaseId };
            if (!IsValidRoute(request.sourceBaseId, request.targetBaseId, route))
                return;

            if (source.pendingTroops > 0) {
                source.pendingTarget = route[1];
                source.pendingRoute = (int[])route.Clone();
                BroadcastState();
                return;
            }

            if (source.troops <= 0)
                return;

            QueueSend(source, route[1], source.troops, sendInterval);
            source.pendingRoute = (int[])route.Clone();
            BroadcastState();
        }

        bool IsValidRoute(int source, int target, int[] route) {
            if (route == null || route.Length < 2 || route.Length > bases.Count ||
                route[0] != source || route[route.Length - 1] != target)
                return false;
            var visited = new HashSet<int>();
            for (int i = 0; i < route.Length; i++) {
                if (route[i] < 0 || route[i] >= bases.Count || !visited.Add(route[i]) ||
                    i > 0 && !AreNeighbors(route[i - 1], route[i]))
                    return false;
            }
            return true;
        }

        void CancelOrders(int sourceBaseId, int ownerClientId) {
            ClearSend(bases[sourceBaseId]);
            // Already moving troops finish their current leg, then stop routing.
            foreach (DotState dot in dots)
                if (dot.ownerClientId == ownerClientId && dot.originBaseId == sourceBaseId)
                    dot.route = null;
        }

        bool AreNeighbors(int source, int target) {
            for (int i = 0; i < links.Count; i++) {
                Vector2Int link = links[i];
                if ((link.x == source && link.y == target) || (link.x == target && link.y == source))
                    return true;
            }
            return false;
        }
    }
}
#endif
