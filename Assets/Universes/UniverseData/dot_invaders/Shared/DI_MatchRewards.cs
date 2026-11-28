using System;
using System.Collections.Generic;
using RyanAssets.Retention;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    /// <summary>What one player did in a finished match, as the rewards see it.</summary>
    public struct DI_MatchResult {
        public bool won;
        /// <summary>1-based place among <see cref="teamCount"/> teams.</summary>
        public int placement;
        public int teamCount;
        public int captures;
        public int eliminations;
        /// <summary>Bounties the player's troops claimed.</summary>
        public int bounties;
        /// <summary>The match took the player into a rank above any they held before.</summary>
        public bool newRank;
        public string rankName;
    }

    /// <summary>
    /// XP and gold for a finished match, paid through the retention kit's summary card. Serialized on the
    /// server runner; <see cref="Lines"/> is pure so the amounts can be tested.
    /// </summary>
    [Serializable]
    public sealed class DI_MatchRewards {
        [Tooltip("Paid for every match the player is still in at the end.")]
        [Min(0)] public int matchXp = 25;
        [Min(0)] public int victoryXp = 100;
        [Min(0)] public int victoryGold = 50;
        [Tooltip("For each team the player finished ahead of.")]
        [Min(0)] public int teamBeatenXp = 15;
        [Min(0)] public int captureXp = 3;
        [Tooltip("Most XP captures pay in one match.")]
        [Min(0)] public int captureXpCap = 60;
        [Tooltip("For each team whose last base the player took.")]
        [Min(0)] public int eliminationXp = 20;
        [Tooltip("For each bounty the player's troops claimed.")]
        [Min(0)] public int bountyXp = 25;
        [Min(0)] public int bountyGold = 5;
        [Tooltip("Reaching a rank the player never held before.")]
        [Min(0)] public int newRankXp = 50;
        [Min(0)] public int newRankGold = 25;

        public List<RewardLine> Lines(in DI_MatchResult result) {
            var lines = new List<RewardLine>();
            if (result.won)
                lines.Add(new RewardLine("Victory", victoryXp, victoryGold));
            int beaten = Mathf.Max(0, result.teamCount - result.placement);
            if (!result.won && beaten > 0)
                lines.Add(new RewardLine($"Finished {DI_Rating.Ordinal(result.placement)} of {result.teamCount}", beaten * teamBeatenXp));
            else if (result.won && beaten > 0)
                lines.Add(new RewardLine("Teams outlasted", beaten * teamBeatenXp, 0, beaten));
            lines.Add(new RewardLine("Match played", matchXp));
            if (result.captures > 0)
                lines.Add(new RewardLine("Bases captured", Mathf.Min(captureXpCap, result.captures * captureXp), 0, result.captures));
            if (result.eliminations > 0)
                lines.Add(new RewardLine("Teams eliminated", result.eliminations * eliminationXp, 0, result.eliminations));
            if (result.bounties > 0)
                lines.Add(new RewardLine("Bounties claimed", result.bounties * bountyXp, result.bounties * bountyGold, result.bounties));
            if (result.newRank)
                lines.Add(new RewardLine($"New rank: {result.rankName}", newRankXp, newRankGold));
            return lines;
        }

        /// <summary>The summary card's headline.</summary>
        public static string Title(in DI_MatchResult result, bool draw) =>
            result.won ? "Victory!" : draw && result.placement == 1 ? "Draw" :
            result.placement > 0 ? $"{DI_Rating.Ordinal(result.placement)} of {result.teamCount}" : "Match over";
    }
}
