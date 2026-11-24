#if UNITY_SERVER
using System.Collections.Generic;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    // Skill rating: finishing order, Elo updates, the NPC tier a match is played at and why a match is unrated.
    public sealed partial class DI_ServerRunner {
        const string CustomTierName = "Custom";
        // Standings: eliminated teams count 0, 1, 2... in the order they went out; teams still on the
        // board rank above every eliminated one, by their score.
        const long AliveStanding = 1_000_000_000L;
        const string UnratedLateJoin = "joined late";
        const string UnratedCustom = "custom settings";
        const string UnratedAlone = "no opponents";

        /// <summary>What one match did to a player's rating.</summary>
        struct RatingChange {
            public bool rated;
            public int before, after, placement;
            /// <summary>The new rating reaches a rank above the best the player ever held.</summary>
            public bool newRank;
            public string unratedReason;
        }

        readonly Dictionary<int, int> eliminationOrder = new();
        // Player teams' ratings as they entered (an NPC team is rated at its tier).
        readonly Dictionary<int, int> teamRatings = new();
        // Players who entered within the late-join window: only they are rated.
        readonly HashSet<int> ratedPlayers = new();
        // The owner (client id, negative for NPCs) whose troops last took a base from each team.
        readonly Dictionary<int, int> lastCapturer = new();
        int npcTierIndex = -1;
        int forcedNpcTier = -1;
        bool npcTuningOverridden;
        string matchUnratedReason;

        internal string NpcTierName => npcTuningOverridden || npcTierIndex < 0 || npcTierIndex >= npcTiers.Length
            ? CustomTierName : npcTiers[npcTierIndex].name;
        internal int ForcedNpcTier => forcedNpcTier;
        internal int NpcTierCount => npcTiers.Length;

        int NpcRating => npcTierIndex >= 0 && npcTierIndex < npcTiers.Length ? npcTiers[npcTierIndex].rating : ratingTuning.startRating;

        int RatingOf(NetworkConnection connection) =>
            PlayerProgress.TryGet(connection, out DI_SaveData save) ? save.rating : ratingTuning.startRating;

        /// <summary>
        /// Sets the bots to the tier nearest the players' average rating (or the tier the NPCTier command
        /// forced). NPCIntelligence / NPCAggression set by command keep their values instead.
        /// </summary>
        void ChooseNpcTier() {
            if (npcTiers == null || npcTiers.Length == 0) {
                npcTierIndex = -1;
                return;
            }
            long total = 0;
            int count = 0;
            // Null before the first play session (EditMode tests).
            if (PlayerData.Players != null)
                foreach (PlayerData player in PlayerData.Players.Values) {
                    if (!player.Owner.IsValid) continue;
                    total += RatingOf(player.Owner);
                    count++;
                }
            int average = count > 0 ? (int)(total / count) : ratingTuning.startRating;
            npcTierIndex = forcedNpcTier >= 0 ? Mathf.Min(forcedNpcTier, npcTiers.Length - 1) : DI_NpcTiers.Pick(npcTiers, average);
            if (npcTuningOverridden)
                return;
            npcIntelligence = Mathf.Clamp(npcTiers[npcTierIndex].intelligence, 0f, 100f);
            npcAggression = Mathf.Clamp(npcTiers[npcTierIndex].aggression, 0f, 100f);
        }

        /// <summary>Notes a player's team and rating as they enter; joining after the window plays unrated.</summary>
        void EnterRating(NetworkConnection connection, int teamId) {
            teamRatings[teamId] = RatingOf(connection);
            if (matchElapsed <= ratingTuning.lateJoinSeconds)
                ratedPlayers.Add(connection.ClientId);
            else
                ratedPlayers.Remove(connection.ClientId);
        }

        /// <summary>A save that loaded after the player entered: rate them (and pick the bots) from their real rating.</summary>
        void RefreshEnteredRating(NetworkConnection connection) {
            if (!playerTeams.TryGetValue(connection.ClientId, out int team) || !dotInvadersTeams.Contains(team))
                return;
            teamRatings[team] = RatingOf(connection);
            if (matchInProgress && matchElapsed <= lateLoadoutSeconds) {
                ChooseNpcTier();
                BroadcastState();
            }
        }

        void RecordElimination(int teamId) {
            if (!eliminationOrder.ContainsKey(teamId))
                eliminationOrder[teamId] = eliminationOrder.Count;
        }

        /// <summary>Called when a base changes hands: remembers who took it from whom, for elimination credit.</summary>
        void RecordCaptureFrom(int previousTeamId, int capturerClientId) {
            if (previousTeamId >= 0)
                lastCapturer[previousTeamId] = capturerClientId;
        }

        /// <summary>The player (if any) who took the eliminated team's last base.</summary>
        void CreditElimination(int teamId) {
            if (!lastCapturer.TryGetValue(teamId, out int capturer) || capturer < 0)
                return;
            if (playerTeams.TryGetValue(capturer, out int capturerTeam) && capturerTeam != teamId)
                RecordEliminationCredit(capturer);
        }

        /// <summary>A team's score on the board: bases dominate, then garrisons, then troops on the move.</summary>
        long TeamScore(int teamId) {
            long score = 0;
            foreach (BaseState state in bases)
                if (state.teamId == teamId)
                    score += BaseScore + state.troops;
            foreach (DotState dot in dots)
                if (dot.teamId == teamId)
                    score++;
            return score;
        }

        const int BaseScore = 10000;

        /// <summary>Every team that held a base this match, with its standing and the rating it counts as.</summary>
        List<DI_TeamResult> BuildResults(int winnerTeamId) {
            var results = new List<DI_TeamResult>(dotInvadersTeams.Count);
            foreach (int team in dotInvadersTeams) {
                long standing = eliminationOrder.TryGetValue(team, out int order) ? order
                    : team == winnerTeamId ? long.MaxValue
                    : AliveStanding + TeamScore(team);
                results.Add(new DI_TeamResult(team, standing, RatingOfTeam(team)));
            }
            return results;
        }

        int RatingOfTeam(int teamId) => teamId >= NpcTeamIdStart ? NpcRating
            : teamRatings.TryGetValue(teamId, out int rating) ? rating : ratingTuning.startRating;

        RatingChange RateFinishedPlayer(NetworkConnection connection, int teamId, List<DI_TeamResult> results) =>
            Rate(connection, teamId, results);

        /// <summary>A player leaving a running match is rated now, as out behind every team still in.</summary>
        void RateLeavingPlayer(NetworkConnection connection, int teamId) => Rate(connection, teamId, BuildResults(-1));

        RatingChange Rate(NetworkConnection connection, int teamId, List<DI_TeamResult> results) {
            int before = RatingOf(connection);
            var change = new RatingChange {
                before = before,
                after = before,
                placement = DI_Rating.Placement(teamId, results),
                unratedReason = UnratedReason(connection.ClientId, results.Count),
            };
            if (change.unratedReason != null)
                return change;

            bool loaded = PlayerProgress.TryGet(connection, out DI_SaveData save);
            int ratedMatches = loaded ? save.ratedMatches : 0;
            int peak = loaded ? save.peakRating : ratingTuning.startRating;
            change.after = DI_Rating.Apply(before, ratedMatches, DI_Rating.Opponents(teamId, results), ratingTuning);
            change.rated = true;
            change.newRank = DI_Ranks.Of(change.after).Index > DI_Ranks.Of(peak).Index;
            int after = change.after;
            PlayerProgress.Modify<DI_SaveData>(connection, s => s.RecordRating(after));
            if (change.newRank)
                AnnounceNewRank(connection, DI_Ranks.Of(after));
            return change;
        }

        string UnratedReason(int clientId, int teams) =>
            matchUnratedReason ?? (teams < 2 ? UnratedAlone : !ratedPlayers.Contains(clientId) ? UnratedLateJoin : null);

        bool IsRated(int clientId) => matchUnratedReason == null && ratedPlayers.Contains(clientId);

        static void AnnounceNewRank(NetworkConnection connection, DI_Rank rank) {
            string name = TeamConfig.ColorRichText(PlayerData.GetPlayerName(connection), Color.white);
            string rankText = TeamConfig.ColorRichText(rank.Name, rank.Color);
            ServerChat.SendSystemMessage(new SystemMessageBroadcast($"{name} reached {rankText}!", SystemMessageSource.CustomMessage));
        }

        /// <summary>Admin settings changed the running match (NPC tuning, player buffs): nobody is rated for it.</summary>
        internal void MarkCustomMatch() {
            if (matchInProgress)
                matchUnratedReason = UnratedCustom;
        }

        /// <summary>NPCTier command: -1 picks by rating, 0 and up forces a tier. Applies to the running match.</summary>
        internal void SetNpcTier(int tier) {
            forcedNpcTier = tier < 0 ? -1 : Mathf.Min(tier, npcTiers.Length - 1);
            npcTuningOverridden = false;
            ChooseNpcTier();
            MarkCustomMatch();
            BroadcastState();
        }

        void ClearRatingState() {
            eliminationOrder.Clear();
            teamRatings.Clear();
            ratedPlayers.Clear();
            lastCapturer.Clear();
            matchUnratedReason = null;
        }
    }
}
#endif
