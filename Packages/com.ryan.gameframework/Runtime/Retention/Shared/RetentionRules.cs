using System;
using System.Collections.Generic;

namespace RyanAssets.Retention {
    /// <summary>A change of a player's XP.</summary>
    public readonly struct XpChange {
        public readonly LevelProgress Before;
        public readonly LevelProgress After;
        public readonly int Gained;

        public XpChange(LevelProgress before, LevelProgress after, int gained) {
            Before = before;
            After = after;
            Gained = gained;
        }

        public bool LeveledUp => After.Level > Before.Level;
    }

    /// <summary>A challenge whose progress just moved; <see cref="Completed"/> is true exactly once per challenge and day.</summary>
    public readonly struct ChallengeUpdate {
        /// <summary>Index in the day's challenge list.</summary>
        public readonly int Index;
        public readonly ChallengeDefinition Challenge;
        public readonly int Progress;
        public readonly bool Completed;

        public ChallengeUpdate(int index, ChallengeDefinition challenge, int progress, bool completed) {
            Index = index;
            Challenge = challenge;
            Progress = progress;
            Completed = completed;
        }
    }

    /// <summary>What reporting a stat would do to a save (see <see cref="RetentionRules.Preview"/>).</summary>
    public enum StatEffect {
        /// <summary>No unfinished challenge of the day counts the stat, or the value raises nothing.</summary>
        None,
        /// <summary>A challenge moves but none completes.</summary>
        Progress,
        /// <summary>At least one challenge completes (and pays).</summary>
        Completes,
    }

    /// <summary>
    /// One game's retention rules: its universe id, level track, unlock table, challenge pool and
    /// tuning, plus the pure operations on a player's <see cref="RetentionSave"/> (no networking, no
    /// clock: the day is passed in), so everything here is unit-testable. The definitions are fixed
    /// once built; the only mutable state is a cache of the last two days' picks, so use one instance
    /// from the main thread (as the server and the HUD do). Build it once per game, typically from a
    /// static table in the game's Shared assembly.
    /// <para>
    /// Clock skew: servers' clocks differ by a few seconds, so a player can finish a match just after
    /// midnight on one server and join another that still reads the previous day. A save dated exactly
    /// one day ahead of the given day is therefore treated as current (<see cref="EffectiveDay"/>):
    /// nothing is reset and nothing pays twice. Only a save more than one day ahead is reset.
    /// </para>
    /// </summary>
    public sealed class RetentionRules {
        readonly ChallengeDefinition[] pool;
        readonly Dictionary<string, ChallengeDefinition> poolById = new(StringComparer.Ordinal);
        readonly HashSet<string> statKeys = new(StringComparer.Ordinal);
        // Two-day pick cache: today and the day a slightly-ahead save belongs to.
        readonly ChallengeDefinition[][] pickCache = new ChallengeDefinition[2][];
        readonly int[] pickCacheDay = { int.MinValue, int.MinValue };
        int pickCacheNext;
        readonly List<ChallengeDefinition> pickBuffer = new();

        /// <param name="universeId">The game's universe id; seeds the daily pick (use the save's universe id).</param>
        /// <exception cref="ArgumentException">Missing universe id, or an invalid unlock or challenge definition.</exception>
        public RetentionRules(string universeId, RetentionTuning tuning, IEnumerable<UnlockDefinition> unlocks, IEnumerable<ChallengeDefinition> challengePool) {
            if (string.IsNullOrWhiteSpace(universeId)) throw new ArgumentException("A universe id is required.", nameof(universeId));
            UniverseId = universeId;
            Tuning = (tuning ?? new RetentionTuning()).Clone();
            Tuning.Validate();
            Track = new LevelTrack(Tuning.levels, unlocks);

            var list = new List<ChallengeDefinition>();
            if (challengePool != null) {
                foreach (ChallengeDefinition challenge in challengePool) {
                    Check(challenge);
                    if (!poolById.TryAdd(challenge.id, challenge)) throw new ArgumentException($"Challenge id '{challenge.id}' is used twice.", nameof(challengePool));
                    list.Add(challenge);
                    statKeys.Add(challenge.statKey);
                }
            }
            pool = list.ToArray();
        }

