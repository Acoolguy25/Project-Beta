using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// Difficulty tiers. A course is split into equal runs of stages, one per tier, from Basic to
    /// Extreme, and each tier has its own theme (look of its blocks, islands and sky).
    /// Values are stable: they index authored theme arrays.
    /// </summary>
    public enum OB_Tier : byte { Basic, Easy, Medium, Hard, Insane, Extreme }

    public static class OB_Tiers {
        public const int Count = 6;

        /// <summary>How much a tier's first stage eases off from the end of the previous tier (0..1 of a tier).</summary>
        const float Breather = 0.3f;

        /// <summary>The tier a stage belongs to. Stage 0 (the lobby run) is always Basic.</summary>
        public static OB_Tier TierOf(int stage, int stageCount) {
            if (stageCount <= 1) return OB_Tier.Basic;
            return (OB_Tier)Mathf.Clamp(stage * Count / stageCount, 0, Count - 1);
        }

        /// <summary>
        /// Difficulty 0..1 of a stage. It climbs through each tier, then eases off a little at the start of
        /// the next one, so intensity rises in waves instead of a flat ramp.
        /// </summary>
        public static float Difficulty(int stage, int stageCount) {
            if (stageCount <= 1) return 0f;
            int tier = (int)TierOf(stage, stageCount);
            int first = FirstStage(tier, stageCount), last = FirstStage(tier + 1, stageCount) - 1;
            float within = last > first ? (stage - first) / (float)(last - first) : 0f;
            return Mathf.Clamp01((tier + within * (1f + Breather)) / (Count + Breather));
        }

        /// <summary>0..1 position of <paramref name="stage"/> inside its tier (0 on its first stage, 1 on its last).</summary>
        public static float ProgressInTier(int stage, int stageCount) {
            if (stageCount <= 1) return 0f;
            int tier = (int)TierOf(stage, stageCount);
            int first = FirstStage(tier, stageCount), last = FirstStage(tier + 1, stageCount) - 1;
            return last > first ? Mathf.Clamp01((stage - first) / (float)(last - first)) : 0f;
        }

        /// <summary>First stage index of <paramref name="tier"/> (or stageCount past the last tier).</summary>
        public static int FirstStage(int tier, int stageCount) {
            if (tier >= Count) return stageCount;
            // Smallest stage s with s * Count / stageCount >= tier.
            return Mathf.Min(stageCount, (tier * stageCount + Count - 1) / Count);
        }

        public static string Name(OB_Tier tier) => tier switch {
            OB_Tier.Basic => "Basic",
            OB_Tier.Easy => "Easy",
            OB_Tier.Medium => "Medium",
            OB_Tier.Hard => "Hard",
            OB_Tier.Insane => "Insane",
            _ => "Extreme"
        };

        public static string Theme(OB_Tier tier) => tier switch {
            OB_Tier.Basic => "White Blocks",
            OB_Tier.Easy => "Grass Fields",
            OB_Tier.Medium => "Desert Dunes",
            OB_Tier.Hard => "Frozen Peaks",
            OB_Tier.Insane => "Volcano",
            _ => "The Void"
        };
    }
}
