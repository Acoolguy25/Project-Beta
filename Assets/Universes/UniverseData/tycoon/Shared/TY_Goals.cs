using System;

namespace Universes.UniverseData.tycoon {
    /// <summary>What a tycoon goal measures. Build counts are the current plot's; the rest are lifetime totals.</summary>
    public enum TY_GoalStat : byte {
        Collects, Droppers, Upgraders, Buildings, CollectorUpgrades, Built, Income, Lifetime, Coins, Rebirths, Landmarks, PerkLevels,
    }

    /// <summary>The numbers goals are checked against, filled in by the server once a second.</summary>
    public struct TY_GoalStats {
        /// <summary>The player's plot build (0 without a plot).</summary>
        public ulong purchased;
        /// <summary>Collector emptied, gold coins grabbed (lifetime).</summary>
        public long collects, coins;
        /// <summary>Cash earned in total; income per second as the wallet shows it (rush hours included).</summary>
        public long lifetime, income;
        public long rebirths;
        /// <summary>Rebirth perk levels bought, summed over all perks.</summary>
        public long perkLevels;

        public readonly long Get(TY_GoalStat stat) => stat switch {
            TY_GoalStat.Collects => collects,
            TY_GoalStat.Droppers => TY_Catalog.OwnedOfKind(purchased, TY_ItemKind.Dropper),
            TY_GoalStat.Upgraders => TY_Catalog.OwnedOfKind(purchased, TY_ItemKind.Upgrader),
            TY_GoalStat.Buildings => TY_Catalog.OwnedOfKind(purchased, TY_ItemKind.Building),
            TY_GoalStat.CollectorUpgrades => TY_Catalog.OwnedOfKind(purchased, TY_ItemKind.Utility),
            TY_GoalStat.Built => TY_Catalog.OwnedCount(purchased & TY_Catalog.BaseMask),
            TY_GoalStat.Income => income,
            TY_GoalStat.Lifetime => lifetime,
            TY_GoalStat.Coins => coins,
            TY_GoalStat.Rebirths => rebirths,
            TY_GoalStat.Landmarks => TY_Catalog.OwnedOfKind(purchased, TY_ItemKind.Landmark),
            TY_GoalStat.PerkLevels => perkLevels,
            _ => 0,
        };
    }

    /// <summary>One step of the goal chain: what to reach and what it pays.</summary>
    public readonly struct TY_Goal {
        public readonly TY_GoalStat stat;
        public readonly long target;
        /// <summary>What the player reads; "{0}" is the target ("$25K" for money goals).</summary>
        public readonly string text;
        /// <summary>Cash paid at least.</summary>
        public readonly long cash;
        /// <summary>Seconds of the player's income paid (when that is more than <see cref="cash"/>), so late goals stay worth having.</summary>
        public readonly float incomeSeconds;
        /// <summary>Rebirth perk tokens paid.</summary>
        public readonly int tokens;
        /// <summary>Game XP paid (through the retention kit, which also pays account XP).</summary>
        public readonly int xp;

        public TY_Goal(TY_GoalStat stat, long target, string text, long cash = 0, float incomeSeconds = 0f, int tokens = 0, int xp = 0) {
            this.stat = stat; this.target = target; this.text = text;
            this.cash = cash; this.incomeSeconds = incomeSeconds; this.tokens = tokens; this.xp = xp;
        }

        public bool IsMoney => stat is TY_GoalStat.Income or TY_GoalStat.Lifetime;

        /// <summary>A value of this goal's stat as the player reads it ("$12.5K", "3").</summary>
        public string Format(long value) => IsMoney ? $"${TY_Catalog.Money(value)}" + (stat == TY_GoalStat.Income ? "/s" : "") : value.ToString();

        public string Describe() => string.Format(text, Format(target));

        /// <summary>
        /// The cash this goal pays a player earning <paramref name="incomePerSecond"/> (without rush hours).
        /// An income goal is paid at the income it asks for (or more), so the card shows what the goal
        /// will pay when it is met, not what the player earns before.
        /// </summary>
        public long CashReward(double incomePerSecond) {
            double basis = double.IsNaN(incomePerSecond) ? 0d : Math.Max(0d, incomePerSecond);
            if (stat == TY_GoalStat.Income) basis = Math.Max(basis, target);
            return Math.Max(cash, basis > 0 ? (long)Math.Min(long.MaxValue / 4d, basis * incomeSeconds) : 0);
        }

        /// <summary>
        /// The goal pays seconds of income that the player's income sets, not a fixed amount (an income goal
        /// pays at its target, which is fixed): the card names the seconds, since right after a rebirth the
        /// dollars would read small.
        /// </summary>
        public bool PaysIncomeSeconds => incomeSeconds > 0f && stat != TY_GoalStat.Income;
    }

