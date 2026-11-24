using System;
using RyanAssets.Progress;
using RyanAssets.Retention;

namespace Universes.UniverseData.obby.Server {
    /// <summary>What Sky Obby remembers about a player between sessions. Written only by the server.</summary>
    public sealed class OB_SaveData : ProgressData {
        public const string Universe = OB_Retention.Universe;
        /// <summary>2: retention block, stars, tier bests.</summary>
        public override int LatestVersion => 2;

        /// <summary>Fastest finish in seconds (clock time, before the coin bonus); 0 until the first finish.</summary>
        public float bestFinishTime;
        public long totalCoins;
        public int coursesFinished;
        /// <summary>Furthest checkpoint reached in any race (the stage count when finished).</summary>
        public int bestStage;
        /// <summary>Best scored time (finish time minus the coin bonus); 0 until the first finish.</summary>
        public float bestScoredTime;
        /// <summary>Best medal ever earned, as <see cref="OB_Medal"/>.</summary>
        public int bestMedal;
        public int goldMedals, silverMedals, bronzeMedals;
        /// <summary>Races won (first in the final results).</summary>
        public int wins;
        /// <summary>Races finished in the top three of the final results.</summary>
        public int podiums;

        // Version 2.
        /// <summary>Levels, daily challenges and streak (the shared retention kit).</summary>
        public RetentionSave retention = new();
        /// <summary>Stars collected over every race.</summary>
        public long totalStars;
        /// <summary>Most stars collected in one race.</summary>
        public int bestRunStars;
        /// <summary>Fastest time through each tier (indexed by <see cref="OB_Tier"/>; 0 = not cleared yet).</summary>
        public float[] tierBestTimes = new float[OB_Tiers.Count];
        /// <summary>Most stars collected in each tier in one race.</summary>
        public int[] tierBestStars = new int[OB_Tiers.Count];
        /// <summary>Tiers cleared without a fall or hazard, and with every coin and star.</summary>
        public int cleanTiers, perfectTiers;

        public void RecordStage(int stage) => bestStage = Math.Max(bestStage, stage);

        /// <summary>Records a finish and returns whether its scored time is a new personal best.</summary>
        public bool RecordFinish(float seconds, float scoredSeconds, OB_Medal medal, int stageCount) {
            coursesFinished++;
            RecordStage(stageCount);
            if (seconds > 0f && (bestFinishTime <= 0f || seconds < bestFinishTime)) bestFinishTime = seconds;
            switch (medal) {
                case OB_Medal.Gold: goldMedals++; break;
                case OB_Medal.Silver: silverMedals++; break;
                case OB_Medal.Bronze: bronzeMedals++; break;
            }
            bestMedal = Math.Max(bestMedal, (int)medal);
            bool best = scoredSeconds > 0f && (bestScoredTime <= 0f || scoredSeconds < bestScoredTime);
            if (best) bestScoredTime = scoredSeconds;
            return best;
        }

        /// <summary>
        /// Records clearing <paramref name="tier"/> in <paramref name="seconds"/> with <paramref name="stars"/> of
        /// its stars. Returns the best time before this one (0 when the tier was never cleared).
        /// </summary>
        public float RecordTier(OB_Tier tier, float seconds, int stars, bool clean, bool perfect) {
            int t = (int)tier;
            float previous = tierBestTimes[t];
            if (seconds > 0f && (previous <= 0f || seconds < previous)) tierBestTimes[t] = seconds;
            tierBestStars[t] = Math.Max(tierBestStars[t], stars);
            if (clean) cleanTiers++;
            if (perfect) perfectTiers++;
            return previous;
        }

        /// <summary>Best time through <paramref name="tier"/> (0 when never cleared).</summary>
        public float TierBest(OB_Tier tier) => tierBestTimes[(int)tier];

        /// <summary>Records a star collected (call once per star) and the run's total so far.</summary>
        public void RecordStar(int runStars) {
            totalStars++;
            bestRunStars = Math.Max(bestRunStars, runStars);
        }

        /// <summary>Records a final race place (1-based). Only call it for races with at least two racers.</summary>
        public void RecordPlace(int place) {
            if (place == 1) wins++;
            if (place is >= 1 and <= 3) podiums++;
        }

        public override void Validate() {
            bestFinishTime = ValidTime(bestFinishTime);
            bestScoredTime = ValidTime(bestScoredTime);
            // Saves from before the coin bonus have a finish time only. A scored time is never slower than
            // the finish it came from, so the old finish is a safe best to beat (no free "NEW BEST!").
            if (bestScoredTime <= 0f && bestFinishTime > 0f) bestScoredTime = bestFinishTime;
            totalCoins = Math.Max(0, totalCoins);
            coursesFinished = Math.Max(0, coursesFinished);
            bestStage = Math.Max(0, bestStage);
            bestMedal = Math.Clamp(bestMedal, (int)OB_Medal.None, (int)OB_Medal.Gold);
            goldMedals = Math.Max(0, goldMedals);
            silverMedals = Math.Max(0, silverMedals);
            bronzeMedals = Math.Max(0, bronzeMedals);
            wins = Math.Max(0, wins);
            podiums = Math.Max(0, podiums);
            // Version 1 saves have none of the fields below; they start empty.
            retention = RetentionSave.Repair(retention);
            totalStars = Math.Max(0, totalStars);
            bestRunStars = Math.Max(0, bestRunStars);
            tierBestTimes = Fit(tierBestTimes);
            for (int i = 0; i < tierBestTimes.Length; i++) tierBestTimes[i] = ValidTime(tierBestTimes[i]);
            tierBestStars = Fit(tierBestStars);
            for (int i = 0; i < tierBestStars.Length; i++) tierBestStars[i] = Math.Max(0, tierBestStars[i]);
            cleanTiers = Math.Max(0, cleanTiers);
            perfectTiers = Math.Max(0, perfectTiers);
        }

        /// <summary>An array of one entry per tier, keeping what <paramref name="values"/> already holds.</summary>
        static T[] Fit<T>(T[] values) {
            if (values != null && values.Length == OB_Tiers.Count) return values;
            var fitted = new T[OB_Tiers.Count];
            if (values != null) Array.Copy(values, fitted, Math.Min(values.Length, fitted.Length));
            return fitted;
        }

        static float ValidTime(float seconds) => float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f ? 0f : seconds;
    }
}
