using System;
using System.Collections.Generic;
using FishNet.Broadcast;

namespace RyanAssets.Retention {
    /// <summary>
    /// Where the <see cref="RetentionLedger"/>'s effects go. The server's
    /// <c>RetentionService</c> sends with FishNet, pays through <c>LevelsServer</c> and saves through
    /// <c>PlayerProgress</c>; tests record the calls.
    /// </summary>
    public interface IRetentionSink {
        /// <summary>Sends <paramref name="message"/> to <paramref name="player"/>'s client.</summary>
        void Send<T>(RetentionPlayer player, T message) where T : struct, IBroadcast;

        /// <summary>Pays account XP and gold (each zero or more, never both zero).</summary>
        void PayAccount(RetentionPlayer player, int accountXp, int gold);

        /// <summary>
        /// A payout that must never repeat happened (a challenge completed, the streak was claimed):
        /// save the player's document and account as soon as the current change is applied.
        /// </summary>
        void SaveSoon(RetentionPlayer player);

        /// <summary>The player reached <paramref name="unlock"/> (once, as it happens).</summary>
        void Unlocked(RetentionPlayer player, UnlockDefinition unlock);
    }

    /// <summary>
    /// What the ledger keeps about one player between calls: the open match, stat reports waiting to be
    /// applied, and the fraction of account XP not yet paid. The server's per-player state derives
    /// from it.
    /// </summary>
    public class RetentionPlayer {
        internal readonly struct PendingStat {
            public readonly string Key;
            public readonly long Value;
            public readonly bool Accumulate;

            public PendingStat(string key, long value, bool accumulate) {
                Key = key;
                Value = value;
                Accumulate = accumulate;
            }
        }

        internal List<PendingStat> pending = new(4);
        internal List<PendingStat> draining = new(4);
        internal double accountXpCarry;
        internal bool finishing;
        int announcedAccountLevel = -1;
        DateTime accountLevelQuietUntil;

        /// <summary>
        /// False when the player's document is not saved this session (it could not be loaded, or the
        /// player has no valid id). Daily rewards (challenges, the streak) then pay no account XP or
        /// gold, since they would come back on every rejoin. Set by the owner before each ledger call.
        /// </summary>
        public bool Saved { get; set; } = true;

        /// <summary>The open match, from <see cref="RetentionLedger.BeginMatch"/> to <see cref="RetentionLedger.FinishMatch"/>.</summary>
        public MatchSummary Match { get; set; }

        /// <summary>Stat reports are waiting for <see cref="RetentionLedger.Flush"/>.</summary>
        public bool HasPendingStats => pending.Count > 0;

        /// <summary>Account XP earned but not yet paid (below 1 point), carried to the next payout.</summary>
        public double AccountXpCarry => accountXpCarry;

        /// <summary>
        /// True while a match is open or <see cref="RetentionLedger.FinishMatch"/> is paying its lines
        /// (the match is already closed then). A level-up inside a match is announced once, when it ends.
        /// </summary>
        public bool InMatch => Match != null || finishing;

        /// <summary>
        /// The account level last announced (or seen before the first payout); -1 while not known.
        /// Level 0 is a known level: a new player going from 0 to 1 is announced.
        /// </summary>
        public int AnnouncedAccountLevel => announcedAccountLevel;

        /// <summary>
        /// Call before paying account XP, with the level the player has now. Only the first call counts
        /// (while the level is not known), so later level-ups are measured from it.
        /// </summary>
        public void NoteAccountLevel(int level) {
            if (announcedAccountLevel < 0) announcedAccountLevel = Math.Max(0, level);
        }

        /// <summary>
        /// True (and records it) when <paramref name="level"/> is above the level last announced; false
        /// when it is not, or while no level was noted yet.
        /// </summary>
        public bool TryAnnounceAccountLevel(int level) {
            if (announcedAccountLevel < 0 || level <= announcedAccountLevel) return false;
            announcedAccountLevel = level;
            return true;
        }

        /// <summary>
        /// Until <paramref name="utc"/> (just after joining), account level-ups only move the level last
        /// announced: <see cref="TryAnnounceAccountLevel(int, DateTime)"/> records them and says false.
        /// </summary>
        public void QuietAccountLevelUntil(DateTime utc) => accountLevelQuietUntil = utc;

        /// <summary>
        /// <see cref="TryAnnounceAccountLevel(int)"/>, except that a level-up before the time given to
        /// <see cref="QuietAccountLevelUntil"/> is recorded without an announcement (false).
        /// </summary>
        public bool TryAnnounceAccountLevel(int level, DateTime utcNow) =>
            TryAnnounceAccountLevel(level) && utcNow >= accountLevelQuietUntil;
    }

