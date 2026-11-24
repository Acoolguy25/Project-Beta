using RyanAssets.Retention;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// Sky Obby's side of the shared retention kit: the level unlock table, the daily challenge pool
    /// and the stat keys the server reports. Shared, so the client draws the same unlocks the server
    /// announces. Obby unlocks are cosmetic (trails, flags, finish effects) plus one helper (the star
    /// radar), so a new player and a veteran race the same course on equal terms.
    /// </summary>
    public static class OB_Retention {
        public const string Universe = "obby";

        // ---------------------------------------------------------------- unlock ids (never rename a shipped id)
        public const string CloudTrailUnlock = "cloud_trail";
        public const string StarRadarUnlock = "star_radar";
        public const string GoldFlagsUnlock = "gold_flags";
        public const string SparkTrailUnlock = "spark_trail";
        public const string ConfettiFinishUnlock = "confetti_finish";
        public const string RainbowTrailUnlock = "rainbow_trail";
        public const string CometTrailUnlock = "comet_trail";
        public const string ChampionTrailUnlock = "champion_trail";

        public static readonly UnlockDefinition[] Unlocks = {
            new(2, CloudTrailUnlock, "Cloud Trail", "A soft cloud trail follows your robot round the course.", "directions_run"),
            new(4, StarRadarUnlock, "Star Radar", "Stars you have not collected shine a beam into the sky, so pro routes are easy to spot.", "star"),
            new(6, GoldFlagsUnlock, "Gold Flags", "Checkpoints you reach raise a gold flag.", "flying_flag"),
            new(8, SparkTrailUnlock, "Spark Trail", "Your trail throws sparks.", "bolt"),
            new(10, ConfettiFinishUnlock, "Finish Fireworks", "A confetti storm and a shockwave when you cross the finish.", "emoji_events"),
            new(13, RainbowTrailUnlock, "Rainbow Trail", "Your trail cycles through every colour of the course.", "favorite"),
            new(17, CometTrailUnlock, "Comet Trail", "A blazing comet trail behind you.", "local_fire_department"),
            new(22, ChampionTrailUnlock, "Champion's Gold", "A gold trail for the obby's best.", "laurels_trophy"),
        };

        // ---------------------------------------------------------------- stat keys (free-form, reported by the server)
        public const string CheckpointsStat = "checkpoints";
        public const string CoinsStat = "coins";
        public const string StarsStat = "stars";
        public const string FinishStat = "finish";
        public const string SilverStat = "silver_or_better";
        public const string GoldStat = "gold";
        /// <summary>Highest tier reached in one race, as 1 (Basic) to 6 (Extreme).</summary>
        public const string TierStat = "tier";
        public const string CleanTierStat = "clean_tier";
        public const string PerfectTierStat = "perfect_tier";
        public const string GravityStat = "gravity_stages";
        public const string LavaStat = "lava_escapes";
        public const string PodiumStat = "podium";

        /// <summary>The daily pool: one entry per stat key, so every day's three challenges ask for different things.</summary>
        public static readonly ChallengeDefinition[] Challenges = {
            new("checkpoints_30", CheckpointsStat, 30, 110, 25, "Reach {target} checkpoints"),
            new("coins_15", CoinsStat, 15, 110, 30, "Collect {target} coins"),
            new("stars_3", StarsStat, 3, 150, 40, "Collect {target} hidden stars"),
            new("finish_1", FinishStat, 1, 160, 40, "Finish a course"),
            new("silver_1", SilverStat, 1, 180, 45, "Finish with a silver medal or better"),
            new("gold_1", GoldStat, 1, 240, 70, "Win a gold medal"),
            new("tier_5", TierStat, 5, 140, 35, "Reach the Insane tier"),
            new("clean_tier_2", CleanTierStat, 2, 140, 35, "Clear {target} tiers without falling"),
            new("perfect_tier_1", PerfectTierStat, 1, 170, 45, "Clear a tier with all of its coins and stars"),
            new("gravity_6", GravityStat, 6, 120, 30, "Clear {target} gravity stages"),
            new("lava_3", LavaStat, 3, 130, 35, "Outrun the rising lava {target} times"),
            new("podium_1", PodiumStat, 1, 150, 40, "Finish in the top three of a race"),
        };

        public static RetentionRules Rules(RetentionTuning tuning) => new(Universe, tuning, Unlocks, Challenges);

        /// <summary>The level that unlocks <paramref name="unlockId"/> (1 for an empty or unknown id).</summary>
        public static int LevelOf(string unlockId) {
            if (string.IsNullOrEmpty(unlockId)) return 1;
            foreach (var unlock in Unlocks)
                if (unlock.id == unlockId) return unlock.level;
            return 1;
        }

        /// <summary>Whether a player of <paramref name="level"/> owns <paramref name="unlockId"/>.</summary>
        public static bool Owns(int level, string unlockId) => level >= LevelOf(unlockId);
    }
}