        static void Check(ChallengeDefinition challenge) {
            const string param = "challengePool";
            if (challenge == null) throw new ArgumentException("The challenge pool contains a null entry.", param);
            if (string.IsNullOrWhiteSpace(challenge.id) || challenge.id.Length > RetentionSave.MaxIdLength)
                throw new ArgumentException($"Challenge '{challenge.text}' needs an id of 1..{RetentionSave.MaxIdLength} characters.", param);
            if (string.IsNullOrWhiteSpace(challenge.statKey)) throw new ArgumentException($"Challenge '{challenge.id}' has no stat key.", param);
            if (challenge.target < 1) throw new ArgumentException($"Challenge '{challenge.id}' needs a target of at least 1.", param);
            if (challenge.xpReward < 0 || challenge.goldReward < 0) throw new ArgumentException($"Challenge '{challenge.id}' has a negative reward.", param);
        }

        public string UniverseId { get; }
        /// <summary>A validated copy of the tuning the rules were built with.</summary>
        public RetentionTuning Tuning { get; }
        public LevelTrack Track { get; }
        public IReadOnlyList<ChallengeDefinition> ChallengePool => pool;

        public ChallengeDefinition FindChallenge(string id) =>
            id != null && poolById.TryGetValue(id, out ChallengeDefinition challenge) ? challenge : null;

        /// <summary>True when some challenge of the pool counts <paramref name="statKey"/> (a free first test).</summary>
        public bool UsesStat(string statKey) => statKey != null && statKeys.Contains(statKey);

        /// <summary>The challenges of <paramref name="day"/> (the last two days asked are cached).</summary>
        public IReadOnlyList<ChallengeDefinition> ChallengesFor(int day) {
            for (int i = 0; i < pickCache.Length; i++)
                if (pickCacheDay[i] == day) return pickCache[i];
            pickBuffer.Clear();
            DailyChallenges.Pick(pool, Tuning.dailyChallengeCount, day, UniverseId, pickBuffer);
            int slot = pickCacheNext;
            pickCacheNext = (pickCacheNext + 1) % pickCache.Length;
            pickCache[slot] = pickBuffer.ToArray();
            pickCacheDay[slot] = day;
            return pickCache[slot];
        }

        /// <summary>True when one of <paramref name="day"/>'s challenges counts <paramref name="statKey"/>. Allocation-free.</summary>
        public bool CountsToday(int day, string statKey) {
            if (!UsesStat(statKey)) return false;
            IReadOnlyList<ChallengeDefinition> today = ChallengesFor(day);
            for (int i = 0; i < today.Count; i++)
                if (string.Equals(today[i].statKey, statKey, StringComparison.Ordinal)) return true;
            return false;
        }

        // ---------------------------------------------------------------- day rollover

        /// <summary>
        /// The day <paramref name="save"/> is played on when the clock reads <paramref name="day"/>:
        /// <paramref name="day"/> + 1 when the save's challenges or streak are dated exactly one day
        /// ahead (this server's clock is a little behind the one that wrote it), else <paramref name="day"/>.
        /// Every operation here maps the day through it first.
        /// </summary>
        public int EffectiveDay(RetentionSave save, int day) {
            if (save == null || day == int.MaxValue) return day;
            return save.challengeDay == day + 1 || save.streakDay == day + 1 ? day + 1 : day;
        }

