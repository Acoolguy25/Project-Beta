using System.Collections.Generic;
using RyanAssets.Retention;

namespace Universes.UniverseData.dot_invaders {
    /// <summary>
    /// Dot Invaders' retention rules (shared kit): the level track's unlocks (every commander perk and
    /// the second perk slot) and the daily challenge pool. The same on server and client.
    /// </summary>
    public static class DI_Retention {
        public const string Universe = "dot_invaders";

        /// <summary>Statistics the server reports to the kit (<c>Count</c> unless noted).</summary>
        public static class Stats {
            public const string Captures = "captures";
            /// <summary>Captures of a turret, super, speed, factory or fortress base.</summary>
            public const string SpecialCaptures = "special_captures";
            public const string FactoryCaptures = "factory_captures";
            public const string FortressCaptures = "fortress_captures";
            /// <summary>Reach: the most bases held at once in a match.</summary>
            public const string MostHeld = "most_held";
            public const string Wins = "wins";
            public const string Matches = "matches";
            /// <summary>Matches finished in the top two.</summary>
            public const string Podiums = "podiums";
            /// <summary>Teams whose last base the player took.</summary>
            public const string Eliminations = "eliminations";
            public const string TroopsSent = "troops_sent";
            /// <summary>Enemy troops the player's troops destroyed, on the road or in a garrison.</summary>
            public const string TroopsDefeated = "troops_defeated";
        }

        /// <summary>The unlock table: one entry per perk at its level, plus the second slot.</summary>
        public static List<UnlockDefinition> Unlocks() {
            var unlocks = new List<UnlockDefinition>();
            foreach (DI_PerkDefinition perk in DI_Perks.All)
                unlocks.Add(new UnlockDefinition(perk.UnlockLevel, perk.Id, perk.Title, perk.Description, perk.Icon));
            unlocks.Add(new UnlockDefinition(DI_Perks.SecondSlotLevel, DI_Perks.SecondSlot, "Second Perk Slot",
                "Equip two commander perks at once.", "star_medal"));
            return unlocks;
        }

        /// <summary>The daily challenge pool (the kit picks three a day, never two on the same stat).</summary>
        public static ChallengeDefinition[] ChallengePool() => new[] {
            new ChallengeDefinition("captures_30", Stats.Captures, 30, 90, 20, "Capture {target} bases"),
            new ChallengeDefinition("captures_75", Stats.Captures, 75, 170, 40, "Capture {target} bases"),
            new ChallengeDefinition("specials_6", Stats.SpecialCaptures, 6, 110, 25, "Capture {target} special bases"),
            new ChallengeDefinition("factories_3", Stats.FactoryCaptures, 3, 100, 20, "Capture {target} factories"),
            new ChallengeDefinition("fortresses_2", Stats.FortressCaptures, 2, 110, 25, "Storm {target} fortresses"),
            new ChallengeDefinition("held_12", Stats.MostHeld, 12, 120, 25, "Hold {target} bases at once"),
            new ChallengeDefinition("win_1", Stats.Wins, 1, 150, 40, "Win {target} match"),
            new ChallengeDefinition("matches_3", Stats.Matches, 3, 80, 20, "Finish {target} matches"),
            new ChallengeDefinition("podium_2", Stats.Podiums, 2, 120, 25, "Finish in the top two {target} times"),
            new ChallengeDefinition("eliminate_2", Stats.Eliminations, 2, 130, 30, "Eliminate {target} teams"),
            new ChallengeDefinition("sent_600", Stats.TroopsSent, 600, 80, 15, "Send {target} troops into battle"),
            new ChallengeDefinition("defeated_400", Stats.TroopsDefeated, 400, 100, 20, "Defeat {target} enemy troops"),
        };

        /// <summary>The rules for <paramref name="tuning"/> (the runner's serialized kit tuning; null = kit defaults).</summary>
        public static RetentionRules Rules(RetentionTuning tuning) =>
            new(Universe, tuning ?? new RetentionTuning(), Unlocks(), ChallengePool());
    }
}
