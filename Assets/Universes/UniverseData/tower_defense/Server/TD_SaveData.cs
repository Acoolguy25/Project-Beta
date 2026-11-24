using System;
using Newtonsoft.Json.Linq;
using RyanAssets.Progress;
using RyanAssets.Retention;

namespace Universes.UniverseData.tower_defense.Server {
    /// <summary>What tower defense remembers about a player between sessions. Written only by the server.</summary>
    public sealed class TD_SaveData : ProgressData {
        public const string Universe = TD_Retention.Universe;
        /// <summary>2: per-mode wins and stars (version 1 saves only knew Normal, so their wins become Normal wins).</summary>
        public override int LatestVersion => 2;

        /// <summary>Highest wave the player has started (fought in), in any mode.</summary>
        public int bestWave;
        /// <summary>Enemies finished off by the player's towers and abilities.</summary>
        public long totalKills;
        public int gamesWon;
        /// <summary>Games the player was in when they ended (won or lost).</summary>
        public int gamesPlayed;
        public int towersBuilt;
        public int towersUpgraded;
        /// <summary>Heavy Tank bosses finished off by the player's towers.</summary>
        public int bossesDestroyed;

        /// <summary>Wins on Normal (a first one unlocks Hard).</summary>
        public int normalWins;
        public int hardWins;
        /// <summary>Best stars won on Normal and on Hard (0..3).</summary>
        public int bestStarsNormal;
        public int bestStarsHard;
        /// <summary>Furthest wave reached in Endless.</summary>
        public int bestEndlessWave;
        public int shieldsBroken;
        public int abilitiesUsed;

        /// <summary>Levels, daily challenges and the play streak (the shared retention kit).</summary>
        public RetentionSave retention = new();

        /// <summary>Records <paramref name="wave"/> as fought in; true when it beats the previous best.</summary>
        public bool RecordWave(int wave) {
            if (wave <= bestWave) return false;
            bestWave = wave;
            return true;
        }

        /// <summary>Records an Endless wave reached; true when it beats the previous best.</summary>
        public bool RecordEndlessWave(int wave) {
            if (wave <= bestEndlessWave) return false;
            bestEndlessWave = wave;
            return true;
        }

        /// <summary>Records a win of <paramref name="mode"/> with <paramref name="stars"/>; true when the stars beat the mode's best.</summary>
        public bool RecordWin(TD_Difficulty mode, int stars) {
            gamesWon++;
            stars = Math.Clamp(stars, 0, TD_Rules.MaxStars);
            switch (mode) {
                case TD_Difficulty.Normal:
                    normalWins++;
                    if (stars <= bestStarsNormal) return false;
                    bestStarsNormal = stars;
                    return true;
                case TD_Difficulty.Hard:
                    hardWins++;
                    if (stars <= bestStarsHard) return false;
                    bestStarsHard = stars;
                    return true;
                default:
                    return false;
            }
        }

        public int BestStars(TD_Difficulty mode) => mode switch {
            TD_Difficulty.Normal => bestStarsNormal,
            TD_Difficulty.Hard => bestStarsHard,
            _ => 0,
        };

        /// <summary>Hard opens after a first Normal win.</summary>
        public bool HardUnlocked => normalWins > 0;

        public override void Migrate(int fromVersion, JObject stored) {
            // Version 1 had Normal only: every win so far was a Normal win.
            if (fromVersion < 2) normalWins = Math.Max(normalWins, gamesWon);
        }

        public override void Validate() {
            bestWave = Math.Max(0, bestWave);
            totalKills = Math.Max(0, totalKills);
            normalWins = Math.Max(0, normalWins);
            hardWins = Math.Max(0, hardWins);
            gamesWon = Math.Max(Math.Max(0, gamesWon), normalWins + hardWins);
            gamesPlayed = Math.Max(gamesWon, gamesPlayed);
            towersBuilt = Math.Max(0, towersBuilt);
            towersUpgraded = Math.Max(0, towersUpgraded);
            bossesDestroyed = Math.Max(0, bossesDestroyed);
            bestStarsNormal = Math.Clamp(bestStarsNormal, 0, TD_Rules.MaxStars);
            bestStarsHard = Math.Clamp(bestStarsHard, 0, TD_Rules.MaxStars);
            bestEndlessWave = Math.Max(0, bestEndlessWave);
            shieldsBroken = Math.Max(0, shieldsBroken);
            abilitiesUsed = Math.Max(0, abilitiesUsed);
            retention = RetentionSave.Repair(retention);
        }
    }
}
