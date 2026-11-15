using System;
using RyanAssets.Progress;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>What War Valley remembers about a commander between sessions. Written only by the server.</summary>
    public sealed class WV_SaveData : ProgressData {
        public const string Universe = "war_valley";
        public override int LatestVersion => 1;

        /// <summary>Highest wave the player has fought in.</summary>
        public int bestWave;
        /// <summary>Rounds won: every wave held with the flag standing.</summary>
        public int victories;

        public void RecordWave(int wave) => bestWave = Math.Max(bestWave, wave);

        public override void Validate() {
            bestWave = Math.Max(0, bestWave);
            victories = Math.Max(0, victories);
        }
    }
}
