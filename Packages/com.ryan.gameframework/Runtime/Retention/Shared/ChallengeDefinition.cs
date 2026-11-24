using System;

namespace RyanAssets.Retention {
    /// <summary>
    /// One entry of a game's daily challenge pool. Every UTC day each game shows a few of them
    /// (<see cref="DailyChallenges.Pick"/>); progress comes from the game calling
    /// <c>RetentionService.Count(conn, statKey, amount)</c> (running totals: kills, wins, coins) or
    /// <c>Reach(conn, statKey, value)</c> (bests within one go: wave reached, combo length).
    /// </summary>
    /// <example><code>new ChallengeDefinition("kills_200", "kills", 200, 150, 40, "Destroy {target} enemies")</code></example>
    [Serializable]
    public sealed class ChallengeDefinition {
        /// <summary>Placeholder in <see cref="text"/> replaced by <see cref="target"/>.</summary>
        public const string TargetToken = "{target}";

        /// <summary>Stable id saved with the player's progress; renaming one resets today's progress on it.</summary>
        public string id;
        /// <summary>The statistic the game reports ("kills", "wins", "wave").</summary>
        public string statKey;
        /// <summary>Amount (Count) or value (Reach) that completes it.</summary>
        public int target;
        /// <summary>Game XP paid once on completion (account XP is paid too, see <see cref="RetentionTuning.accountXpScale"/>).</summary>
        public int xpReward;
        /// <summary>Account gold paid once on completion.</summary>
        public int goldReward;
        /// <summary>What the player reads, e.g. "Win {target} rounds".</summary>
        public string text;

        public ChallengeDefinition() { }

        public ChallengeDefinition(string id, string statKey, int target, int xpReward, int goldReward, string text) {
            this.id = id;
            this.statKey = statKey;
            this.target = target;
            this.xpReward = xpReward;
            this.goldReward = goldReward;
            this.text = text;
        }

        /// <summary><see cref="text"/> with <see cref="TargetToken"/> filled in.</summary>
        public string Describe() => (text ?? id ?? string.Empty).Replace(TargetToken, target.ToString());

        public override string ToString() => $"{id} ({statKey} {target})";
    }
}
