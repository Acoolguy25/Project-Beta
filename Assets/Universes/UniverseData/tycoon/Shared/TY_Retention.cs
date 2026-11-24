using System.Globalization;
using RyanAssets.Retention;

namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// Brick Tycoon on the shared retention kit: its level unlocks (each changes how the tycoon plays,
    /// checked by the server when it is used), its daily challenge pool and the stat keys the server
    /// reports. A tycoon has no matches, so a rebirth closes the "match": its summary card lists what
    /// the run earned.
    /// </summary>
    public static class TY_Retention {
        /// <summary>The save document's universe id (also seeds the daily challenge pick).</summary>
        public const string Universe = "tycoon";

        // Unlock ids: never rename one that shipped.
        public const string LuckyCoins = "lucky_coins";
        public const string NightShift = "night_shift";
        public const string BigHopper = "big_hopper";
        public const string CoinRain = "coin_rain";
        public const string Overtime = "overtime";
        public const string RushVip = "rush_vip";
        public const string TokenPress = "token_press";
        public const string HeadOffice = "head_office";
        public const string Legend = "tycoon_legend";

        // Stat keys reported to the kit (Count unless noted).
        public const string StatCollect = "collect";
        public const string StatCoins = "coins";
        public const string StatBuilds = "builds";
        public const string StatRebirth = "rebirth";
        /// <summary>A rush hour ended while the player had a plot.</summary>
        public const string StatRush = "rush";
        public const string StatGoals = "goals";
        /// <summary>A minute on a claimed plot.</summary>
        public const string StatMinutes = "minutes";
        /// <summary>Cash earned (in dollars).</summary>
        public const string StatEarn = "earn";
        /// <summary>Income per second, reported with Reach.</summary>
        public const string StatIncome = "income";

        /// <summary>The level unlocks, worded from <paramref name="tuning"/> and <paramref name="offline"/>.</summary>
        public static UnlockDefinition[] Unlocks(TY_UnlockTuning tuning, TY_OfflineEarnings offline) {
            tuning ??= new TY_UnlockTuning();
            offline ??= new TY_OfflineEarnings();
            return new[] {
                new UnlockDefinition(2, LuckyCoins, "Lucky Coins", $"Gold coins pay x{Num(tuning.luckyCoinsMultiplier)}.", "coins"),
                new UnlockDefinition(3, NightShift, "Night Shift", $"Earn while away for up to {Num(offline.nightShiftCapHours)} hours.", "hourglass"),
                new UnlockDefinition(5, BigHopper, "Big Hopper", $"Your collector holds x{Num(tuning.bigHopperCapacity)} as much.", "monetization_on"),
                new UnlockDefinition(7, CoinRain, "Coin Rain", $"Gold coins appear x{Num(1f / tuning.coinRainInterval)} as often.", "paid"),
                new UnlockDefinition(10, Overtime, "Overtime", $"While away you earn {offline.overtimeShare * 100f:0}% of your income, for up to {Num(offline.overtimeCapHours)} hours.", "timer"),
                new UnlockDefinition(12, RushVip, "Rush VIP", $"Your rush-hour multiplier goes up by {Num(tuning.rushVipBonus)}.", "local_fire_department"),
                new UnlockDefinition(15, TokenPress, "Token Press", $"+{tuning.tokenPressTokens} perk token every rebirth.", "star_medal"),
                new UnlockDefinition(20, HeadOffice, "Head Office", "Rebirths keep your vaults and Auto-Collector.", "castle"),
                new UnlockDefinition(25, Legend, "Tycoon Legend", $"x{Num(tuning.legendIncome)} income, forever.", "laurels_trophy"),
            };
        }

        /// <summary>The daily challenge pool: things any player can do in a session, whatever their rebirth count.</summary>
        public static ChallengeDefinition[] ChallengePool() => new[] {
            new ChallengeDefinition("collect_20", StatCollect, 20, 210, 25, "Empty your collector {target} times"),
            new ChallengeDefinition("coins_4", StatCoins, 4, 220, 30, "Grab {target} gold coins"),
            new ChallengeDefinition("coins_8", StatCoins, 8, 320, 45, "Grab {target} gold coins"),
            new ChallengeDefinition("builds_12", StatBuilds, 12, 210, 25, "Build {target} items"),
            new ChallengeDefinition("builds_25", StatBuilds, 25, 320, 45, "Build {target} items"),
            new ChallengeDefinition("rebirth_1", StatRebirth, 1, 360, 60, "Rebirth once"),
            new ChallengeDefinition("rush_2", StatRush, 2, 270, 40, "Keep your tycoon running through {target} rush hours"),
            new ChallengeDefinition("goals_3", StatGoals, 3, 250, 35, "Complete {target} tycoon goals"),
            new ChallengeDefinition("minutes_15", StatMinutes, 15, 200, 25, "Run your tycoon for {target} minutes"),
            new ChallengeDefinition("minutes_30", StatMinutes, 30, 300, 45, "Run your tycoon for {target} minutes"),
            new ChallengeDefinition("earn_50k", StatEarn, 50_000, 240, 35, "Earn $50K"),
            new ChallengeDefinition("income_500", StatIncome, 500, 250, 35, "Reach $500/s income"),
        };

        /// <summary>The tycoon's retention rules (build once, on the server runner).</summary>
        public static RetentionRules Rules(RetentionTuning tuning, TY_UnlockTuning unlocks, TY_OfflineEarnings offline) =>
            new(Universe, tuning ?? DefaultTuning(), Unlocks(unlocks, offline), ChallengePool());

        /// <summary>
        /// The tycoon's level track and streak: slower than the kit's defaults, because a tycoon pays XP all
        /// the time. Level 5 comes at about the first rebirth and level 25 after several days of play (the
        /// pacing test measures it); the three daily challenges together are worth about a level at level 12.
        /// The server runner starts from these values.
        /// </summary>
        public static RetentionTuning DefaultTuning() {
            var tuning = new RetentionTuning();
            tuning.levels.firstLevelXp = 250;
            tuning.levels.extraXpPerLevel = 30;
            tuning.levels.growth = 1.025f;
            tuning.streak.firstDayXp = 50;
            tuning.streak.xpPerExtraDay = 25;
            tuning.Validate();
            return tuning;
        }

        static string Num(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