    /// <summary>
    /// The reward orchestration of the retention kit, free of networking and storage so it is tested
    /// in EditMode: it pays each reward exactly once (game XP into the save, account XP and gold through
    /// the sink), lists rewards on the open match, announces XP, level-ups and challenges, claims the
    /// streak, builds the summary card, and coalesces stat reports.
    /// <para>
    /// The caller owns the save: every method that takes a <see cref="RetentionSave"/> may change it, so
    /// call those inside the document's change (the server uses <c>PlayerProgress.Modify</c>).
    /// <see cref="Queue"/> only reads it.
    /// </para>
    /// </summary>
    public sealed class RetentionLedger {
        /// <summary>Why a reward is paid; daily rewards pay no account currency to unsaved players.</summary>
        enum Source { Play, Daily }

        readonly IRetentionSink sink;

        public RetentionLedger(RetentionRules rules, IRetentionSink sink) {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public RetentionRules Rules { get; }

        // ---------------------------------------------------------------- XP and matches

        /// <summary>Pays <paramref name="xp"/> game XP now (and account XP); inside a match it is also listed on the card.</summary>
        public void AddXp(RetentionPlayer player, RetentionSave save, int xp, string reason, bool announce) {
            if (xp > 0) Grant(player, save, new RewardLine(reason, xp), announce, Source.Play, null);
        }

        /// <summary>Opens the player's match: the card's "before" bar is taken now. Calling it again restarts the card.</summary>
        public void BeginMatch(RetentionPlayer player, RetentionSave save) =>
            player.Match = new MatchSummary(Rules.Progress(save));

        /// <summary>
        /// Ends the player's match on <paramref name="day"/>: applies waiting stat reports (so a
        /// challenge finished in the last moments is on the card), pays <paramref name="rewards"/>
        /// and, for the first finished match of the day, the streak bonus; then sends the card and a
        /// fresh snapshot.
        /// </summary>
        public void FinishMatch(RetentionPlayer player, RetentionSave save, int day, DateTime utcNow, string title, IReadOnlyList<RewardLine> rewards) {
            Flush(player, save, day);
            MatchSummary summary = player.Match ?? new MatchSummary(Rules.Progress(save));
            player.Match = null;
            summary.Title = title;
            player.finishing = true;
            try {
                if (rewards != null)
                    foreach (RewardLine line in rewards) Grant(player, save, line, false, Source.Play, summary);
                StreakClaim streak = Rules.ClaimStreak(save, day);
                if (streak.Paid) {
                    Grant(player, save, new RewardLine(Rules.Tuning.streak.Line(streak.Days), streak.Xp, streak.Gold), false, Source.Daily, summary);
                    summary.StreakDays = streak.Days;
                    sink.SaveSoon(player);
                }
            } finally {
                player.finishing = false;
            }
            if (save.matchesFinished < int.MaxValue) save.matchesFinished++;
            summary.After = Rules.Progress(save);
            Rules.Track.UnlocksBetween(summary.Before.Level, summary.After.Level, summary.Unlocks);
            sink.Send(player, Rules.SummaryBroadcast(summary));
            SendSnapshot(player, save, utcNow);
        }

        /// <summary>Pays today's streak bonus now if it is unpaid (games without matches).</summary>
        public void ClaimDailyStreak(RetentionPlayer player, RetentionSave save, int day) {
            StreakClaim claim = Rules.ClaimStreak(save, day);
            if (!claim.Paid) return;
            Grant(player, save, new RewardLine(Rules.Tuning.streak.Line(claim.Days), claim.Xp, claim.Gold), true, Source.Daily, null);
            sink.SaveSoon(player);
        }

        public void SendSnapshot(RetentionPlayer player, RetentionSave save, DateTime utcNow) =>
            sink.Send(player, Rules.Snapshot(save, utcNow, player.Saved));

        // ---------------------------------------------------------------- challenge stats

        /// <summary>
        /// Queues a stat report without touching the save, after checking (allocation-free) what it
        /// would do. <see cref="StatEffect.None"/>: nothing was queued (no unfinished challenge of the
        /// day counts the stat), so there is nothing to apply. <see cref="StatEffect.Completes"/>: call
        /// <see cref="Flush"/> now so the reward is paid and announced at once.
        /// <see cref="StatEffect.Progress"/>: flush within a fraction of a second.
        /// </summary>
        /// <param name="accumulate">True for <c>Count</c> (adds up), false for <c>Reach</c> (keeps the best).</param>
        public StatEffect Queue(RetentionPlayer player, RetentionSave save, int day, string statKey, int value, bool accumulate) {
            if (value <= 0 || !Rules.UsesStat(statKey)) return StatEffect.None;
            List<RetentionPlayer.PendingStat> pending = player.pending;
            int index = -1;
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].Accumulate == accumulate && string.Equals(pending[i].Key, statKey, StringComparison.Ordinal)) { index = i; break; }
            long total = index < 0 ? value : accumulate ? pending[index].Value + value : Math.Max(pending[index].Value, value);
            StatEffect effect = Rules.Preview(save, day, statKey, total, accumulate);
            if (effect == StatEffect.None) return StatEffect.None;
            var stat = new RetentionPlayer.PendingStat(statKey, total, accumulate);
            if (index < 0) pending.Add(stat);
            else pending[index] = stat;
            return effect;
        }

