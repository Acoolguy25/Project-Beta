using System;
using System.Collections.Generic;
using RyanAssets.Progress;
using RyanAssets.Retention;
using Universes.UniverseData.war_valley.Shared;

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

        /// <summary>The retention kit's block: level, XP, daily challenges and streak.</summary>
        public RetentionSave retention = new();
        /// <summary>Best stars won on each tier, indexed by <see cref="WV_DifficultyTier"/>; 0 = never won.</summary>
        public List<int> tierStars = new();
        /// <summary>Highest wave reached in an Endless round.</summary>
        public int bestEndlessWave;
        /// <summary>Doctrines picked across every round.</summary>
        public int doctrinesPicked;

        /// <summary>Best stars on <paramref name="tier"/>.</summary>
        public int StarsOn(WV_DifficultyTier tier) => WV_Campaign.StarsOn(tierStars, tier);

        /// <summary>
        /// Keeps the better of the saved and the new stars on <paramref name="tier"/>. Returns true
        /// when this was the tier's first win.
        /// </summary>
        public bool RecordStars(WV_DifficultyTier tier, int stars) {
            if (!WV_Campaign.HasStars(tier) || stars <= 0)
                return false;
            int index = (int)tier;
            while (tierStars.Count <= index)
                tierStars.Add(0);
            bool first = tierStars[index] <= 0;
            tierStars[index] = Math.Max(tierStars[index], Math.Min(stars, WV_Campaign.MaxStars));
            return first;
        }

        /// <summary>Keeps the higher Endless wave. Returns true for a new record.</summary>
        public bool RecordEndlessWave(int wave) {
            if (wave <= bestEndlessWave)
                return false;
            bestEndlessWave = wave;
            return true;
        }

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
            retention = RetentionSave.Repair(retention);
            tierStars ??= new List<int>();
            if (tierStars.Count > WV_Campaign.Tiers.Length)
                tierStars.RemoveRange(WV_Campaign.Tiers.Length, tierStars.Count - WV_Campaign.Tiers.Length);
            for (int i = 0; i < tierStars.Count; i++)
                tierStars[i] = Math.Clamp(tierStars[i], 0, WV_Campaign.MaxStars);
            bestEndlessWave = Math.Max(0, bestEndlessWave);
            doctrinesPicked = Math.Max(0, doctrinesPicked);
        }
    }
}
