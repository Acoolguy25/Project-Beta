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
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    // Match end conditions, winner announcement and state broadcast.
    public sealed partial class DI_ServerRunner {
        void CheckEndConditions() {
            if (!matchInProgress || bases.Count == 0)
                return;

            int survivor = FindSoleSurvivingTeam();
            if (survivor != -2)
                CompleteMatch(survivor);
        }

        // -2 means contested, -1 means no survivors. Neutral bases do not compete.
        int FindSoleSurvivingTeam() {
            int survivor = -1;
            foreach (BaseState state in bases) {
                if (state.teamId < 0)
                    continue;
                if (survivor >= 0 && survivor != state.teamId)
                    return -2;
                survivor = state.teamId;
            }
            foreach (DotState dot in dots) {
                if (dot.teamId < 0)
                    continue;
                if (survivor >= 0 && survivor != dot.teamId)
                    return -2;
                survivor = dot.teamId;
            }
            return survivor;
        }

        void CheckEliminatedTeams() {
            foreach (int teamId in dotInvadersTeams) {
                if (announcedEliminations.Contains(teamId) || HasTeamPresence(teamId))
                    continue;

                announcedEliminations.Add(teamId);
                SendTeamChatMessage(teamId, "eliminated.");
            }
        }

        bool HasTeamPresence(int teamId) {
            for (int i = 0; i < bases.Count; i++) {
                if (bases[i].teamId == teamId)
                    return true;
            }
            for (int i = 0; i < dots.Count; i++) {
                if (dots[i].teamId == teamId)
                    return true;
            }
            return false;
        }

        int DetermineLeadingTeam() {
            var scores = new Dictionary<int, int>();
            for (int i = 0; i < bases.Count; i++) {
                BaseState state = bases[i];
                if (state.teamId < 0)
                    continue;
                scores.TryGetValue(state.teamId, out int score);
                scores[state.teamId] = score + 10000 + state.troops;
            }
            for (int i = 0; i < dots.Count; i++) {
                scores.TryGetValue(dots[i].teamId, out int score);
                scores[dots[i].teamId] = score + 1;
            }

            int leader = -1;
            int bestScore = int.MinValue;
            bool tied = false;
            foreach (KeyValuePair<int, int> entry in scores) {
                if (entry.Value > bestScore) {
                    leader = entry.Key;
                    bestScore = entry.Value;
                    tied = false;
                } else if (entry.Value == bestScore) {
                    tied = true;
                }
            }
            return tied ? -1 : leader;
        }

        void CompleteMatch(int winnerTeamId) {
            if (!matchInProgress)
                return;

            CheckEliminatedTeams();
            matchInProgress = false;
            winningTeamId = winnerTeamId;
            for (int i = 0; i < bases.Count; i++)
                ClearSend(bases[i]);

            if (winnerTeamId >= 0 && winnerTeamId < NpcTeamIdStart) {
                foreach (NetworkConnection connection in InstanceFinder.ServerManager.Clients.Values) {
                    if (connection.IsValid && playerTeams.TryGetValue(connection.ClientId, out int playerTeam) &&
                        playerTeam == winnerTeamId)
                        ServerReward.AddReward(connection, WinnerXPReward, WinnerGoldReward);
                }
            }

            RecordMatchProgress(winnerTeamId);
            if (winnerTeamId >= 0)
                SendTeamChatMessage(winnerTeamId, "wins the game!");

            UpdateTopMessage();
            BroadcastState();
        }

        static void SendTeamChatMessage(int teamId, string message) {
            string teamName = GetDotInvadersTeamName(teamId, teamId >= NpcTeamIdStart);
            string coloredTeamName = TeamConfig.ColorRichText(
                teamName,
                TeamConfig.TeamToColor(DI_Rules.GetTeamColor(teamId)));
            ServerChat.SendSystemMessage(new SystemMessageBroadcast(
                $"{coloredTeamName} {message}",
                SystemMessageSource.CustomMessage));
        }

        static string GetDotInvadersTeamName(int teamId, bool npc) {
            string name = DI_Rules.GetTeamColor(teamId).ToString().ToUpperInvariant();
            return npc ? $"{name} NPC TEAM" : $"{name} TEAM";
        }

        void UpdateTopMessage() => SetTopMessage(TopMessageText());

        string TopMessageText() {
            if (matchInProgress) {
                int minutes = Mathf.Max(0, secondsRemaining) / 60;
                int seconds = Mathf.Max(0, secondsRemaining) % 60;
                return $"Dot Invaders - {minutes}:{seconds:00}";
            }

            if (winningTeamId < 0)
                return "Dot Invaders - Draw";
            if (winningTeamId >= NpcTeamIdStart)
                return $"{GetDotInvadersTeamName(winningTeamId, true)} wins!";
            return $"{GetDotInvadersTeamName(winningTeamId, false)} wins! +{WinnerXPReward} XP / +{WinnerGoldReward} gold";
        }

        // Leaderboard hook: each player's team standing (bases held and troops stationed in them).
        void UpdateLeaderboard() {
            int basesIndex = SharedGlobalEvents.GetLeaderboardIndex(BasesColumn);
            int troopsIndex = SharedGlobalEvents.GetLeaderboardIndex(TroopsColumn);
            if (basesIndex < 0 && troopsIndex < 0)
                return;

            foreach (PlayerData player in PlayerData.Players.Values) {
                int baseTotal = 0;
                int troopTotal = 0;
                if (player.Owner.IsValid && playerTeams.TryGetValue(player.Owner.ClientId, out int teamId)) {
                    for (int i = 0; i < bases.Count; i++) {
                        if (bases[i].teamId != teamId)
                            continue;
                        baseTotal++;
                        troopTotal += bases[i].troops;
                    }
                }

                SetLeaderboardValue(player, basesIndex, baseTotal);
                SetLeaderboardValue(player, troopsIndex, troopTotal);
            }
        }

        static void SetLeaderboardValue(PlayerData player, int index, int value) {
            if (index >= 0 && index < player.leaderboard.Count && player.leaderboard[index] != value)
                player.leaderboard[index] = value;
        }

        void BroadcastState() {
            if (!initialized || !InstanceFinder.IsServerStarted)
                return;

            UpdateLeaderboard();
            DI_StateBroadcast state = CreateState();
            foreach (NetworkConnection connection in InstanceFinder.ServerManager.Clients.Values) {
                if (!connection.IsActive || !connection.IsAuthenticated)
                    continue;

                state.yourClientId = connection.ClientId;
                state.yourTeamId = playerTeams.TryGetValue(connection.ClientId, out int teamId) ? teamId : -1;
                state.yourSummary = matchSummaries.TryGetValue(connection.ClientId, out DI_MatchSummary summary)
                    ? summary : default;
                InstanceFinder.ServerManager.Broadcast(connection, state);
            }
            // Clashes are events, not state: each one is sent in exactly one snapshot.
            clashPositions.Clear();
        }

        DI_StateBroadcast CreateState() {
            int baseCount = bases.Count;
            int linkCount = links.Count;
            int dotCount = dots.Count;

            var state = new DI_StateBroadcast {
                revision = ++revision,
                yourTeamId = -1,
                secondsRemaining = secondsRemaining,
                matchEnded = initialized && !matchInProgress,
                winningTeamId = winningTeamId,
                npcIntelligence = npcIntelligence,
                turretRange = turretRange,
                turretFireRate = turretFireRate,
                maxCapacity = maxCapacity,
                moveSpeed = moveSpeed,
                superProductionSpeed = superProductionSpeed,
                superSpeedupSeconds = superSpeedupSeconds,
                superMaxCapacity = superMaxCapacity,
                productionSpeed = productionSpeed,
                sendInterval = sendInterval,
                speedBaseBonus = speedBaseBonus,
                npcAggression = npcAggression,
                baseSuperProducers = new bool[baseCount],
                baseProductionCharge = new float[baseCount],
                baseProductionRates = new float[baseCount],
                basePositions = new Vector2[baseCount],
                baseTroops = new int[baseCount],
                baseOwners = new int[baseCount],
                baseTeams = new int[baseCount],
                basePendingTroops = new int[baseCount],
                linkSources = new int[linkCount],
                linkTargets = new int[linkCount],
                dotIds = new int[dotCount],
                dotPositions = new Vector2[dotCount],
                dotTeams = new int[dotCount],
                baseTurrets = new bool[baseCount],
                turretShotSequences = new int[baseCount],
                turretShotPositions = new Vector2[baseCount],
                baseRoutes = new DI_Route[baseCount],
                baseSpeedBases = new bool[baseCount],
                baseFactories = new bool[baseCount],
                baseFortresses = new bool[baseCount],
                clashPositions = clashPositions.ToArray(),
                factoryMultiplier = factoryMultiplier,
                fortressMultiplier = fortressMultiplier
            };

            for (int i = 0; i < baseCount; i++) {
                state.basePositions[i] = bases[i].position;
                state.baseTroops[i] = bases[i].troops;
                state.baseOwners[i] = bases[i].ownerClientId;
                state.baseTeams[i] = bases[i].teamId;
                state.basePendingTroops[i] = bases[i].pendingTroops;
                state.baseTurrets[i] = bases[i].isTurret;
                state.baseSuperProducers[i] = bases[i].isSuperProducer;
                state.baseSpeedBases[i] = bases[i].isSpeedBase;
                state.baseFactories[i] = bases[i].isFactory;
                state.baseFortresses[i] = bases[i].isFortress;
                state.baseProductionCharge[i] = Mathf.Clamp01(bases[i].uninterruptedSeconds / superSpeedupSeconds);
                BaseState production = bases[i];
                state.baseProductionRates[i] = production.teamId >= 0 && !production.isTurret &&
                    production.pendingTroops == 0 && production.productionDelay <= 0f &&
                    production.troops < Capacity(production) && !HasOutgoingTroops(i) ? ProductionRate(production) : 0f;
                state.turretShotSequences[i] = bases[i].shotSequence;
                state.turretShotPositions[i] = bases[i].shotPosition;
                state.baseRoutes[i] = new DI_Route { baseIds = bases[i].pendingRoute };
            }

            foreach (DotState dot in dots) {
                if (dot.route == null || dot.route.Length < 2)
                    continue;
                int source = dot.route[0];
                if (bases[source].ownerClientId == dot.ownerClientId && state.baseRoutes[source].baseIds == null)
                    state.baseRoutes[source] = new DI_Route { baseIds = dot.route };
            }

            for (int i = 0; i < linkCount; i++) {
                state.linkSources[i] = links[i].x;
                state.linkTargets[i] = links[i].y;
            }

            for (int i = 0; i < dotCount; i++) {
                state.dotIds[i] = dots[i].id;
                state.dotPositions[i] = GetDotPosition(dots[i]);
                state.dotTeams[i] = dots[i].teamId;
            }

            return state;
        }
    }
}
#endif
