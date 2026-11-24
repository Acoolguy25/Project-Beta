using System;
using UnityEngine;

namespace RyanAssets.Retention {
    /// <summary>
    /// Every number of the retention kit a game may tune, in one serializable block: put it on the
    /// server runner (<c>[SerializeField] RetentionTuning retention = new();</c>) and pass it to
    /// <see cref="RetentionRules"/>. Values are copied (and clamped) when the rules are built.
    /// </summary>
    [Serializable]
    public sealed class RetentionTuning {
        public LevelCurve levels = new();
        public StreakRules streak = new();
        [Tooltip("Daily challenges shown per UTC day.")]
        [Range(1, RetentionSave.MaxChallenges)] public int dailyChallengeCount = 3;
        [Tooltip("Account XP (LevelsServer) paid per game XP. 1 = the same amount; 0 = game XP only.")]
        [Min(0f)] public float accountXpScale = 1f;

        public void Validate() {
            levels ??= new LevelCurve();
            streak ??= new StreakRules();
            levels.Validate();
            streak.Validate();
            dailyChallengeCount = Math.Clamp(dailyChallengeCount, 1, RetentionSave.MaxChallenges);
            accountXpScale = float.IsNaN(accountXpScale) ? 1f : Math.Max(0f, accountXpScale);
        }

        /// <summary>A deep copy, so later inspector edits never change rules that are already running.</summary>
        public RetentionTuning Clone() {
            var copy = (RetentionTuning)MemberwiseClone();
            copy.levels = (levels ?? new LevelCurve()).Clone();
            copy.streak = (streak ?? new StreakRules()).Clone();
            return copy;
        }
    }
}
