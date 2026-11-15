using System;
using RyanAssets.Progress;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>What Flag Wars remembers about a player between sessions. Written only by the server.</summary>
    public sealed class CTF_SaveData : ProgressData {
        public const string Universe = "capture_the_flag";
        public override int LatestVersion => 1;

        public int captures;
        public int kills;
        /// <summary>Games the player's team won.</summary>
        public int wins;

        public override void Validate() {
            captures = Math.Max(0, captures);
            kills = Math.Max(0, kills);
            wins = Math.Max(0, wins);
        }
    }
}
