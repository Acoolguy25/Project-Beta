#if UNITY_SERVER
using System;
using RyanAssets.Progress;

namespace Universes.UniverseData.dot_invaders {
    /// <summary>What Dot Invaders remembers about a player between sessions. Written only by the server.</summary>
    public sealed class DI_SaveData : ProgressData {
        public const string Universe = "dot_invaders";
        public override int LatestVersion => 1;

        /// <summary>Matches the player was still in when they ended.</summary>
        public int matches;
        public int wins;
        /// <summary>Bases the player's own troops captured, over all matches.</summary>
        public int basesCaptured;
        /// <summary>The most bases the player's team held at once in any match.</summary>
        public int mostBasesHeld;

        public void RecordMatch(bool won) {
            matches++;
            if (won) wins++;
        }

        public void RecordCaptures(int captures, int basesHeld) {
            basesCaptured += Math.Max(0, captures);
            mostBasesHeld = Math.Max(mostBasesHeld, basesHeld);
        }

        public override void Validate() {
            matches = Math.Max(0, matches);
            wins = Math.Clamp(wins, 0, matches);
            basesCaptured = Math.Max(0, basesCaptured);
            mostBasesHeld = Math.Max(0, mostBasesHeld);
        }
    }
}
#endif