        /// <summary>
        /// Brings a save up to <paramref name="day"/> (through <see cref="EffectiveDay"/>):
        /// <list type="bullet">
        /// <item>A different day (older, or more than one day ahead) gets that day's challenges at zero.</item>
        /// <item>On the same day the entries are matched by id to the current pick, so lowered targets are
        /// clamped. Entries of the day that are no longer picked (a pool edit, or two builds live during a
        /// rolling deploy) are dropped unless they are done: done ids are kept behind the pick, so a
        /// challenge that comes back the same day never pays twice.</item>
        /// <item>A streak dated more than one day ahead is reset.</item>
        /// </list>
        /// Returns true when the challenge list changed.
        /// </summary>
        public bool Refresh(RetentionSave save, int day) {
            if (save == null) throw new ArgumentNullException(nameof(save));
            Bring(save, day, out bool changed);
            return changed;
        }

        /// <summary>Refreshes <paramref name="save"/> and returns the effective day it is now on.</summary>
        int Bring(RetentionSave save, int day, out bool changed) {
            day = EffectiveDay(save, day);
            changed = false;
            if (save.streakDay > day) {
                save.streakDay = RetentionDay.None;
                save.streakDays = 0;
            }

            IReadOnlyList<ChallengeDefinition> today = ChallengesFor(day);
            save.challenges ??= new List<ChallengeSave>();
            bool sameDay = save.challengeDay == day;
            if (sameDay && InPickOrder(save.challenges, today)) {
                for (int i = 0; i < today.Count; i++) Clamp(save.challenges[i], today[i]);
                return day;
            }

            var fresh = new List<ChallengeSave>(Math.Max(today.Count, Math.Min(save.challenges.Count, RetentionSave.MaxChallenges)));
            foreach (ChallengeDefinition challenge in today) {
                ChallengeSave entry = sameDay ? Find(save.challenges, challenge.id) : null;
                entry ??= new ChallengeSave(challenge.id);
                Clamp(entry, challenge);
                fresh.Add(entry);
            }
            if (sameDay) {
                // Keep the day's done ids that are not picked now, so they cannot be earned again today.
                foreach (ChallengeSave old in save.challenges)
                    if (old != null && old.done && fresh.Count < RetentionSave.MaxChallenges && !fresh.Contains(old)) fresh.Add(old);
            }
            save.challenges = fresh;
            save.challengeDay = day;
            changed = true;
            return day;
        }

        static ChallengeSave Find(List<ChallengeSave> saved, string id) {
            for (int i = 0; i < saved.Count; i++)
                if (saved[i] != null && saved[i].id == id) return saved[i];
            return null;
        }

        /// <summary>The day's picks lead the list in order; extra (done) entries may follow.</summary>
        static bool InPickOrder(List<ChallengeSave> saved, IReadOnlyList<ChallengeDefinition> today) {
            if (saved.Count < today.Count) return false;
            for (int i = 0; i < today.Count; i++)
                if (saved[i] == null || saved[i].id != today[i].id) return false;
            return true;
        }

        static void Clamp(ChallengeSave entry, ChallengeDefinition challenge) =>
            entry.progress = Math.Clamp(entry.progress, 0, challenge.target);

        // ---------------------------------------------------------------- XP and levels

        public LevelProgress Progress(RetentionSave save) => Track.Progress(save.xp);

        /// <summary>Adds game XP (ignored when not positive) and reports the level before and after.</summary>
        public XpChange AddXp(RetentionSave save, int xp) {
            LevelProgress before = Track.Progress(save.xp);
            int gained = Math.Max(0, xp);
            save.xp = Math.Min(RetentionSave.MaxXp, save.xp + gained);
            return new XpChange(before, Track.Progress(save.xp), gained);
        }

        // ---------------------------------------------------------------- daily challenges

        /// <summary>
        /// Adds <paramref name="amount"/> to today's unfinished challenges counting <paramref name="statKey"/>.
        /// Each that moves is added to <paramref name="updates"/>; one that reaches its target is marked
        /// done and reported as completed (the caller pays it) exactly once.
        /// </summary>
        public void Count(RetentionSave save, int day, string statKey, int amount, List<ChallengeUpdate> updates) {
            if (amount > 0) Advance(save, day, statKey, amount, true, updates);
        }

