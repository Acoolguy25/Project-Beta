using System;
using RyanAssets.Progress;

namespace Universes.UniverseData.classic_horror.Server {
    /// <summary>What the horror case remembers about an investigator between sessions. Written only by the server.</summary>
    public sealed class CH_SaveData : ProgressData {
        public const string Universe = "classic_horror";
        public override int LatestVersion => 1;

        /// <summary>Chapters the player was still in the case for when they closed (two per full case).</summary>
        public int chaptersCompleted;
        /// <summary>Cases the player survived to the end of.</summary>
        public int casesCompleted;
        /// <summary>Evidence, diary pages and offerings the player secured.</summary>
        public int evidenceFound;

        /// <summary>Escaping closes the final chapter and the case.</summary>
        public void RecordCaseClosed() {
            chaptersCompleted++;
            casesCompleted++;
        }

        public override void Validate() {
            chaptersCompleted = Math.Max(0, chaptersCompleted);
            casesCompleted = Math.Max(0, casesCompleted);
            evidenceFound = Math.Max(0, evidenceFound);
        }
    }
}