    /// <summary>
    /// The tycoon's goal chain: one goal at a time, always on the HUD, each paying cash, tokens and XP.
    /// The authored chain teaches the game (collect, build each lane, rebirth, landmarks, perks); after
    /// it, an endless tail alternates "rebirth N times" and "earn $X in total", so there is always a
    /// next goal. Shared so the server checks it and the client shows the same text.
    /// </summary>
    public static class TY_Goals {
        // Tail tuning: the chain after the authored goals.
        /// <summary>Each tail rebirth goal asks this many rebirths more than the one before.</summary>
        public const int TailRebirthStep = 2;
        /// <summary>Each tail lifetime goal asks this many times the one before.</summary>
        public const double TailLifetimeGrowth = 5;
        public const int TailRebirthTokens = 1, TailRebirthXp = 150, TailLifetimeXp = 120;
        public const float TailIncomeSeconds = 120f;

        static readonly TY_Goal[] Chain = {
            /* 0*/ new(TY_GoalStat.Collects, 1, "Empty your collector at the blue pad", cash: 25, xp: 15),
            /* 1*/ new(TY_GoalStat.Droppers, 2, "Build {0} droppers", cash: 40, xp: 15),
            /* 2*/ new(TY_GoalStat.Buildings, 1, "Put up your first building", cash: 60, xp: 15),
            /* 3*/ new(TY_GoalStat.CollectorUpgrades, 1, "Buy a Vault for your collector", cash: 80, xp: 15),
            /* 4*/ new(TY_GoalStat.Droppers, 3, "Build {0} droppers", cash: 120, xp: 15),
            /* 5*/ new(TY_GoalStat.Upgraders, 1, "Build an upgrader on the belt", cash: 200, xp: 15),
            /* 6*/ new(TY_GoalStat.Income, 25, "Reach {0} income", cash: 250, xp: 15),
            /* 7*/ new(TY_GoalStat.Coins, 1, "Grab a gold coin", cash: 300, xp: 15),
            /* 8*/ new(TY_GoalStat.Built, 10, "Own {0} items on your plot", cash: 500, xp: 20),
            /* 9*/ new(TY_GoalStat.Lifetime, 5_000, "Earn {0} in total", cash: 800, xp: 20),
            /*10*/ new(TY_GoalStat.Upgraders, 2, "Build {0} upgraders", cash: 1_200, xp: 20),
            /*11*/ new(TY_GoalStat.Income, 150, "Reach {0} income", cash: 2_000, xp: 25),
            /*12*/ new(TY_GoalStat.Droppers, 5, "Build {0} droppers", cash: 3_000, xp: 25),
            /*13*/ new(TY_GoalStat.CollectorUpgrades, 4, "Get the Auto-Collector", cash: 4_000, xp: 25),
            /*14*/ new(TY_GoalStat.Lifetime, 50_000, "Earn {0} in total", cash: 5_000, xp: 30),
            /*15*/ new(TY_GoalStat.Built, TY_Catalog.BaseCount, "Finish your whole factory", incomeSeconds: 60f, xp: 60),
            /*16*/ new(TY_GoalStat.Rebirths, 1, "Rebirth for the first time", cash: 500, tokens: 1, xp: 60),
            /*17*/ new(TY_GoalStat.Landmarks, 1, "Build the Solar Farm landmark", incomeSeconds: 60f, xp: 50),
            /*18*/ new(TY_GoalStat.PerkLevels, 1, "Buy a rebirth perk (press P)", incomeSeconds: 60f, xp: 40),
            /*19*/ new(TY_GoalStat.Coins, 10, "Grab {0} gold coins", incomeSeconds: 90f, xp: 40),
            /*20*/ new(TY_GoalStat.Rebirths, 2, "Rebirth {0} times", tokens: 1, xp: 80),
            /*21*/ new(TY_GoalStat.Landmarks, 2, "Build {0} landmarks", incomeSeconds: 90f, xp: 60),
            /*22*/ new(TY_GoalStat.Income, 5_000, "Reach {0} income", incomeSeconds: 30f, xp: 60),
            /*23*/ new(TY_GoalStat.Lifetime, 1_000_000, "Earn {0} in total", tokens: 1, xp: 80),
            /*24*/ new(TY_GoalStat.Rebirths, 4, "Rebirth {0} times", tokens: 1, xp: 100),
            /*25*/ new(TY_GoalStat.Landmarks, 3, "Build {0} landmarks", incomeSeconds: 120f, xp: 80),
            /*26*/ new(TY_GoalStat.Coins, 25, "Grab {0} gold coins", incomeSeconds: 120f, xp: 60),
            /*27*/ new(TY_GoalStat.Rebirths, 6, "Rebirth {0} times", tokens: 1, xp: 100),
            /*28*/ new(TY_GoalStat.Landmarks, 4, "Build {0} landmarks", incomeSeconds: 120f, xp: 100),
            /*29*/ new(TY_GoalStat.Lifetime, 100_000_000, "Earn {0} in total", tokens: 1, xp: 100),
            /*30*/ new(TY_GoalStat.Rebirths, 9, "Rebirth {0} times", tokens: 2, xp: 150),
            /*31*/ new(TY_GoalStat.Landmarks, TY_Catalog.LandmarkCount, "Launch the Brick Rocket: own all {0} landmarks", incomeSeconds: 180f, xp: 200),
        };

