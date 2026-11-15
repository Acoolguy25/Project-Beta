using System;
using RyanAssets.Progress;

namespace Universes.UniverseData.tower_defense.Server {
    /// <summary>What tower defense remembers about a player between sessions. Written only by the server.</summary>
    public sealed class TD_SaveData : ProgressData {
        public const string Universe = "tower_defense";
        public override int LatestVersion => 1;

        /// <summary>Highest wave the player has started (fought in).</summary>
        public int bestWave;
        /// <summary>Enemies finished off by the player's towers.</summary>
        public long totalKills;
        public int gamesWon;

        public void RecordWave(int wave) => bestWave = Math.Max(bestWave, wave);

        public override void Validate() {
            bestWave = Math.Max(0, bestWave);
            totalKills = Math.Max(0, totalKills);
            gamesWon = Math.Max(0, gamesWon);
        }
    }
}
