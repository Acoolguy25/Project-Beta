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
        /// <summary>Games the player was in when they ended (won or lost).</summary>
        public int gamesPlayed;
        public int towersBuilt;
        public int towersUpgraded;
        /// <summary>Heavy Tank bosses finished off by the player's towers.</summary>
        public int bossesDestroyed;

        /// <summary>Records <paramref name="wave"/> as fought in; true when it beats the previous best.</summary>
        public bool RecordWave(int wave) {
            if (wave <= bestWave) return false;
            bestWave = wave;
            return true;
        }

        public override void Validate() {
            bestWave = Math.Max(0, bestWave);
            totalKills = Math.Max(0, totalKills);
            gamesWon = Math.Max(0, gamesWon);
            gamesPlayed = Math.Max(gamesWon, gamesPlayed);
            towersBuilt = Math.Max(0, towersBuilt);
            towersUpgraded = Math.Max(0, towersUpgraded);
            bossesDestroyed = Math.Max(0, bossesDestroyed);
        }
    }
}
