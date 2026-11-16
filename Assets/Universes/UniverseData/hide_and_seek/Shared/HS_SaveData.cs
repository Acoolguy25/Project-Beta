using System;
using RyanAssets.Progress;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>
    /// What hide and seek remembers about a player between sessions. Written only by the server; it lives
    /// in Shared so EditMode tests (which run without UNITY_SERVER) can check it.
    /// </summary>
    public sealed class HS_SaveData : ProgressData {
        public const string Universe = "hide_and_seek";
        public override int LatestVersion => 1;

        public int roundsPlayed;
        /// <summary>Rounds the player ended as a hider nobody found.</summary>
        public int hidesSurvived;
        /// <summary>Hiders the player found as a seeker.</summary>
        public int tags;
        /// <summary>Taunts the player made as a hider, and how many of them were brave (a seeker close by).</summary>
        public int taunts, braveTaunts;

        public void RecordRound(bool survivedHiding) {
            roundsPlayed++;
            if (survivedHiding) hidesSurvived++;
        }

        public void RecordTaunt(bool brave) {
            taunts++;
            if (brave) braveTaunts++;
        }

        public override void Validate() {
            roundsPlayed = Math.Max(0, roundsPlayed);
            hidesSurvived = Math.Max(0, hidesSurvived);
            tags = Math.Max(0, tags);
            taunts = Math.Max(0, taunts);
            braveTaunts = Math.Clamp(braveTaunts, 0, taunts);
        }
    }
}
