using System;
using RyanAssets.Progress;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>
    /// What Flag Wars remembers about a player between sessions. Written only by the server. New
    /// counters were added without a version bump: missing fields load as 0.
    /// </summary>
    public sealed class CTF_SaveData : ProgressData {
        public const string Universe = "capture_the_flag";
        public override int LatestVersion => 1;

        public int captures;
        public int kills;
        /// <summary>Games the player's team won.</summary>
        public int wins;
        /// <summary>Own dropped flags sent home by touching them.</summary>
        public int returns;
        /// <summary>Deaths during play (not in the lobby or results).</summary>
        public int deaths;
        /// <summary>Matches the player was on a team for when the results came.</summary>
        public int matchesPlayed;
        /// <summary>Health and speed power-ups taken.</summary>
        public int powerUps;

        public override void Validate() {
            captures = Math.Max(0, captures);
            kills = Math.Max(0, kills);
            wins = Math.Max(0, wins);
            returns = Math.Max(0, returns);
            deaths = Math.Max(0, deaths);
            matchesPlayed = Math.Max(0, matchesPlayed);
            powerUps = Math.Max(0, powerUps);
        }
    }
}
