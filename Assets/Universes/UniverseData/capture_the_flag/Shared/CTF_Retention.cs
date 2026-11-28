using System;
using RyanAssets.Retention;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>
    /// Flag Wars on the shared retention kit: the level unlock table (guns and perks, see
    /// <see cref="CTF_Loadouts"/>), the daily challenge pool and the stat keys the server reports.
    /// The same on server and client; the server builds the rules with its tuning.
    /// </summary>
    public static class CTF_Retention {
        public const string Universe = "capture_the_flag";

        // ---- unlock ids (never rename a shipped id)
        public const string UnlockSecondWind = "perk_second_wind";
        public const string UnlockBurstRifle = "weapon_burst_rifle";
        public const string UnlockFleetFoot = "perk_fleet_foot";
        public const string UnlockScattergun = "weapon_scattergun";
        public const string UnlockFlagRunner = "perk_flag_runner";
        public const string UnlockMarksman = "weapon_marksman";
        public const string UnlockSteadyAim = "perk_steady_aim";
        public const string UnlockAdrenaline = "perk_adrenaline";
        public const string UnlockGuardian = "perk_guardian";
        public const string UnlockFieldMedic = "perk_field_medic";
        public const string UnlockFlagLegend = "title_flag_legend";
        public const string UnlockFlagTrail = "cosmetic_flag_trail";
        public const string UnlockGoldTracers = "cosmetic_gold_tracers";

        /// <summary>The title <see cref="UnlockFlagLegend"/> puts before a player's name in the feeds and on the MVP line.</summary>
        public const string LegendTitle = "Flag Legend";

        /// <summary>
        /// The track's last level: the last unlock, so every level up to the cap brings something new
        /// (<see cref="Rules"/> holds the kit's level cap here). Try 2 moved it from 23 to 30 and added two
        /// late unlocks others see (try 1: the whole track was over in three to five days).
        /// </summary>
        public const int MaxLevel = 30;

        /// <summary>
        /// Flag Wars' level curve: level 2 costs 160 XP and each level 55 more than the last, so at six
        /// matches a day the last gun lands on day 3 and the cap on day 11 to 13 (see the pacing test,
        /// which models the XP measured in try 1's natural matches).
        /// </summary>
        public const int FirstLevelXp = 160, ExtraXpPerLevel = 55;

        /// <summary>Eliminations with one gun that master it: a star before your name in the kill feed while you use it.</summary>
        public const int MasteryKills = 50;

        // ---- stat keys reported to Count / Reach
        public const string StatKills = "kills";
        public const string StatCaptures = "captures";
        public const string StatReturns = "returns";
        public const string StatWins = "wins";
        public const string StatCarrierKills = "carrier_kills";
        public const string StatMedals = "medals";
        public const string StatKillStreak = "kill_streak";
        public const string StatPowerUps = "powerups";
        public const string StatMatches = "matches";
        public const string StatFlagTakes = "flag_takes";
        public const string StatMultiKills = "multikills";
        public const string StatDefends = "defends";

        /// <summary>
        /// Every level unlock, in level order, at most three levels apart up to the cap: a gun or a perk
        /// every two or three levels, the late perks change how a life plays (speed after a kill, long-reach
        /// returns, healing from flag play), and the cap gives a title everyone sees.
        /// <c>CTF_PacingTests</c> prints the match and day each one lands on.
        /// </summary>
        public static readonly UnlockDefinition[] Unlocks = {
            new(2, UnlockSecondWind, "Second Wind", "Perk: every elimination heals you. Pick perks with H.", "favorite"),
            new(3, UnlockBurstRifle, "Burst Rifle", "Three-round bursts, long and tight. Pick guns with G.", "my_location"),
            new(5, UnlockFleetFoot, "Fleet Foot", "Perk: run faster everywhere.", "sprint"),
            new(7, UnlockScattergun, "Scattergun", "Six pellets that delete anyone up close.", "local_fire_department"),
            new(10, UnlockFlagRunner, "Flag Runner", "Perk: carry the flag at full speed.", "run"),
            new(13, UnlockMarksman, "Marksman Rifle", "Two dead-on shots drop anyone, across the arena.", "crosshair"),
            new(15, UnlockSteadyAim, "Steady Aim", "Perk: tighter spread on every gun.", "visibility"),
            new(17, UnlockAdrenaline, "Adrenaline", "Perk: every elimination gives you a burst of speed.", "bolt"),
            new(19, UnlockGuardian, "Guardian", "Perk: send your dropped flag home from three times as far.", "shield"),
            new(21, UnlockFieldMedic, "Field Medic", "Perk: flag grabs, returns and captures heal you to full.", "flag"),
            new(24, UnlockFlagTrail, "Flag Trail", "A flag you carry leaves a blazing trail everyone sees.", "speed"),
            new(27, UnlockGoldTracers, "Gold Tracers", "Every gun you fire draws gold tracers, for everyone.", "star"),
            new(MaxLevel, UnlockFlagLegend, LegendTitle, "The title before your name in every feed and on the MVP line.", "military_tech"),
        };

        /// <summary>
        /// The daily pool: three a day, no two on the same stat. Sized so the three together pay about
        /// a level at mid levels (a level is 300 to 500 XP there).
        /// </summary>
        public static readonly ChallengeDefinition[] Challenges = {
            new("kills_12", StatKills, 12, 120, 30, "Eliminate {target} enemies"),
            new("captures_2", StatCaptures, 2, 160, 40, "Capture {target} flags"),
            new("returns_3", StatReturns, 3, 110, 30, "Return your flag {target} times") { shortText = "Flag returns x{target}" },
            new("wins_2", StatWins, 2, 170, 45, "Win {target} matches"),
            new("carrier_kills_3", StatCarrierKills, 3, 140, 35, "Take down {target} flag carriers") { shortText = "Carrier kills x{target}" },
            new("medals_8", StatMedals, 8, 120, 30, "Earn {target} medals"),
            new("streak_4", StatKillStreak, 4, 150, 40, "Get a {target}-kill streak"),
            new("powerups_4", StatPowerUps, 4, 90, 25, "Grab {target} power-ups"),
            new("matches_3", StatMatches, 3, 100, 25, "Finish {target} matches"),
            new("flag_takes_4", StatFlagTakes, 4, 110, 30, "Take the enemy flag {target} times") { shortText = "Flag grabs x{target}" },
            new("multikills_2", StatMultiKills, 2, 140, 35, "Score {target} multi-kills"),
            new("defends_4", StatDefends, 4, 120, 30, "Defend your flag {target} times") { shortText = "Defends x{target}" },
        };

        /// <summary>The kit's tuning with Flag Wars' curve (<see cref="FirstLevelXp"/>, <see cref="ExtraXpPerLevel"/>, capped at <see cref="MaxLevel"/>).</summary>
        public static RetentionTuning DefaultTuning() {
            var tuning = new RetentionTuning();
            tuning.levels.maxLevel = MaxLevel;
            tuning.levels.firstLevelXp = FirstLevelXp;
            tuning.levels.extraXpPerLevel = ExtraXpPerLevel;
            return tuning;
        }

        /// <summary>The level that unlocks <paramref name="unlockId"/>, or 0 when no unlock has that id.</summary>
        public static int LevelOf(string unlockId) {
            foreach (var unlock in Unlocks) if (unlock.id == unlockId) return unlock.level;
            return 0;
        }

        /// <summary>
        /// The first not-yet-owned entry of a gun or perk table and the level that unlocks it, given a
        /// mask of owned entries and each entry's unlock id; false when everything is owned.
        /// </summary>
        public static bool NextLocked(byte owned, string[] unlockIds, out int index, out int level) {
            index = -1;
            level = int.MaxValue;
            for (int i = 0; unlockIds != null && i < unlockIds.Length; i++) {
                if (CTF_Loadouts.Has(owned, i) || string.IsNullOrEmpty(unlockIds[i])) continue;
                int at = LevelOf(unlockIds[i]);
                if (at <= 0 || at >= level) continue;
                level = at;
                index = i;
            }
            if (index < 0) level = 0;
            return index >= 0;
        }

        /// <summary>
        /// Flag Wars' retention rules with the server's <paramref name="tuning"/> (defaults when null). The
        /// level cap is held at <see cref="MaxLevel"/>: levels past the last unlock would bring nothing.
        /// </summary>
        public static RetentionRules Rules(RetentionTuning tuning = null) {
            RetentionTuning capped = (tuning ?? DefaultTuning()).Clone();
            capped.levels.maxLevel = Math.Min(capped.levels.maxLevel, MaxLevel);
            return new RetentionRules(Universe, capped, Unlocks, Challenges);
        }
    }
}
