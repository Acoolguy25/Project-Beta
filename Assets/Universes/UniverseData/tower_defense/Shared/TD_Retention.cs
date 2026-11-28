using RyanAssets.Retention;

namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// Tower Defense's side of the shared retention kit: the level unlock table (towers, abilities, a
    /// mode, perks for the towers a player builds, a cosmetic), the daily challenge pool and the stat
    /// keys the server reports. Shared, so the client draws locks and perks from the same table the
    /// server enforces.
    /// </summary>
    public static class TD_Retention {
        public const string Universe = "tower_defense";

        // ---------------------------------------------------------------- unlock ids (never rename a shipped id)
        public const string WarChestUnlock = "war_chest";
        public const string TeslaUnlock = "tesla";
        public const string AirstrikeUnlock = "airstrike";
        public const string EndlessUnlock = "endless";
        public const string MortarUnlock = "mortar";
        public const string SalvageUnlock = "salvage";
        public const string VeteranUnlock = "veteran";
        public const string EmpUnlock = "emp";
        public const string EngineersUnlock = "engineers";
        public const string GoldUnlock = "gold_trim";
        public const string SupplyUnlock = "supply_drop";
        public const string ReloadUnlock = "rapid_reload";
        public const string EliteUnlock = "elite_towers";

        public const int EndlessLevel = 5;
        /// <summary>The level that unlocks elite specialisations (<see cref="EliteUnlock"/>).</summary>
        public const int EliteLevel = 8;

        /// <summary>Team cash each player with War Chest adds at the start of a game.</summary>
        public const int WarChestCash = 60;
        /// <summary>Sale value of towers built by a player with Salvage Crew (instead of <see cref="TD_Rules.SellRefundRate"/>).</summary>
        public const float SalvageRefundRate = 0.85f;
        /// <summary>Damage multiplier of towers built by a player with Veteran Crews.</summary>
        public const float VeteranDamage = 1.1f;
        /// <summary>Upgrade discount for a player with Field Engineers.</summary>
        public const float EngineersDiscount = 0.15f;
        /// <summary>Team cash each player with Supply Drop adds at the start of a game, on top of War Chest.</summary>
        public const int SupplyCash = 40;
        /// <summary>Extra fire rate of towers built by a player with Rapid Reload.</summary>
        public const float ReloadBonus = 0.05f;

        public static readonly UnlockDefinition[] Unlocks = {
            new(2, WarChestUnlock, "War Chest", $"+${WarChestCash} team cash at the start of every game you play.", "paid"),
            new(3, TeslaUnlock, "Tesla Coil", "New tower: lightning that chains through swarms and breaks shields.", "bolt"),
            new(4, AirstrikeUnlock, "Airstrike", "Press F in a wave: a jet bombs the enemies closest to the base.", "jet_fighter"),
            new(EndlessLevel, EndlessUnlock, "Endless Mode", "Vote for Endless: the waves never stop. Set a record.", "hourglass_bottom"),
            new(6, MortarUnlock, "Artillery", "New tower: huge range and big blasts on the ground army.", "my_location"),
            new(7, SalvageUnlock, "Salvage Crew", "Your towers sell back for 85% instead of 70%.", "refresh"),
            new(EliteLevel, EliteUnlock, "Tower Specialists", "Upgrade a level 3 tower once more into one of two elite forms.", "emoji_events"),
            new(9, VeteranUnlock, "Veteran Crews", $"Towers you build deal 10% more damage: enough for level 3 Gunners to break shields.", "military_tech"),
            new(11, EmpUnlock, "EMP Blast", "Press V in a wave: stun every enemy and strip all shields.", "hub"),
            new(13, EngineersUnlock, "Field Engineers", "Your upgrades cost 15% less.", "trending_up"),
            new(16, GoldUnlock, "Gold Trim", "Towers you build glow gold for everyone to see.", "star_medal"),
            new(20, SupplyUnlock, "Supply Drop", $"+${SupplyCash} more team cash at the start of every game, on top of War Chest.", "paid"),
            new(25, ReloadUnlock, "Rapid Reload", "Towers you build fire 5% faster.", "speed"),
        };

        // ---------------------------------------------------------------- stat keys (free-form, reported by the server)
        public const string KillsStat = "kills";
        public const string BossStat = "boss";
        public const string WaveStat = "wave";
        public const string WinStat = "win";
        public const string BuildsStat = "builds";
        public const string MaxedStat = "maxed";
        public const string CleanWaveStat = "clean_wave";
        public const string ShieldsStat = "shields";
        public const string DronesStat = "drones";
        public const string EarlyStat = "early";
        public const string MedicsStat = "medics";
        public const string StarsStat = "stars";
        public const string HardWinStat = "hard_win";

        /// <summary>The daily pool: one entry per stat key, so every day's three challenges ask for different things.</summary>
        public static readonly ChallengeDefinition[] Challenges = {
            new("kills_250", KillsStat, 250, 120, 30, "Destroy {target} enemies"),
            new("boss_2", BossStat, 2, 150, 40, "Destroy {target} Heavy Tanks"),
            new("wave_15", WaveStat, 15, 140, 35, "Reach wave {target} in one game"),
            new("win_1", WinStat, 1, 200, 60, "Win a game"),
            new("builds_20", BuildsStat, 20, 100, 25, "Build {target} towers"),
            new("maxed_4", MaxedStat, 4, 120, 30, "Upgrade {target} towers to level 3"),
            new("clean_8", CleanWaveStat, 8, 130, 35, "Hold {target} waves without a leak"),
            new("shields_12", ShieldsStat, 12, 120, 30, "Break {target} enemy shields"),
            new("drones_60", DronesStat, 60, 110, 30, "Shoot down {target} drones"),
            new("early_5", EarlyStat, 5, 100, 25, "Call {target} waves early"),
            new("medics_8", MedicsStat, 8, 110, 30, "Destroy {target} Medics"),
            new("stars_3", StarsStat, 3, 180, 50, "Win a game with {target} stars"),
            new("hard_win_1", HardWinStat, 1, 260, 80, "Win a game on Hard"),
        };

        public static RetentionRules Rules(RetentionTuning tuning) => new(Universe, tuning, Unlocks, Challenges);

        /// <summary>The level that unlocks <paramref name="unlockId"/> (1 for an empty id or an unknown one).</summary>
        public static int LevelOf(string unlockId) {
            if (string.IsNullOrEmpty(unlockId)) return 1;
            foreach (var unlock in Unlocks)
                if (unlock.id == unlockId) return unlock.level;
            return 1;
        }

        /// <summary>Whether a player of <paramref name="level"/> owns <paramref name="unlockId"/> (an empty id is always owned).</summary>
        public static bool Owns(int level, string unlockId) => level >= LevelOf(unlockId);

        public static bool CanBuild(int level, TD_TowerKind kind) => Owns(level, TD_Rules.Tower(kind).unlockId);

        /// <summary>The perks a player of <paramref name="level"/> gives the towers they build (and the upgrades they buy).</summary>
        public static TD_TowerPerks PerksFor(int level) => PerksFor(id => Owns(level, id));

        /// <summary>The perks from an ownership test (the server asks the kit, which knows the tuned curve).</summary>
        public static TD_TowerPerks PerksFor(System.Func<string, bool> owns) => new() {
            damageScale = owns(VeteranUnlock) ? VeteranDamage : 1f,
            refundRate = owns(SalvageUnlock) ? SalvageRefundRate : TD_Rules.SellRefundRate,
            upgradeDiscount = owns(EngineersUnlock) ? EngineersDiscount : 0f,
            gold = owns(GoldUnlock),
            fireRateBonus = owns(ReloadUnlock) ? ReloadBonus : 0f,
        };

        /// <summary>Team cash a player adds at the start of a game (War Chest, then Supply Drop on top).</summary>
        public static int StartingCash(System.Func<string, bool> owns) => (owns(WarChestUnlock) ? WarChestCash : 0) + (owns(SupplyUnlock) ? SupplyCash : 0);

        /// <summary>The unlock (if any) <paramref name="id"/> names, for its title and level.</summary>
        public static UnlockDefinition Find(string id) {
            foreach (var unlock in Unlocks)
                if (unlock.id == id) return unlock;
            return null;
        }
    }

    /// <summary>What a player's unlocks add to a tower they build (or an upgrade they buy).</summary>
    public struct TD_TowerPerks {
        public float damageScale;
        public float refundRate;
        public float upgradeDiscount;
        /// <summary>Cosmetic gold trim.</summary>
        public bool gold;
        /// <summary>Extra fire rate (0 = none; Rapid Reload).</summary>
        public float fireRateBonus;

        public static TD_TowerPerks None => new() { damageScale = 1f, refundRate = TD_Rules.SellRefundRate };
    }
}
