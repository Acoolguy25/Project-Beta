using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Retention;
using RyanAssets.Retention.Server;
using RyanAssets.Rounds;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Server {
    /// <summary>
    /// Round points and progression. Every play worth something (staying hidden, a close call, a tag, a
    /// brave taunt, a decoy that fooled someone) pays round points at once, which are this game's XP
    /// through the shared retention kit. At the end of a round the most points make the MVP, wins build a
    /// streak, and each player gets the kit's match summary plus a recap for the results card.
    /// </summary>
    public sealed partial class HS_ServerRunner {
        [Header("Points, MVP and streaks")]
        [SerializeField] HS_ScoreTuning scoring = new();
        [Header("Retention kit (levels, daily challenges, play streak)")]
        [SerializeField] RetentionTuning retentionTuning = new();

        RetentionService<HS_SaveData> retention;
        readonly HS_RoundLedger<NetworkConnection> ledger = new();
        readonly Dictionary<NetworkConnection, HS_CloseCall> closeCalls = new();
        readonly Dictionary<NetworkConnection, float> hiddenSeconds = new();
        /// <summary>Everyone dealt into this round (late joiners too: they seek).</summary>
        readonly HashSet<NetworkConnection> participants = new();
        /// <summary>The share of the seek phase gone when each participant joined (0 for everyone dealt in at the start).</summary>
        readonly Dictionary<NetworkConnection, float> joinedAt = new();
        /// <summary>Tags each participant made this round.</summary>
        readonly Dictionary<NetworkConnection, int> roundTags = new();
        /// <summary>
        /// Player ids (not connections, which a rejoin replaces) dealt in as hiders this round, and of
        /// participants who left it: neither can win this round with the seekers.
        /// </summary>
        readonly HashSet<string> dealtHiders = new(), leftRound = new();
        /// <summary>Documents for players whose progress is not tracked (no valid player id), so streaks still work for the session.</summary>
        readonly Dictionary<NetworkConnection, HS_SaveData> sessionSaves = new();
        readonly List<NetworkConnection> scoringScratch = new();
        bool firstFindDone, lastHiderPaid;

        /// <summary>Opens this round's books: everyone dealt in starts a match summary at zero points.</summary>
        void BeginRoundScoring() {
            ledger.Clear();
            closeCalls.Clear();
            hiddenSeconds.Clear();
            participants.Clear();
            joinedAt.Clear();
            roundTags.Clear();
            dealtHiders.Clear();
            leftRound.Clear();
            firstFindDone = lastHiderPaid = false;
            foreach (var pair in roles) {
                if (pair.Value == HS_Role.None || !pair.Key.IsActive) continue;
                participants.Add(pair.Key);
                joinedAt[pair.Key] = 0f;
                if (!Seeks(pair.Value)) {
                    closeCalls[pair.Key] = HS_CloseCall.Fresh;
                    if (IdOf(pair.Key) is { } id) dealtHiders.Add(id);
                }
                ledger.Add(pair.Key, 0);
                retention.BeginMatch(pair.Key);
            }
        }

        /// <summary>
        /// A player who joins mid-round seeks and earns points with the seekers; their side's win counts only
        /// when they joined early enough or tagged someone (<see cref="HS_Rules.SeekerWinCounts"/>).
        /// </summary>
        void JoinRoundScoring(NetworkConnection c) {
            if (!participants.Add(c)) return;
            joinedAt[c] = SeekShareGone;
            ledger.Add(c, 0);
            retention.BeginMatch(c);
        }

        static string IdOf(NetworkConnection c) =>
            PlayerData.TryGetPlayerData(c, out PlayerData player) && !string.IsNullOrEmpty(player.player_id.Value) ? player.player_id.Value : null;

        /// <summary>Whether this participant's seekers' side win counts this round.</summary>
        bool SeekerWinCounts(NetworkConnection c) {
            string id = IdOf(c);
            bool forfeited = id != null && (dealtHiders.Contains(id) || leftRound.Contains(id));
            return HS_Rules.SeekerWinCounts(forfeited, joinedAt.TryGetValue(c, out float share) ? share : 1f, roundTags.TryGetValue(c, out int tags) ? tags : 0);
        }

        /// <summary>
        /// A participant who leaves a round before it ends has lost it: their win streak ends, and if they
        /// come back this round they cannot win it.
        /// </summary>
        void RecordLeaver(NetworkConnection c) {
            if (c == null || !participants.Contains(c) || phase is not (HS_Phase.Hiding or HS_Phase.Seeking)) return;
            string id = IdOf(c);
            if (id == null || !leftRound.Add(id)) return;
            WithSave(c, save => save.RecordLeft());
        }

        void ForgetScoring(NetworkConnection c) {
            closeCalls.Remove(c);
            hiddenSeconds.Remove(c);
            participants.Remove(c);
            joinedAt.Remove(c);
            roundTags.Remove(c);
            sessionSaves.Remove(c);
        }

        /// <summary>Pays round points now: game XP (and account XP) through the kit, the leaderboard and the player's HUD.</summary>
        void AddPoints(NetworkConnection c, int points, string reason, bool highlight) {
            if (points <= 0 || !c.IsActive) return;
            retention.AddXp(c, scoring.Xp(points), reason, announce: false);
            Score(c, points, reason, highlight);
        }

        /// <summary>Counts round points without paying XP (end-of-round points are paid on the summary card instead).</summary>
        void Score(NetworkConnection c, int points, string reason, bool highlight) {
            int total = ledger.Add(c, points);
            if (PlayerData.TryGetPlayerData(c, out var player)) RoundLeaderboard.Add(player, PointsColumn, points);
            InstanceFinder.ServerManager.Broadcast(c, new HS_PointsBroadcast { points = points, total = total, reason = reason, highlight = highlight });
        }

        /// <summary>Seek-phase bookkeeping: time hidden pays survival points, and seekers who came close and left pay close calls.</summary>
        void TickScoring(float dt) {
            if (dt <= 0f) return;
            scoringScratch.Clear();
            foreach (var pair in roles) if (pair.Value == HS_Role.Hider && pair.Key.IsActive) scoringScratch.Add(pair.Key);
            foreach (NetworkConnection hider in scoringScratch) {
                float before = hiddenSeconds.TryGetValue(hider, out float s) ? s : 0f;
                hiddenSeconds[hider] = before + dt;
                AddPoints(hider, scoring.SurvivalPointsFor(before + dt) - scoring.SurvivalPointsFor(before), "Time hidden", false);
                if (!TryGetLiveCharacter(hider, out LocalCharacter character)) continue;
                HS_CloseCall call = closeCalls.TryGetValue(hider, out HS_CloseCall existing) ? existing : HS_CloseCall.Fresh;
                bool escaped = call.Step(NearestSeekerDistance(character.transform.position), Time.time, scoring);
                closeCalls[hider] = call;
                if (!escaped) continue;
                AddFeaturedPoints(hider, scoring.closeCallPoints, HS_RoundEvent.Frenzy, "Close call", "Frenzy close call");
                retention.Count(hider, HS_Retention.Stats.CloseCalls);
                PlayerProgress.Modify<HS_SaveData>(hider, save => save.closeCalls++);
            }
        }

        /// <summary>
        /// The seeker whose flashlight set up this tag (<see cref="HS_RoundEnd.FlashAssist"/>), or null. Read before
        /// the found hider's effects are cleared, since that clears the dazzle too.
        /// </summary>
        NetworkConnection FlashAssister(NetworkConnection hider, NetworkConnection seeker) =>
            abilityStates.TryGetValue(hider, out AbilityState dazzle) && dazzle.dazzledBy != null
            && HS_RoundEnd.FlashAssist(dazzle.dazzledBy == seeker, dazzle.dazzledBy.IsActive, Time.time, dazzle.dazzledUntil, scoring.flashAssistGrace)
                ? dazzle.dazzledBy
                : null;

        /// <summary>Points for a tag: the tagger, a first find, a flashlight assist (<paramref name="assister"/>), and the hider left last.</summary>
        void OnTagScored(NetworkConnection seeker, NetworkConnection hider, NetworkConnection assister) {
            roundTags[seeker] = (roundTags.TryGetValue(seeker, out int tags) ? tags : 0) + 1;
            AddFeaturedPoints(seeker, scoring.tagPoints, HS_RoundEvent.Blackout, "Tag", "Tag in the dark");
            retention.Count(seeker, HS_Retention.Stats.Tags);
            if (!firstFindDone) {
                firstFindDone = true;
                AddPoints(seeker, scoring.firstFindPoints, "First find", true);
                retention.Count(seeker, HS_Retention.Stats.FirstFind);
            }
            if (assister != null) AddPoints(assister, scoring.flashAssistPoints, "Flashlight assist", true);
            if (!HS_RoundEnd.LastHiderDue(lastHiderPaid, hidersAtStart, HidersLeft)) return;
            lastHiderPaid = true;
            foreach (var pair in roles) {
                if (pair.Value != HS_Role.Hider || !pair.Key.IsActive) continue;
                AddPoints(pair.Key, scoring.lastHiderPoints, "Last hider", true);
                retention.Count(pair.Key, HS_Retention.Stats.LastHider);
            }
        }

        // ---------------------------------------------------------------- end of round
        /// <summary>
        /// End-of-round rewards, the MVP, records and streaks, then the kit's summary card and this game's
        /// recap for everyone on the server.
        /// </summary>
        void FinishRound(bool hidersWin) {
            var results = new List<HS_RoundResult<NetworkConnection>>();
            foreach (NetworkConnection c in participants) {
                if (!c.IsActive) continue;
                bool survived = RoleOf(c) == HS_Role.Hider;
                results.Add(new HS_RoundResult<NetworkConnection>(c, survived, survived || (!hidersWin && Seeks(RoleOf(c)) && SeekerWinCounts(c))));
            }
            // End points first, so a hider whose Survived points put them on top is the MVP.
            bool hasMvp = HS_RoundEnd.Settle(ledger, results, scoring, (c, points, reason) => Score(c, points, reason, false), out NetworkConnection mvp, out int mvpPoints);
            string mvpName = hasMvp ? TitledName(mvp) : "";
            var recap = new HS_RoundRecapBroadcast {
                round = round, mvpClientId = hasMvp ? mvp.ClientId : -1, mvpName = mvpName, mvpPoints = mvpPoints,
            };
            if (hasMvp) ServerChat.SendSystemMessage(new($"Round MVP: {mvpName} with {mvpPoints} points.", SystemMessageSource.CustomMessage));

            var byPlayer = new Dictionary<NetworkConnection, HS_RoundResult<NetworkConnection>>();
            foreach (var result in results) byPlayer[result.Player] = result;
            foreach (PlayerData player in PlayerData.Players.Values) {
                NetworkConnection c = player.Owner;
                if (!c.IsActive) continue;
                if (!byPlayer.TryGetValue(c, out var result)) {
                    InstanceFinder.ServerManager.Broadcast(c, recap);
                    continue;
                }
                bool survived = result.Survived, won = result.Won;
                bool isMvp = hasMvp && mvp == c;
                int points = ledger.PointsOf(c);
                float hidden = hiddenSeconds.TryGetValue(c, out float h) ? h : 0f;
                HS_RoundRecapBroadcast mine = recap;
                WithSave(c, save => {
                    mine.played = true;
                    mine.won = won;
                    mine.points = points;
                    mine.newBest = save.RecordResult(survived, won, points, isMvp) && points > 0;
                    mine.bestPoints = save.bestRoundPoints;
                    mine.winStreak = save.winStreak;
                    mine.bestWinStreak = save.bestWinStreak;
                    ReportChallenges(c, survived, won, isMvp, points, hidden);
                    retention.FinishMatch(c, HS_RoundEnd.Title(survived, won, isMvp),
                        HS_RoundEnd.Lines(survived, won, isMvp, save.winStreak, points, mine.newBest, save.bestRoundPoints, scoring));
                    InstanceFinder.ServerManager.Broadcast(c, mine);
                });
            }
        }

        /// <summary>A player's name with the Manor Legend title in front when they own it (MVP lines).</summary>
        string TitledName(NetworkConnection c) =>
            retention.IsUnlocked(c, HS_Retention.UnlockIds.ManorLegend) ? $"{HS_Retention.LegendTitle} {NameOf(c)}" : NameOf(c);

        /// <summary>
        /// Pays a play that belongs to <paramref name="play"/>'s event: multiplied, under <paramref name="featuredReason"/>,
        /// while that event runs on the day it is featured (<see cref="HS_Daily"/>).
        /// </summary>
        void AddFeaturedPoints(NetworkConnection c, int points, HS_RoundEvent play, string reason, string featuredReason) {
            int paid = HS_Daily.Pay(points, play, activeEvent, FeaturedToday, events.featuredPayMultiplier);
            AddPoints(c, paid, paid != points ? featuredReason : reason, true);
        }

        void ReportChallenges(NetworkConnection c, bool survived, bool won, bool mvp, int points, float hidden) {
            retention.Count(c, HS_Retention.Stats.Rounds);
            if (survived) retention.Count(c, HS_Retention.Stats.Survived);
            if (won) retention.Count(c, HS_Retention.Stats.Wins);
            if (mvp) retention.Count(c, HS_Retention.Stats.Mvp);
            retention.Count(c, HS_Retention.Stats.HiddenSeconds, Mathf.FloorToInt(hidden));
            retention.Reach(c, HS_Retention.Stats.RoundPoints, points);
        }

        /// <summary>Changes the player's saved document, or a session-only one when their progress is not tracked.</summary>
        void WithSave(NetworkConnection c, Action<HS_SaveData> change) {
            if (PlayerProgress.Modify(c, change)) return;
            if (!sessionSaves.TryGetValue(c, out HS_SaveData save)) sessionSaves[c] = save = new HS_SaveData();
            change(save);
        }
    }
}
