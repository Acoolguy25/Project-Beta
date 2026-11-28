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
            player.SetPlayerTeam(new TeamConfig(DI_Rules.GetTeamColor(teamId)));

            for (int i = 0; i < bases.Count; i++) {
                if (bases[i].ownerClientId == clientId) {
                    dotInvadersTeams.Add(teamId);
                    return;
                }
            }

            // A team only competes (and can be announced eliminated) once it holds a base.
            int baseId = FindSpawnBase();
            if (baseId < 0)
                return;
            dotInvadersTeams.Add(teamId);

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
            // The home base counts as held, so a player who never captures still shows "Most held 1".
            matchRecords.TryGetValue(clientId, out MatchRecord record);
            record.mostBasesHeld = Mathf.Max(record.mostBasesHeld, 1);
            matchRecords[clientId] = record;
            EnterRating(player.Owner, teamId);
            ApplyLoadout(player.Owner, teamId);
            if (matchInProgress) {
                retention?.BeginMatch(player.Owner);
                BroadcastState();
            }
        }

        void SpawnNpcTeams(int count) {
            for (int teamIndex = 0; teamIndex < Mathf.Clamp(count, 0, DI_Arena.MaxNpcTeams); teamIndex++) {
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

        /// <summary>
        /// The free plain base furthest from every held base. The first team on an empty board starts
        /// as far from the middle as possible, so no team starts in a core. -1 when none is free.
        /// </summary>
        int FindSpawnBase() {
            int best = -1;
            float bestDistance = float.MinValue;
            bool anyHeld = false;
            foreach (BaseState state in bases)
                anyHeld |= state.teamId >= 0;

            for (int i = 0; i < bases.Count; i++) {
                // Nobody starts on a special base, a core or a middle island.
                if (bases[i].teamId >= 0 || IsSpecial(bases[i]) || !bases[i].spawnable)
                    continue;

                float score = anyHeld ? float.MaxValue : bases[i].position.sqrMagnitude;
                if (anyHeld)
                    for (int j = 0; j < bases.Count; j++)
                        if (bases[j].teamId >= 0)
                            score = Mathf.Min(score, (bases[i].position - bases[j].position).sqrMagnitude);

                if (score > bestDistance) {
                    bestDistance = score;
                    best = i;
                }
            }

            return best;
        }

        void OnPlayerRemoved(PlayerData player) {
            if (player == null)
                return;

            int clientId = player.Owner.ClientId;
            // A player who leaves a running match is out: rated as eliminated now, behind every team still in.
            if (matchInProgress && playerTeams.TryGetValue(clientId, out int leavingTeam) && dotInvadersTeams.Contains(leavingTeam)) {
                RecordElimination(leavingTeam);
                RateLeavingPlayer(player.Owner, leavingTeam);
            }
            RecordLeavingPlayerProgress(player);
            ForgetPlayerProgression(clientId);
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
