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
        /// <summary>Rounds the player was in at the end, won or lost.</summary>
        public int roundsPlayed;
        /// <summary>Most waves held in a single round.</summary>
        public int mostWavesHeld;
        /// <summary>Wave enemies defeated across every round the player finished.</summary>
        public long enemiesDefeated;

        public void RecordWave(int wave) => bestWave = Math.Max(bestWave, wave);

        /// <summary>Adds one finished round: how many waves it held and how many enemies fell in it.</summary>
        public void RecordRound(int wavesHeld, int defeated) {
            roundsPlayed++;
            mostWavesHeld = Math.Max(mostWavesHeld, Math.Max(0, wavesHeld));
            enemiesDefeated += Math.Max(0, defeated);
        }

        public override void Validate() {
            bestWave = Math.Max(0, bestWave);
            victories = Math.Max(0, victories);
            roundsPlayed = Math.Max(0, roundsPlayed);
            mostWavesHeld = Math.Max(0, mostWavesHeld);
            enemiesDefeated = Math.Max(0L, enemiesDefeated);
        }
    }
}