        /// <summary>Goals written by hand; every index from here on is generated.</summary>
        public static int AuthoredCount => Chain.Length;

        /// <summary>Goal number <paramref name="index"/> (0-based). Every index has one: the chain never ends.</summary>
        public static TY_Goal Get(int index) {
            index = Math.Max(0, index);
            if (index < Chain.Length) return Chain[index];
            int tail = index - Chain.Length, step = tail / 2 + 1;
            if (tail % 2 == 0) {
                long rebirths = LastTarget(TY_GoalStat.Rebirths) + (long)TailRebirthStep * step;
                return new TY_Goal(TY_GoalStat.Rebirths, rebirths, "Rebirth {0} times", tokens: TailRebirthTokens, xp: TailRebirthXp);
            }
            double lifetime = LastTarget(TY_GoalStat.Lifetime) * Math.Pow(TailLifetimeGrowth, step);
            long target = lifetime >= long.MaxValue / 4d ? long.MaxValue / 4 : (long)lifetime;
            return new TY_Goal(TY_GoalStat.Lifetime, TY_Catalog.RoundPrice(target), "Earn {0} in total", incomeSeconds: TailIncomeSeconds, xp: TailLifetimeXp);
        }

        static long LastTarget(TY_GoalStat stat) {
            for (int i = Chain.Length - 1; i >= 0; i--) if (Chain[i].stat == stat) return Chain[i].target;
            return 0;
        }

        public static bool IsMet(int index, in TY_GoalStats stats) {
            TY_Goal goal = Get(index);
            return stats.Get(goal.stat) >= goal.target;
        }

        /// <summary>
        /// The first goal from <paramref name="from"/> that <paramref name="stats"/> has not met (at most
        /// <paramref name="limit"/> steps on). Used to start a player who played before goals existed
        /// where they are, without paying for the goals they passed long ago.
        /// </summary>
        public static int FirstUnmet(in TY_GoalStats stats, int from = 0, int limit = 1000) {
            int index = Math.Max(0, from);
            for (int step = 0; step < limit && IsMet(index, stats); step++) index++;
            return index;
        }
    }
}
