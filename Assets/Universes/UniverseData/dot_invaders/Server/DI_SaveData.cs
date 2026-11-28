#if UNITY_SERVER
using System;
using System.Collections.Generic;
using RyanAssets.Progress;
using RyanAssets.Retention;

namespace Universes.UniverseData.dot_invaders {
    /// <summary>What Dot Invaders remembers about a player between sessions. Written only by the server.</summary>
    public sealed class DI_SaveData : ProgressData {
        public const string Universe = DI_Retention.Universe;
        /// <summary>Rating of a save written before ratings existed (and of a new player).</summary>
        public const int DefaultRating = 1000;
        public override int LatestVersion => 1;

        /// <summary>Matches the player was still in when they ended.</summary>
        public int matches;
        public int wins;
        /// <summary>Bases the player's own troops captured, over all matches.</summary>
        public int basesCaptured;
        /// <summary>The most bases the player's team held at once in any match.</summary>
        public int mostBasesHeld;

        // Added in the retention pass. New fields keep their initialisers, so older saves load as a
        // new player's rating with no perks; nothing changed meaning, so the version stays 1.
        /// <summary>Levels, daily challenges and the play streak (shared retention kit).</summary>
        public RetentionSave retention = new();
        /// <summary>Skill rating (free-for-all Elo, see DI_Rating).</summary>
        public int rating = DefaultRating;
        public int peakRating = DefaultRating;
        public int ratedMatches;
        /// <summary>Teams whose last base this player took.</summary>
        public int eliminations;
        /// <summary>Equipped commander perks (DI_Perks ids), in slot order.</summary>
        public List<string> perks = new();

        // Added with bounties; an older save loads as none claimed.
        /// <summary>Bounties the player's troops claimed, over all matches.</summary>
        public int bountiesClaimed;

        public void RecordMatch(bool won) {
            matches++;
            if (won) wins++;
        }

        public void RecordBounties(int claimed) => bountiesClaimed += Math.Max(0, claimed);

        public void RecordCaptures(int captures, int basesHeld) {
            basesCaptured += Math.Max(0, captures);
            mostBasesHeld = Math.Max(mostBasesHeld, basesHeld);
        }

        /// <summary>Stores a rated result.</summary>
        public void RecordRating(int newRating) {
            rating = Math.Max(0, newRating);
            peakRating = Math.Max(peakRating, rating);
            if (ratedMatches < int.MaxValue) ratedMatches++;
        }

        public override void Validate() {
            matches = Math.Max(0, matches);
            wins = Math.Clamp(wins, 0, matches);
            basesCaptured = Math.Max(0, basesCaptured);
            mostBasesHeld = Math.Max(0, mostBasesHeld);
            retention = RetentionSave.Repair(retention);
            rating = Math.Max(0, rating);
            peakRating = Math.Max(peakRating, rating);
            ratedMatches = Math.Max(0, ratedMatches);
            eliminations = Math.Max(0, eliminations);
            bountiesClaimed = Math.Max(0, bountiesClaimed);
            perks ??= new List<string>();
            perks.RemoveAll(id => DI_Perks.Find(id) == null);
            if (perks.Count > DI_Perks.MaxSlots) perks.RemoveRange(DI_Perks.MaxSlots, perks.Count - DI_Perks.MaxSlots);
        }
    }
}
#endif
