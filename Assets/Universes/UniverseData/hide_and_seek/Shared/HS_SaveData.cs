using System;
using RyanAssets.Progress;
using RyanAssets.Retention;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>
    /// What hide and seek remembers about a player between sessions. Written only by the server; it lives
    /// in Shared so EditMode tests (which run without UNITY_SERVER) can check it.
    /// </summary>
    public sealed class HS_SaveData : ProgressData {
        public const string Universe = "hide_and_seek";
        // New fields below all have initializers and keep their meaning, so version 1 documents load as they are.
        public override int LatestVersion => 1;

        public int roundsPlayed;
        /// <summary>Rounds the player ended as a hider nobody found.</summary>
        public int hidesSurvived;
        /// <summary>Hiders the player found as a seeker.</summary>
        public int tags;
        /// <summary>Taunts the player made as a hider, and how many of them were brave (a seeker close by).</summary>
        public int taunts, braveTaunts;

        /// <summary>The shared retention kit's block: game level, daily challenges, play streak.</summary>
        public RetentionSave retention = new();
        /// <summary>Rounds won (a hider nobody found, or a starting seeker when everyone was found).</summary>
        public int roundsWon;
        /// <summary>Consecutive wins right now, and the longest run ever.</summary>
        public int winStreak, bestWinStreak;
        /// <summary>Times the player had the most points in a round.</summary>
        public int mvps;
        /// <summary>Most points in a single round.</summary>
        public int bestRoundPoints;
        public int closeCalls;
        public int abilitiesUsed;
        /// <summary>Rounds the player left before they ended (each one is a loss).</summary>
        public int roundsLeft;
        /// <summary>Empty closets this player shut that a seeker then opened (bluffs that worked).</summary>
        public int bluffs;
        /// <summary>Rooms and yards this player searched as a seeker (first visit to each in a round).</summary>
        public int roomsSearched;
        /// <summary>Glowing pumpkins this player grabbed as a hider, and snuffed out as a seeker.</summary>
        public int pumpkins, pumpkinsSnuffed;

        public void RecordRound(bool survivedHiding) {
            roundsPlayed++;
            if (survivedHiding) hidesSurvived++;
        }

        /// <summary>
        /// Records a finished round's result and points. Returns true when <paramref name="points"/> beat the
        /// player's best round (a first scoring round counts).
        /// </summary>
        public bool RecordResult(bool survivedHiding, bool won, int points, bool mvp) {
            RecordRound(survivedHiding);
            if (won) {
                roundsWon++;
                winStreak++;
                bestWinStreak = Math.Max(bestWinStreak, winStreak);
            } else winStreak = 0;
            if (mvp) mvps++;
            if (points <= bestRoundPoints) return false;
            bestRoundPoints = points;
            return true;
        }

        /// <summary>The player left a round they were dealt into before it ended: it counts as a loss, so the win streak ends.</summary>
        public void RecordLeft() {
            roundsLeft++;
            winStreak = 0;
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
            retention = RetentionSave.Repair(retention);
            roundsWon = Math.Max(0, roundsWon);
            winStreak = Math.Max(0, winStreak);
            bestWinStreak = Math.Max(bestWinStreak, winStreak);
            mvps = Math.Max(0, mvps);
            bestRoundPoints = Math.Max(0, bestRoundPoints);
            closeCalls = Math.Max(0, closeCalls);
            abilitiesUsed = Math.Max(0, abilitiesUsed);
            roundsLeft = Math.Max(0, roundsLeft);
            bluffs = Math.Max(0, bluffs);
            roomsSearched = Math.Max(0, roomsSearched);
            pumpkins = Math.Max(0, pumpkins);
            pumpkinsSnuffed = Math.Max(0, pumpkinsSnuffed);
        }
    }
}
