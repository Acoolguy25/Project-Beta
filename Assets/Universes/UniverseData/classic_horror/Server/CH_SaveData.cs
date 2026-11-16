using System;
using RyanAssets.Progress;

namespace Universes.UniverseData.classic_horror.Server {
    /// <summary>What the horror case remembers about an investigator between sessions. Written only by the server.</summary>
    public sealed class CH_SaveData : ProgressData {
        public const string Universe = "classic_horror";
        /// <summary>Endings indexed like <see cref="CH_Case"/>'s Ending* constants.</summary>
        const int EndingCount = 5;
        public override int LatestVersion => 1;

        /// <summary>Chapters the player was still in the case for when they closed (two per full case).</summary>
        public int chaptersCompleted;
        /// <summary>Cases the player survived to the end of.</summary>
        public int casesCompleted;
        /// <summary>Evidence, diary pages and offerings the player secured.</summary>
        public int evidenceFound;
        /// <summary>Priest's diary pages the player read themselves.</summary>
        public int diaryPagesRead;
        /// <summary>Lost memories the player recovered themselves.</summary>
        public int memoriesRecovered;
        /// <summary>Bit per ending (<see cref="CH_Case"/> Ending* index) the player has seen as a survivor.</summary>
        public int endingsSeen;
        /// <summary>Quickest escape in seconds; 0 until the first case is closed.</summary>
        public int fastestCaseSeconds;

        /// <summary>Escaping closes the final chapter and the case.</summary>
        public void RecordCaseClosed(int endingIndex, int caseSeconds) {
            chaptersCompleted++;
            casesCompleted++;
            if (endingIndex >= 0 && endingIndex < EndingCount) endingsSeen |= 1 << endingIndex;
            if (caseSeconds > 0 && (fastestCaseSeconds == 0 || caseSeconds < fastestCaseSeconds)) fastestCaseSeconds = caseSeconds;
        }

        public override void Validate() {
            chaptersCompleted = Math.Max(0, chaptersCompleted);
            casesCompleted = Math.Max(0, casesCompleted);
            evidenceFound = Math.Max(0, evidenceFound);
            diaryPagesRead = Math.Max(0, diaryPagesRead);
            memoriesRecovered = Math.Max(0, memoriesRecovered);
            endingsSeen &= (1 << EndingCount) - 1;
            fastestCaseSeconds = Math.Max(0, fastestCaseSeconds);
        }
    }
}