        /// <summary>Raises today's unfinished <paramref name="statKey"/> challenges to at least <paramref name="value"/> (bests: "reach wave 15").</summary>
        public void Reach(RetentionSave save, int day, string statKey, int value, List<ChallengeUpdate> updates) {
            if (value > 0) Advance(save, day, statKey, value, false, updates);
        }

        /// <summary>
        /// What <see cref="Count"/> (<paramref name="accumulate"/>) or <see cref="Reach"/> with
        /// <paramref name="value"/> would do, without changing the save. Allocation-free, so the server
        /// can test every report and skip the ones that change nothing.
        /// </summary>
        public StatEffect Preview(RetentionSave save, int day, string statKey, long value, bool accumulate) {
            if (save == null || value <= 0 || !UsesStat(statKey)) return StatEffect.None;
            day = EffectiveDay(save, day);
            IReadOnlyList<ChallengeDefinition> today = ChallengesFor(day);
            bool current = save.challengeDay == day && save.challenges != null;
            StatEffect effect = StatEffect.None;
            for (int i = 0; i < today.Count; i++) {
                ChallengeDefinition challenge = today[i];
                if (!string.Equals(challenge.statKey, statKey, StringComparison.Ordinal)) continue;
                ChallengeSave entry = current ? Find(save.challenges, challenge.id) : null;
                if (entry != null && entry.done) continue;
                int progress = entry == null ? 0 : Math.Clamp(entry.progress, 0, challenge.target);
                long raised = accumulate ? progress + value : Math.Max(progress, value);
                if (raised >= challenge.target) return StatEffect.Completes;
                if (raised > progress) effect = StatEffect.Progress;
            }
            return effect;
        }

        void Advance(RetentionSave save, int day, string statKey, int value, bool accumulate, List<ChallengeUpdate> updates) {
            if (string.IsNullOrEmpty(statKey)) return;
            day = Bring(save, day, out _);
            IReadOnlyList<ChallengeDefinition> today = ChallengesFor(day);
            for (int i = 0; i < today.Count; i++) {
                ChallengeDefinition challenge = today[i];
                ChallengeSave entry = save.challenges[i];
                if (entry.done || !string.Equals(challenge.statKey, statKey, StringComparison.Ordinal)) continue;
                long raised = accumulate ? (long)entry.progress + value : Math.Max(entry.progress, value);
                int progress = (int)Math.Min(challenge.target, raised);
                bool completed = progress >= challenge.target;
                if (progress == entry.progress && !completed) continue;
                entry.progress = progress;
                if (completed) {
                    entry.done = true;
                    if (save.challengesCompleted < int.MaxValue) save.challengesCompleted++;
                }
                updates?.Add(new ChallengeUpdate(i, challenge, progress, completed));
            }
        }

        // ---------------------------------------------------------------- streak

        /// <summary>The streak as it stands today: its length while alive (paid today or yesterday), else 0.</summary>
        public int CurrentStreak(RetentionSave save, int day) {
            day = EffectiveDay(save, day);
            return save.streakDay == day || save.streakDay == day - 1 ? save.streakDays : 0;
        }

        /// <summary>
        /// Pays today's streak bonus if it is still unpaid: one more day after yesterday, or day 1
        /// after a gap. The caller adds <see cref="StreakClaim.Xp"/> and pays the gold.
        /// </summary>
        public StreakClaim ClaimStreak(RetentionSave save, int day) {
            day = EffectiveDay(save, day);
            if (save.streakDay > day) {
                save.streakDay = RetentionDay.None;
                save.streakDays = 0;
            }
            if (save.streakDay == day) return new StreakClaim(false, save.streakDays, 0, 0, false);
            bool continues = save.streakDay == day - 1 && save.streakDays > 0;
            bool restarted = !continues && save.streakDays >= 2;
            int days = continues ? (save.streakDays < int.MaxValue ? save.streakDays + 1 : int.MaxValue) : 1;
            save.streakDays = days;
            save.streakDay = day;
            save.bestStreak = Math.Max(save.bestStreak, days);
            return new StreakClaim(true, days, Tuning.streak.BonusXp(days), Tuning.streak.BonusGold(days), restarted);
        }

