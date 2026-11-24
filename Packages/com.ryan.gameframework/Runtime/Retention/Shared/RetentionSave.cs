using System;
using System.Collections.Generic;

namespace RyanAssets.Retention {
    /// <summary>
    /// A player's retention progress in one game: level XP, today's challenges, the play streak. A game
    /// embeds it as ONE public field of its existing <c>&lt;Prefix&gt;_SaveData : ProgressData</c>, so it
    /// is saved in the same document (no new backend row), and repairs it from its Validate:
    /// <code>
    /// public RetentionSave retention = new();
    /// public override void Validate() { retention = RetentionSave.Repair(retention); ... }
    /// </code>
    /// Plain public fields only (the progress codec saves public fields); a few hundred bytes of JSON.
    /// Only the server writes it, through <c>RetentionService</c>.
    /// </summary>
    [Serializable]
    public sealed class RetentionSave {
        /// <summary>Most challenge entries kept (a day shows at most this many).</summary>
        public const int MaxChallenges = 8;
        /// <summary>Upper bound of <see cref="xp"/>, far above any track's maximum, so sums never overflow.</summary>
        public const long MaxXp = 1_000_000_000_000_000L;
        /// <summary>Longest id kept; longer saved ids are garbage and dropped.</summary>
        public const int MaxIdLength = 64;

        /// <summary>Total game XP (the level comes from the game's <see cref="LevelTrack"/>).</summary>
        public long xp;
        /// <summary><see cref="RetentionDay"/> the <see cref="challenges"/> belong to (<see cref="RetentionDay.None"/> = never).</summary>
        public int challengeDay = RetentionDay.None;
        /// <summary>Progress on that day's challenges, in pick order.</summary>
        public List<ChallengeSave> challenges = new();
        /// <summary>Length of the current play streak in days.</summary>
        public int streakDays;
        /// <summary>The day the streak bonus was last paid (<see cref="RetentionDay.None"/> = never).</summary>
        public int streakDay = RetentionDay.None;
        public int bestStreak;
        /// <summary>Matches finished in this game (lifetime).</summary>
        public int matchesFinished;
        /// <summary>Daily challenges completed (lifetime).</summary>
        public int challengesCompleted;

        /// <summary>
        /// Repairs values that need no game context: negative or overflowing numbers, impossible days,
        /// null, empty, overlong or duplicate challenge entries. Day rollover and challenge ids that
        /// are no longer in the pool are handled by <see cref="RetentionRules.Refresh"/>.
        /// </summary>
        public void Validate() {
            xp = Math.Clamp(xp, 0L, MaxXp);
            challengeDay = Math.Max(RetentionDay.None, challengeDay);
            streakDay = Math.Max(RetentionDay.None, streakDay);
            streakDays = streakDay == RetentionDay.None ? 0 : Math.Max(0, streakDays);
            bestStreak = Math.Max(streakDays, bestStreak);
            matchesFinished = Math.Max(0, matchesFinished);
            challengesCompleted = Math.Max(0, challengesCompleted);

            challenges ??= new List<ChallengeSave>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            challenges.RemoveAll(c => c == null || string.IsNullOrEmpty(c.id) || c.id.Length > MaxIdLength || !seen.Add(c.id));
            if (challenges.Count > MaxChallenges) challenges.RemoveRange(MaxChallenges, challenges.Count - MaxChallenges);
            foreach (ChallengeSave challenge in challenges) challenge.progress = Math.Max(0, challenge.progress);
        }

        /// <summary>Returns <paramref name="block"/> validated, or a new block when it is missing (an older save).</summary>
        public static RetentionSave Repair(RetentionSave block) {
            block ??= new RetentionSave();
            block.Validate();
            return block;
        }
    }

    /// <summary>Saved progress on one of the day's challenges.</summary>
    [Serializable]
    public sealed class ChallengeSave {
        public string id;
        public int progress;
        /// <summary>Completed and paid; it never pays again.</summary>
        public bool done;

        public ChallengeSave() { }

        public ChallengeSave(string id) => this.id = id;
    }
}
