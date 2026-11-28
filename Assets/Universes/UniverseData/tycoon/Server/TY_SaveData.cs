using System;
using RyanAssets.Progress;
using RyanAssets.Retention;

namespace Universes.UniverseData.tycoon.Server {
    /// <summary>
    /// What Brick Tycoon remembers about a player between sessions: their wallet, rebirth progress and
    /// the build of their plot (restored when they claim a plot again), their goal, lifetime counters,
    /// daily chest, when they were last seen (for offline earnings) and the retention kit's block
    /// (level, daily challenges, streak). Written only by the server.
    /// </summary>
    public sealed class TY_SaveData : ProgressData {
        public const string Universe = TY_Retention.Universe;
        public override int LatestVersion => 1;

        /// <summary><see cref="goalIndex"/> of a save written before goals existed: the server starts it at the first goal not met yet.</summary>
        public const int GoalNotStarted = -1;

        public double cash;
        /// <summary>Cash earned in total, across rebirths.</summary>
        public double lifetime;
        public int rebirths;
        public int tokens;
        public TY_PerkLevels perks;
        /// <summary>Purchased items as a <see cref="TY_Catalog"/> bit mask; 0 when nothing is built yet.</summary>
        public ulong built;

        /// <summary>The current goal of <see cref="TY_Goals"/>, or <see cref="GoalNotStarted"/>.</summary>
        public int goalIndex = GoalNotStarted;
        /// <summary>Gold coins grabbed and collector emptied, lifetime (goal stats).</summary>
        public int coins, collects;
        /// <summary><see cref="RetentionDay"/> the daily chest was last opened.</summary>
        public int chestDay = RetentionDay.None;
        /// <summary>UTC time of the last save, in Unix seconds: the start of the time away.</summary>
        public long lastSeenUnix;
        /// <summary>Investment levels bought (each adds income for good, kept through rebirths).</summary>
        public int invested;
        /// <summary>The best income per second (rush hours left out) the player's plot has reached; the daily chest pays from it.</summary>
        public double peakIncome;

        /// <summary>Levels, daily challenges and the play streak (retention kit).</summary>
        public RetentionSave retention = new();

        /// <summary>True for a player who has never earned or built anything (nothing to restore).</summary>
        public bool IsEmpty => cash <= 0 && lifetime <= 0 && rebirths == 0 && tokens == 0 && built == 0 && perks.Equals(default(TY_PerkLevels))
            && goalIndex <= 0 && coins == 0 && collects == 0 && chestDay == RetentionDay.None && invested == 0
            && peakIncome <= 0;

        public override void Validate() {
            cash = Finite(cash);
            lifetime = Math.Max(Finite(lifetime), cash);
            rebirths = Math.Max(0, rebirths);
            tokens = Math.Max(0, tokens);
            foreach (TY_Perk perk in TY_Perks.All)
                perks = perks.WithLevel(perk.id, Math.Clamp(perks.Level(perk.id), 0, perk.maxLevel));
            // Unknown items (a catalog that shrank) are dropped; the rebirth is never "built".
            if (built != 0) built = (built & TY_Catalog.AllButRebirth) | TY_Catalog.StarterMask;
            goalIndex = Math.Max(GoalNotStarted, goalIndex);
            coins = Math.Max(0, coins);
            collects = Math.Max(0, collects);
            chestDay = Math.Max(RetentionDay.None, chestDay);
            lastSeenUnix = Math.Max(0L, lastSeenUnix);
            invested = Math.Max(0, invested);
            peakIncome = Finite(peakIncome);
            retention = RetentionSave.Repair(retention);
        }

        static double Finite(double value) => double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0, value);
    }
}