        /// <summary>Applies the queued stat reports: progress is announced (lean), completions are paid and announced.</summary>
        public void Flush(RetentionPlayer player, RetentionSave save, int day) {
            if (player.pending.Count == 0) return;
            // Swap first: a reward handler may report more stats while these are applied.
            List<RetentionPlayer.PendingStat> batch = player.pending;
            player.pending = player.draining;
            player.draining = batch;
            var updates = new List<ChallengeUpdate>();
            foreach (RetentionPlayer.PendingStat stat in batch) {
                int value = (int)Math.Min(int.MaxValue, stat.Value);
                if (stat.Accumulate) Rules.Count(save, day, stat.Key, value, updates);
                else Rules.Reach(save, day, stat.Key, value, updates);
            }
            batch.Clear();
            Announce(player, save, updates);
        }

        /// <summary>Applies one stat report at once, without queueing (used while the document is still loading).</summary>
        public void Apply(RetentionPlayer player, RetentionSave save, int day, string statKey, int value, bool accumulate) {
            if (value <= 0 || !Rules.UsesStat(statKey)) return;
            var updates = new List<ChallengeUpdate>();
            if (accumulate) Rules.Count(save, day, statKey, value, updates);
            else Rules.Reach(save, day, statKey, value, updates);
            Announce(player, save, updates);
        }

        // ---------------------------------------------------------------- internals

        void Announce(RetentionPlayer player, RetentionSave save, List<ChallengeUpdate> updates) {
            if (updates.Count == 0) return;
            // The day the list now belongs to (today, or tomorrow on a server a little behind).
            int day = save.challengeDay;
            foreach (ChallengeUpdate update in updates) {
                ChallengeDefinition challenge = update.Challenge;
                if (!update.Completed) {
                    sink.Send(player, new RetentionProgressBroadcast { day = day, index = update.Index, progress = update.Progress });
                    continue;
                }
                sink.Send(player, new RetentionChallengeBroadcast {
                    index = update.Index, id = challenge.id, text = challenge.Describe(), progress = update.Progress,
                    target = challenge.target, completed = true, xpReward = challenge.xpReward, goldReward = challenge.goldReward, day = day,
                });
                Grant(player, save, new RewardLine(challenge.CardLabel(), challenge.xpReward, challenge.goldReward), true, Source.Daily, null);
                sink.SaveSoon(player);
            }
        }

        /// <summary>
        /// Pays a reward: game XP, account XP and gold. Outside <paramref name="summary"/> (a reward during
        /// play) it is announced at once and listed on the open match; inside one the card announces it.
        /// </summary>
        void Grant(RetentionPlayer player, RetentionSave save, RewardLine line, bool announce, Source source, MatchSummary summary) {
            if (line.IsEmpty) return;
            if (source == Source.Daily && !player.Saved) {
                // It would be paid again on every rejoin: game XP only (session), no account currency.
                line = new RewardLine(line.Label, line.Xp, 0, line.Count, line.Detail);
            } else {
                PayAccount(player, line.Xp, line.Gold);
            }
            XpChange change = Rules.AddXp(save, line.Xp);
            (summary ?? player.Match)?.Add(line);
            if (line.Xp > 0 && summary == null) {
                sink.Send(player, new RetentionXpBroadcast {
                    xp = line.Xp, reason = line.Label, level = change.After.Level, levelXp = change.After.LevelXp,
                    levelXpNeeded = change.After.LevelXpNeeded, totalXp = save.xp, announce = announce,
                });
            }
            if (!change.LeveledUp) return;

            // A fresh list: an Unlocked handler may grant more XP while this one is being raised.
            var unlocks = new List<UnlockDefinition>();
            Rules.Track.UnlocksBetween(change.Before.Level, change.After.Level, unlocks);
            if (summary == null) {
                var views = new RetentionUnlockView[unlocks.Count];
                for (int i = 0; i < views.Length; i++) views[i] = RetentionRules.View(unlocks[i]);
                UnlockDefinition next = Rules.Track.NextUnlock(change.After.Level);
                sink.Send(player, new RetentionLevelUpBroadcast {
                    previousLevel = change.Before.Level, level = change.After.Level, unlocks = views,
                    hasNextUnlock = next != null, nextUnlock = RetentionRules.View(next),
                });
            }
            foreach (UnlockDefinition unlock in unlocks) sink.Unlocked(player, unlock);
        }

        /// <summary>Pays account XP (scaled, with the fraction carried to the next payout) and gold.</summary>
        void PayAccount(RetentionPlayer player, int xp, int gold) {
            double total = xp * (double)Rules.Tuning.accountXpScale + player.accountXpCarry;
            int whole = total >= int.MaxValue ? int.MaxValue : (int)Math.Floor(Math.Max(0d, total));
            player.accountXpCarry = Math.Clamp(total - whole, 0d, 1d);
            if (whole > 0 || gold > 0) sink.PayAccount(player, whole, Math.Max(0, gold));
        }
    }
}
