using System;
using RyanAssets.Progress;

namespace Universes.UniverseData.obby.Server {
    /// <summary>What Sky Obby remembers about a player between sessions. Written only by the server.</summary>
    public sealed class OB_SaveData : ProgressData {
        public const string Universe = "obby";
        public override int LatestVersion => 1;

        /// <summary>Fastest finish in seconds; 0 until the first finish.</summary>
        public float bestFinishTime;
        public long totalCoins;
        public int coursesFinished;
        /// <summary>Furthest checkpoint reached in any race (the stage count when finished).</summary>
        public int bestStage;

        public void RecordStage(int stage) => bestStage = Math.Max(bestStage, stage);

        public void RecordFinish(float seconds, int stageCount) {
            coursesFinished++;
            RecordStage(stageCount);
            if (seconds > 0f && (bestFinishTime <= 0f || seconds < bestFinishTime)) bestFinishTime = seconds;
        }

        public override void Validate() {
            if (float.IsNaN(bestFinishTime) || float.IsInfinity(bestFinishTime) || bestFinishTime < 0f) bestFinishTime = 0f;
            totalCoins = Math.Max(0, totalCoins);
            coursesFinished = Math.Max(0, coursesFinished);
            bestStage = Math.Max(0, bestStage);
        }
    }
}
