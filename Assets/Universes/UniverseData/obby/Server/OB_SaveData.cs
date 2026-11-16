using System;
using RyanAssets.Progress;

namespace Universes.UniverseData.obby.Server {
    /// <summary>What Sky Obby remembers about a player between sessions. Written only by the server.</summary>
    public sealed class OB_SaveData : ProgressData {
        public const string Universe = "obby";
        public override int LatestVersion => 1;

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

        /// <summary>Records a final race place (1-based).</summary>
        public void RecordPlace(int place) {
            if (place == 1) wins++;
            if (place is >= 1 and <= 3) podiums++;
        }

        public override void Validate() {
            bestFinishTime = ValidTime(bestFinishTime);
            bestScoredTime = ValidTime(bestScoredTime);
            totalCoins = Math.Max(0, totalCoins);
            coursesFinished = Math.Max(0, coursesFinished);
            bestStage = Math.Max(0, bestStage);
            bestMedal = Math.Clamp(bestMedal, (int)OB_Medal.None, (int)OB_Medal.Gold);
            goldMedals = Math.Max(0, goldMedals);
            silverMedals = Math.Max(0, silverMedals);
            bronzeMedals = Math.Max(0, bronzeMedals);
            wins = Math.Max(0, wins);
            podiums = Math.Max(0, podiums);
        }

        static float ValidTime(float seconds) => float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f ? 0f : seconds;
    }
}
