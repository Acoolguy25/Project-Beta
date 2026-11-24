using System;
using UnityEngine;

namespace RyanAssets.Retention {
    /// <summary>
    /// How much XP each level of a game's <see cref="LevelTrack"/> costs:
    /// <c>xpToNext(level) = (firstLevelXp + extraXpPerLevel * (level - 1)) * growth^(level - 1)</c>.
    /// The defaults reach level 2 after one good match, level 10 after about a dozen and level 50
    /// after a few hundred, so there is always a level within reach.
    /// </summary>
    [Serializable]
    public sealed class LevelCurve {
        public const int MinLevels = 2;
        public const int MaxLevels = 500;

        [Tooltip("Highest level a player can reach.")]
        [Range(MinLevels, MaxLevels)] public int maxLevel = 50;
        [Tooltip("XP from level 1 to level 2.")]
        [Min(1)] public int firstLevelXp = 100;
        [Tooltip("Extra XP every further level costs on top of the previous one (linear growth).")]
        [Min(0)] public int extraXpPerLevel = 25;
        [Tooltip("Multiplier applied per level on top of the linear growth (1 = linear only).")]
        [Range(1f, 1.5f)] public float growth = 1f;

        /// <summary>XP needed to go from <paramref name="level"/> to the next one (at least 1).</summary>
        public int XpToNext(int level) {
            int step = Math.Max(1, level) - 1;
            double xp = (Math.Max(1, firstLevelXp) + (double)Math.Max(0, extraXpPerLevel) * step) * Math.Pow(Math.Max(1f, growth), step);
            return (int)Math.Clamp(Math.Round(xp), 1d, int.MaxValue / 4d);
        }

        /// <summary>Clamps every field into its documented range (saved inspector values can be anything).</summary>
        public void Validate() {
            maxLevel = Math.Clamp(maxLevel, MinLevels, MaxLevels);
            firstLevelXp = Math.Max(1, firstLevelXp);
            extraXpPerLevel = Math.Max(0, extraXpPerLevel);
            growth = float.IsNaN(growth) ? 1f : Math.Clamp(growth, 1f, 1.5f);
        }

        public LevelCurve Clone() => (LevelCurve)MemberwiseClone();
    }
}
