using RyanAssets.Retention;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// War Valley's level track and daily challenge pool for the shared retention kit, the same on
    /// server and client. Every unlock changes how a round plays: a bigger opening, a new doctrine
    /// card, a free squad, more choice in the deck.
    /// </summary>
    public static class WV_Retention {
        // --- Unlock ids (saved progress matches on these: never rename) ------
        public const string SupplyCache = "supply_cache";
        public const string CardBountyHunters = "card_bounty_hunters";
        public const string DoctrineReroll = "doctrine_reroll";
        public const string OpeningOrders = "opening_orders";
        public const string VeteranSquad = "veteran_squad";
        public const string CardOvercharge = "card_turret_overcharge";
        public const string FlagEngineers = "flag_engineers";
        public const string FourthCard = "fourth_card";
        public const string CardFieldMedics = "card_field_medics";
        public const string EliteDoctrines = "elite_doctrines";

        // --- Stat keys counted by the server ---------------------------------
        public const string KillsStat = "kills";
        public const string WavesStat = "waves";
        public const string WaveStat = "wave";
        public const string WinsStat = "wins";
        public const string DoctrinesStat = "doctrines";
        public const string TroopsStat = "troops";
        public const string VehiclesStat = "vehicles";
        public const string StructuresStat = "structures";
        public const string BossStat = "boss";
        public const string VeteranWinStat = "veteran_win";
        public const string StarsStat = "stars";
        public const string EndlessStat = "endless";

        // --- What the unlocks give (referenced by the unlock text and the server) ---
        /// <summary>Extra starting funds from <see cref="SupplyCache"/>.</summary>
        public const int SupplyCacheFunds = 400;
        /// <summary>Free troops from <see cref="VeteranSquad"/>, and their kind.</summary>
        public const int VeteranSquadSize = 2;
        public const WV_TroopKind VeteranSquadKind = WV_TroopKind.Gunner;
        /// <summary>Share of the flag's health repaired after each held wave with <see cref="FlagEngineers"/>.</summary>
        public const float FlagEngineersRepair = 0.15f;
        /// <summary>Rerolls a round with <see cref="DoctrineReroll"/>.</summary>
        public const int RerollsPerRound = 1;

        public static readonly UnlockDefinition[] Unlocks = {
            new(2, SupplyCache, "Supply Cache", $"Start every round with {SupplyCacheFunds} extra funds.", "coins"),
            new(3, CardBountyHunters, "Bounty Hunters", "New doctrine card: funds for every enemy you defeat.", "star_medal"),
            new(4, DoctrineReroll, "Reroll", "Redraw one doctrine hand per round.", "refresh"),
            new(5, OpeningOrders, "Opening Orders", "Pick a doctrine before the first wave.", "military_tech"),
            new(7, VeteranSquad, "Veteran Squad", $"{VeteranSquadSize} free Gunners join you at the start of every round.", "rally_troops"),
            new(9, CardOvercharge, "Overcharge", "New doctrine card: turrets deal 30% more damage.", "bolt"),
            new(11, FlagEngineers, "Flag Engineers", "The flag repairs 15% after every wave you hold.", "flying_flag"),
            new(13, FourthCard, "War Council", "Doctrine hands deal 4 cards instead of 3.", "groups"),
            new(16, CardFieldMedics, "Field Medics", "New doctrine card: your troops heal over time.", "favorite"),
            new(20, EliteDoctrines, "Elite Doctrines", "Rare, powerful doctrine cards can be dealt.", "warlord"),
        };

        /// <summary>
        /// The daily pool. Retention try 2: every entry can be done on Recruit, the one tier every commander
        /// has, so no day deals a commander a challenge their campaign has not opened yet ("Win on Veteran",
        /// "Reach wave 22 in Endless" left the pool); Veteran and Endless goals are paid by the campaign
        /// itself (first wins, stars, records). The kit draws one pool for everyone, so this is the only gate.
        /// </summary>
        public static readonly ChallengeDefinition[] ChallengePool = {
            new("kills_60", KillsStat, 60, 120, 40, "Defeat {target} wave enemies") { shortText = "Enemies x{target}" },
            new("waves_10", WavesStat, 10, 120, 40, "Hold {target} waves"),
            new("wave_12", WaveStat, 12, 140, 50, "Reach wave {target} in one round") { shortText = "Reach wave {target}" },
            new("win_1", WinsStat, 1, 200, 80, "Win a round"),
            new("doctrines_5", DoctrinesStat, 5, 100, 30, "Pick {target} doctrines"),
            new("troops_15", TroopsStat, 15, 100, 30, "Train {target} troops"),
            new("vehicles_4", VehiclesStat, 4, 110, 35, "Build {target} vehicles"),
            new("structures_12", StructuresStat, 12, 100, 30, "Place {target} buildings"),
            new("boss_1", BossStat, 1, 150, 50, "Defeat a Warlord"),
            new("stars_3", StarsStat, 3, 160, 60, "Earn a {target}-star victory") { shortText = "{target}-star win" },
        };

        /// <summary>The level the track ends at: the last unlock's, so no level at the top is empty.</summary>
        public const int MaxLevel = 20;

        /// <summary>
        /// War Valley's level curve: about 23,600 XP to level 20. A round is long (20 waves, about 28
        /// minutes) and pays 130 to 1,900 XP, so on two rounds a day (with the daily challenges) the
        /// first unlocks come in the first rounds, the Veteran Squad on day 2, War Council on day 4
        /// and Elite Doctrines on about day 10 (see WV_PacingTests for the run-by-run numbers).
        /// </summary>
        public static RetentionTuning DefaultTuning() {
            var tuning = new RetentionTuning();
            tuning.levels.maxLevel = MaxLevel;
            tuning.levels.firstLevelXp = 120;
            tuning.levels.extraXpPerLevel = 40;
            tuning.levels.growth = 1.08f;
            return tuning;
        }

        public static RetentionRules Rules(RetentionTuning tuning) =>
            new(WV_SaveUniverse, tuning, Unlocks, ChallengePool);

        /// <summary>The save document's universe id (<c>WV_SaveData.Universe</c>), which also seeds the daily pick.</summary>
        public const string WV_SaveUniverse = "war_valley";

        /// <summary>The level that grants <paramref name="unlockId"/>, or 0 for an unknown id.</summary>
        public static int LevelOf(string unlockId) {
            foreach (UnlockDefinition unlock in Unlocks) {
                if (unlock.id == unlockId)
                    return unlock.level;
            }
            return 0;
        }
    }
}