        // ---------------------------------------------------------------- wire

        /// <summary>The full HUD state for a player at <paramref name="utcNow"/> (refreshes the save to that day first).</summary>
        public RetentionSnapshotBroadcast Snapshot(RetentionSave save, DateTime utcNow, bool saved) {
            int clockDay = RetentionDay.Of(utcNow);
            int day = Bring(save, clockDay, out _);
            IReadOnlyList<ChallengeDefinition> today = ChallengesFor(day);
            var challenges = new RetentionChallengeView[today.Count];
            for (int i = 0; i < today.Count; i++) {
                ChallengeDefinition challenge = today[i];
                ChallengeSave entry = save.challenges[i];
                challenges[i] = new RetentionChallengeView {
                    id = challenge.id, text = challenge.Describe(), progress = entry.progress, target = challenge.target,
                    xpReward = challenge.xpReward, goldReward = challenge.goldReward, done = entry.done,
                };
            }

            LevelProgress progress = Track.Progress(save.xp);
            int streak = CurrentStreak(save, day);
            // Unclaimed: today's claim extends the streak to streak + 1; claimed: tomorrow's does.
            int nextStreakDay = streak + 1;
            UnlockDefinition next = Track.NextUnlock(progress.Level);
            return new RetentionSnapshotBroadcast {
                universeId = UniverseId,
                level = progress.Level, levelXp = progress.LevelXp, levelXpNeeded = progress.LevelXpNeeded,
                totalXp = save.xp, maxLevel = Track.MaxLevel,
                challenges = challenges,
                // A save a day ahead of this server's clock resets at the end of its own day.
                secondsToReset = RetentionDay.SecondsUntilReset(utcNow) + (day - clockDay) * RetentionDay.SecondsPerDay,
                streakDays = streak, streakClaimedToday = save.streakDay == day,
                streakNextXp = Tuning.streak.BonusXp(nextStreakDay), streakNextGold = Tuning.streak.BonusGold(nextStreakDay),
                bestStreak = save.bestStreak,
                hasNextUnlock = next != null, nextUnlock = View(next),
                notSaved = !saved,
                challengeDay = day,
            };
        }

        /// <summary>The summary card message for <paramref name="summary"/>.</summary>
        public RetentionSummaryBroadcast SummaryBroadcast(MatchSummary summary) {
            var lines = new RetentionRewardView[summary.Lines.Count];
            for (int i = 0; i < lines.Length; i++) {
                RewardLine line = summary.Lines[i];
                lines[i] = new RetentionRewardView { label = line.Label, xp = line.Xp, gold = line.Gold, count = line.Count, detail = line.Detail };
            }
            var unlocks = new RetentionUnlockView[summary.Unlocks.Count];
            for (int i = 0; i < unlocks.Length; i++) unlocks[i] = View(summary.Unlocks[i]);
            UnlockDefinition next = Track.NextUnlock(summary.After.Level);
            return new RetentionSummaryBroadcast {
                title = summary.Title ?? string.Empty,
                lines = lines, totalXp = summary.TotalXp, totalGold = summary.TotalGold,
                levelBefore = summary.Before.Level, levelXpBefore = summary.Before.LevelXp, levelXpNeededBefore = summary.Before.LevelXpNeeded,
                levelAfter = summary.After.Level, levelXpAfter = summary.After.LevelXp, levelXpNeededAfter = summary.After.LevelXpNeeded,
                unlocks = unlocks, streakDays = summary.StreakDays,
                hasNextUnlock = next != null, nextUnlock = View(next),
            };
        }

        /// <summary>The wire form of an unlock (default when null).</summary>
        public static RetentionUnlockView View(UnlockDefinition unlock) => unlock == null ? default : new RetentionUnlockView {
            id = unlock.id, level = unlock.level, title = unlock.title, description = unlock.description, icon = unlock.icon,
        };
    }
}
