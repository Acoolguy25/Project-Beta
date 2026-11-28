#if UNITY_SERVER
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Progress;
using RyanAssets.Retention;
using RyanAssets.Retention.Server;
using RyanAssets.Server.ServerCore;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    // Per-match records, saved progress and the retention kit (levels, daily challenges, summary card).
    public sealed partial class DI_ServerRunner {
        /// <summary>One player's tally for the running match, written to their save when it ends or they leave.</summary>
        struct MatchRecord {
            public int captures;
            public int mostBasesHeld;
            public int eliminations;
            public int bounties;
        }

        RetentionService<DI_SaveData> retention;

        /// <summary>What each player's match-over panel shows; filled when the match ends, cleared with the board.</summary>
        readonly Dictionary<int, DI_MatchSummary> matchSummaries = new();

        void StartRetention() {
            retention = new RetentionService<DI_SaveData>(DI_Retention.Rules(retentionTuning), save => save.retention);
            retention.Ready += OnProgressReady;
            retention.Unlocked += OnUnlocked;
        }

        void StopRetention() {
            if (retention == null)
                return;
            retention.Ready -= OnProgressReady;
            retention.Unlocked -= OnUnlocked;
            retention.Dispose();
            retention = null;
        }

        /// <summary>The player's document is known: their rating, record and perks can be shown and used.</summary>
        void OnProgressReady(NetworkConnection connection, int level) {
            ApplyLateLoadout(connection);
            SendProfile(connection);
        }

        void BeginMatchForPlayers() {
            if (retention == null || InstanceFinder.ServerManager == null)
                return;
            foreach (NetworkConnection connection in InstanceFinder.ServerManager.Clients.Values)
                if (connection.IsValid && playerTeams.ContainsKey(connection.ClientId))
                    retention.BeginMatch(connection);
        }

        static bool TryConnection(int clientId, out NetworkConnection connection) {
            connection = null;
            return clientId >= 0 && InstanceFinder.ServerManager != null &&
                   InstanceFinder.ServerManager.Clients.TryGetValue(clientId, out connection) && connection.IsValid;
        }

        /// <summary>Reports a stat for the player behind <paramref name="clientId"/>; NPC owners (negative ids) have none.</summary>
        void CountStat(int clientId, string stat, int amount = 1) {
            if (retention != null && TryConnection(clientId, out NetworkConnection connection))
                retention.Count(connection, stat, amount);
        }

        /// <summary>
        /// Counts a capture for the player who sent the troop (NPC teams have no save) and feeds the
        /// capture challenges: any base, special bases, factories and fortresses.
        /// </summary>
        void RecordCapture(int ownerClientId, int teamId, BaseState captured) {
            if (ownerClientId < 0)
                return;
            int held = 0;
            foreach (BaseState state in bases)
                if (state.teamId == teamId)
                    held++;
            matchRecords.TryGetValue(ownerClientId, out MatchRecord record);
            record.captures++;
            bool newMost = held > record.mostBasesHeld;
            if (newMost)
                record.mostBasesHeld = held;
            matchRecords[ownerClientId] = record;

            CountStat(ownerClientId, DI_Retention.Stats.Captures);
            if (IsSpecial(captured))
                CountStat(ownerClientId, DI_Retention.Stats.SpecialCaptures);
            if (captured.isFactory)
                CountStat(ownerClientId, DI_Retention.Stats.FactoryCaptures);
            if (captured.isFortress)
                CountStat(ownerClientId, DI_Retention.Stats.FortressCaptures);
            if (newMost && retention != null && TryConnection(ownerClientId, out NetworkConnection connection))
                retention.Reach(connection, DI_Retention.Stats.MostHeld, held);
        }

        /// <summary>Credits the player who took a team's last base with eliminating it.</summary>
        void RecordEliminationCredit(int clientId) {
            if (clientId < 0)
                return;
            matchRecords.TryGetValue(clientId, out MatchRecord record);
            record.eliminations++;
            matchRecords[clientId] = record;
            CountStat(clientId, DI_Retention.Stats.Eliminations);
        }

        /// <summary>
        /// Ends the match for every player still on a team: saves their record and rating, pays the match
        /// rewards through the kit's summary card, and fills the match-over panel.
        /// </summary>
        void RecordMatchProgress(int winnerTeamId) {
            List<DI_TeamResult> results = BuildResults(winnerTeamId);
            bool draw = winnerTeamId < 0;
            foreach (NetworkConnection connection in InstanceFinder.ServerManager.Clients.Values) {
                if (!connection.IsValid || !playerTeams.TryGetValue(connection.ClientId, out int team) ||
                    !dotInvadersTeams.Contains(team))
                    continue;
                bool won = team == winnerTeamId;
                MatchRecord record = TakeMatchRecord(connection.ClientId);
                PlayerProgress.Modify<DI_SaveData>(connection, save => {
                    save.RecordMatch(won);
                    save.RecordCaptures(record.captures, record.mostBasesHeld);
                    save.eliminations += record.eliminations;
                    save.RecordBounties(record.bounties);
                });
                RatingChange rating = RateFinishedPlayer(connection, team, results);

                var result = new DI_MatchResult {
                    won = won,
                    placement = rating.placement,
                    teamCount = results.Count,
                    captures = record.captures,
                    eliminations = record.eliminations,
                    bounties = record.bounties,
                    newRank = rating.newRank,
                    rankName = DI_Ranks.Of(rating.after).Name,
                };
                List<RewardLine> lines = rewards.Lines(result);
                lines.Add(RatingLine(rating));
                retention.Count(connection, DI_Retention.Stats.Matches);
                if (won)
                    retention.Count(connection, DI_Retention.Stats.Wins);
                if (rating.placement is >= 1 and <= PodiumPlaces)
                    retention.Count(connection, DI_Retention.Stats.Podiums);
                retention.FinishMatch(connection, DI_MatchRewards.Title(result, draw), lines);

                bool saved = PlayerProgress.TryGet(connection, out DI_SaveData progress);
                matchSummaries[connection.ClientId] = new DI_MatchSummary {
                    saved = saved,
                    matchCaptures = record.captures,
                    matchMostHeld = record.mostBasesHeld,
                    totalCaptures = saved ? progress.basesCaptured : record.captures,
                    bestMostHeld = saved ? progress.mostBasesHeld : record.mostBasesHeld,
                    rated = rating.rated,
                    ratingBefore = rating.before,
                    ratingAfter = rating.after,
                    placement = rating.placement,
                    teamCount = results.Count,
                    unratedReason = rating.unratedReason ?? string.Empty,
                    matchBounties = record.bounties,
                    totalBounties = saved ? progress.bountiesClaimed : record.bounties,
                };
                SendProfile(connection);
            }
        }

        /// <summary>Finishing in this many places or better counts for the podium challenge.</summary>
        const int PodiumPlaces = 2;

        /// <summary>The summary card's rating line: "Rating 1034 (+18)" or why the match was not rated.</summary>
        static RewardLine RatingLine(in RatingChange rating) {
            if (!rating.rated)
                return RewardLine.Info("Rating", $"Unrated: {rating.unratedReason}");
            int delta = rating.after - rating.before;
            string sign = delta < 0 ? "-" : "+";
            return RewardLine.Info($"Rating {rating.after}", $"{sign}{Mathf.Abs(delta)}  {DI_Ranks.Of(rating.after).Name}");
        }

        /// <summary>A player leaving mid-match keeps the bases they took; the match itself is not counted.</summary>
        void RecordLeavingPlayerProgress(PlayerData player) {
            MatchRecord record = TakeMatchRecord(player.Owner.ClientId);
            if (record.captures > 0 || record.eliminations > 0 || record.bounties > 0)
                PlayerProgress.Modify<DI_SaveData>(player, save => {
                    save.RecordCaptures(record.captures, record.mostBasesHeld);
                    save.eliminations += record.eliminations;
                    save.RecordBounties(record.bounties);
                });
        }

        MatchRecord TakeMatchRecord(int clientId) {
            matchRecords.Remove(clientId, out MatchRecord record);
            return record;
        }

        /// <summary>Sends the player their rating, record and equipped perks.</summary>
        void SendProfile(NetworkConnection connection) {
            if (connection == null || !connection.IsActive)
                return;
            bool saved = PlayerProgress.TryGet(connection, out DI_SaveData save);
            List<string> perks = LoadoutOf(connection);
            InstanceFinder.ServerManager.Broadcast(connection, new DI_ProfileBroadcast {
                saved = PlayerProgress.GetState<DI_SaveData>(PlayerOf(connection)) == ProgressState.Loaded,
                rating = saved ? save.rating : ratingTuning.startRating,
                peakRating = saved ? save.peakRating : ratingTuning.startRating,
                ratedMatches = saved ? save.ratedMatches : 0,
                matches = saved ? save.matches : 0,
                wins = saved ? save.wins : 0,
                perks = perks.ToArray(),
            });
        }

        static PlayerData PlayerOf(NetworkConnection connection) =>
            PlayerData.TryGetPlayerData(connection, out PlayerData player) ? player : null;
    }
}
#endif
