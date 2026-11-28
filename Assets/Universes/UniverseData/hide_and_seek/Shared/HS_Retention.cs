using System;
using System.Collections.Generic;
using RyanAssets.Retention;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>
    /// Hide and Seek's level track and daily challenges for the shared retention kit: the abilities are
    /// the unlocks (one per side to start, the second keys in the first evening, the third keys on day
    /// two and three), then their upgrades, then perks that change how a round plays (a pair of decoys,
    /// more taunts) and two cosmetics. The track ends at the last unlock (<see cref="MaxLevel"/>), so no
    /// level is dead. The challenge pool asks for a different way of playing each day.
    /// </summary>
    public static class HS_Retention {
        /// <summary>Unlock ids. Never rename one that shipped (games check them by id).</summary>
        public static class UnlockIds {
            public const string Dash = "dash", Rush = "rush", Decoy = "decoy", Radar = "radar", Blend = "blend", Flashlight = "flashlight";
            public const string DashPlus = "dash_plus", RushPlus = "rush_plus", DecoyPlus = "decoy_plus", RadarPlus = "radar_plus",
                BlendPlus = "blend_plus", FlashlightPlus = "flashlight_plus", QuickRecovery = "quick_recovery";
            public const string DecoyPair = "decoy_pair", TauntMaster = "taunt_master", GhostTrail = "ghost_trail", ManorLegend = "manor_legend";
        }

        /// <summary>Stat keys the server reports to the kit (<c>Count</c> unless noted).</summary>
        public static class Stats {
            public const string Rounds = "rounds", Survived = "survived", Tags = "tags", CloseCalls = "close_calls", BraveTaunts = "brave_taunts",
                Abilities = "abilities", Wins = "wins", Mvp = "mvp", LastHider = "last_hider", HiddenSeconds = "hidden_seconds", FirstFind = "first_find",
                Bluffs = "bluffs", RoomsSearched = "rooms_searched", Pumpkins = "pumpkins";
            /// <summary>Reported with <c>Reach</c>: the player's points in one round.</summary>
            public const string RoundPoints = "round_points";

            public static readonly string[] All = { Rounds, Survived, Tags, CloseCalls, BraveTaunts, Abilities, Wins, Mvp, LastHider, HiddenSeconds, FirstFind, RoundPoints, Bluffs, RoomsSearched, Pumpkins };
        }

        /// <summary>
        /// The level track's unlocks, at most three levels apart up to the last one. With about eight rounds a
        /// day the second keys come on day one, Blend In early on day two and the Flashlight on day three
        /// (<c>HS_RetentionTests.PacingSpreadsTheUnlocksOverManyRounds</c> prints the day and round of each).
        /// </summary>
        public static readonly UnlockDefinition[] Unlocks = {
            new(1, UnlockIds.Dash, "Dash", "Hiders: key 1 for a burst of speed.", "sprint"),
            new(1, UnlockIds.Rush, "Rush", "Seekers: key 1 for a burst of speed.", "speed"),
            new(2, UnlockIds.Decoy, "Decoy", "Hiders: key 2 throws a noise maker that fakes a taunt and fools pings.", "campaign"),
            new(3, UnlockIds.Radar, "Radar", "Seekers: key 2 senses hiders around you.", "radio"),
            new(5, UnlockIds.DashPlus, "Dash+", "Longer, faster dashes on a shorter cooldown.", "sprint"),
            new(7, UnlockIds.RushPlus, "Rush+", "Longer, faster rushes on a shorter cooldown.", "speed"),
            new(8, UnlockIds.Blend, "Blend In", "Hiders: key 3 fades you out; seekers only see your shadow. Press 3 again to move.", "ghost"),
            new(10, UnlockIds.DecoyPlus, "Decoy+", "Decoys fly further and fool seekers for longer.", "campaign"),
            new(12, UnlockIds.Flashlight, "Flashlight", "Seekers: key 3 fires a beam that blinds, reveals and slows hiders.", "bolt"),
            new(14, UnlockIds.RadarPlus, "Radar+", "A much bigger radar sweep that recharges faster.", "radio"),
            new(16, UnlockIds.BlendPlus, "Blend+", "Stay blended longer and blend again sooner.", "ghost"),
            new(18, UnlockIds.FlashlightPlus, "Flashlight+", "A longer beam that keeps hiders revealed longer.", "bolt"),
            new(20, UnlockIds.QuickRecovery, "Quick Recovery", "Every ability recharges 20% faster.", "hourglass"),
            new(22, UnlockIds.DecoyPair, "Decoy Pair", "Each Decoy throws two noise makers, apart, to split the seekers.", "megaphone"),
            new(24, UnlockIds.TauntMaster, "Taunt Master", "Two more taunts every round.", "hearing"),
            new(26, UnlockIds.GhostTrail, "Ghost Trail", "Your Dash and Rush leave a glowing trail everyone sees.", "local_fire_department"),
            new(MaxLevel, UnlockIds.ManorLegend, "Manor Legend", "The title on your MVP lines: everyone hears about the Manor Legend.", "crowned_skull"),
        };

        /// <summary>The track's last level: the last unlock, so every level up to the cap brings something.</summary>
        public const int MaxLevel = 28;

        /// <summary>Taunts a round that Taunt Master adds to <see cref="HS_Rules.TauntsPerRound"/>.</summary>
        public const int TauntMasterExtra = 2;

        /// <summary>The title Manor Legend puts before a player's name on MVP lines.</summary>
        public const string LegendTitle = "Manor Legend";

        /// <summary>Daily challenge pool (three a day). None needs an ability a new player does not have.</summary>
        public static readonly ChallengeDefinition[] Challenges = {
            new("play_rounds_4", Stats.Rounds, 4, 90, 20, "Play {target} rounds"),
            new("survive_2", Stats.Survived, 2, 120, 30, "Survive {target} rounds as a hider") { shortText = "Survived x{target}" },
            new("tag_4", Stats.Tags, 4, 110, 25, "Tag {target} hiders"),
            new("close_calls_3", Stats.CloseCalls, 3, 110, 25, "Escape {target} close calls") { shortText = "Close calls x{target}" },
            new("brave_taunts_3", Stats.BraveTaunts, 3, 100, 20, "Make {target} brave taunts") { shortText = "Brave taunts x{target}" },
            new("abilities_10", Stats.Abilities, 10, 90, 20, "Use abilities {target} times") { shortText = "Abilities x{target}" },
            new("win_3", Stats.Wins, 3, 120, 30, "Win {target} rounds"),
            new("round_points_150", Stats.RoundPoints, 150, 120, 30, "Score {target} points in one round") { shortText = "{target}-point round" },
            new("mvp_1", Stats.Mvp, 1, 130, 35, "Be a round's MVP"),
            new("last_hider_1", Stats.LastHider, 1, 120, 30, "Be the last hider left") { shortText = "Last hider" },
            new("hidden_240", Stats.HiddenSeconds, 240, 100, 20, "Stay hidden for {target} seconds while seekers hunt") { shortText = "Hidden {target}s" },
            new("first_find_2", Stats.FirstFind, 2, 110, 25, "Find the first hider of a round {target} times") { shortText = "First finds x{target}" },
            new("bluffs_2", Stats.Bluffs, 2, 120, 30, "Fool seekers with {target} empty closets") { shortText = "Closet bluffs x{target}" },
            new("search_rooms_15", Stats.RoomsSearched, 15, 90, 20, "Search {target} rooms as a seeker") { shortText = "Rooms searched x{target}" },
            new("pumpkins_5", Stats.Pumpkins, 5, 110, 25, "Grab {target} glowing pumpkins as a hider") { shortText = "Pumpkins x{target}" },
        };

        static Dictionary<string, int> unlockLevels;

        /// <summary>
        /// The kit rules for this game (build once on the server; the client only reads <see cref="Unlocks"/>).
        /// The level cap is held at <see cref="MaxLevel"/>: levels past the last unlock would give nothing.
        /// </summary>
        public static RetentionRules Rules(RetentionTuning tuning) {
            RetentionTuning capped = (tuning ?? new RetentionTuning()).Clone();
            capped.levels.maxLevel = Math.Min(capped.levels.maxLevel, MaxLevel);
            return new RetentionRules(HS_SaveData.Universe, capped, Unlocks, Challenges);
        }

        /// <summary>The level that unlocks <paramref name="unlockId"/>, or 0 for an unknown id.</summary>
        public static int UnlockLevel(string unlockId) {
            if (unlockLevels == null) {
                var levels = new Dictionary<string, int>();
                foreach (UnlockDefinition unlock in Unlocks) levels[unlock.id] = unlock.level;
                unlockLevels = levels;
            }
            return unlockId != null && unlockLevels.TryGetValue(unlockId, out int level) ? level : 0;
        }
    }
}
