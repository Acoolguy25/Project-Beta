namespace Universes.UniverseData.tower_defense {
    /// <summary>The game modes players vote for before wave 1. A wire enum: APPEND-ONLY.</summary>
    public enum TD_Difficulty : byte { Normal, Hard, Endless }

    /// <summary>What a mode changes, and what it takes to play it.</summary>
    public readonly struct TD_ModeStats {
        public readonly string name;
        public readonly string blurb;
        /// <summary>Multiplier on every enemy's health.</summary>
        public readonly float healthScale;
        /// <summary>Multiplier on the cash each kill pays.</summary>
        public readonly float rewardScale;
        /// <summary>Multiplier on the end-of-wave cash bonus.</summary>
        public readonly float waveBonusScale;
        public readonly int startCash;
        /// <summary>Waves to win; 0 = endless (the game ends when the base falls).</summary>
        public readonly int waveCount;
        /// <summary>Multiplier on the level XP the game pays.</summary>
        public readonly float xpScale;
        /// <summary>What unlocks the mode, shown on its locked vote card.</summary>
        public readonly string lockedHint;

        public TD_ModeStats(string name, string blurb, float healthScale, float rewardScale, float waveBonusScale, int startCash, int waveCount, float xpScale, string lockedHint) {
            this.name = name; this.blurb = blurb; this.healthScale = healthScale; this.rewardScale = rewardScale;
            this.waveBonusScale = waveBonusScale; this.startCash = startCash; this.waveCount = waveCount; this.xpScale = xpScale;
            this.lockedHint = lockedHint;
        }

        public bool Endless => waveCount <= 0;
    }

    /// <summary>
    /// Normal, Hard and Endless: their numbers and the vote rules. Hard is opened by a Normal win and
    /// Endless by reaching the Endless unlock level; a player can only vote for a mode they have.
    /// </summary>
    public static class TD_Modes {
        public const int Count = 3;

        static readonly TD_ModeStats[] Modes = {
            new("Normal", "20 waves. Learn the enemies.", 1f, 1f, 1f, TD_Rules.StartCash, TD_Rules.WaveCount, 1f, ""),
            new("Hard", "20 waves. Tougher armies, less cash.", 1.25f, 0.9f, 0.85f, TD_Rules.StartCash + 50, TD_Rules.WaveCount, 1.6f, "Win on Normal to unlock"),
            new("Endless", "No last wave. How far can you hold?", 1f, 1f, 1f, TD_Rules.StartCash, 0, 1.2f, $"Reach level {TD_Retention.EndlessLevel} to unlock"),
        };

        public static TD_ModeStats Get(TD_Difficulty mode) => Modes[(int)mode < Modes.Length ? (int)mode : 0];

        public static bool IsValid(TD_Difficulty mode) => (int)mode < Modes.Length;

        /// <summary>
        /// Whether <paramref name="mode"/> is open to a player whose profile says <paramref name="modeUnlocked"/>
        /// (index = <see cref="TD_Difficulty"/>; null before the profile arrives). Normal always is.
        /// </summary>
        public static bool Open(bool[] modeUnlocked, TD_Difficulty mode) =>
            mode == TD_Difficulty.Normal || (modeUnlocked != null && (int)mode < modeUnlocked.Length && modeUnlocked[(int)mode]);

        /// <summary>
        /// The mode with the most votes (index = <see cref="TD_Difficulty"/>); a tie goes to the easier mode,
        /// and no votes at all to Normal.
        /// </summary>
        public static TD_Difficulty Winner(int[] votes) {
            int best = 0;
            if (votes != null)
                for (int i = 1; i < votes.Length && i < Count; i++)
                    if (votes[i] > votes[best]) best = i;
            return (TD_Difficulty)best;
        }
    }
}
