using System;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// Gold coins: a bonus that appears on the owner's plot now and then and pays seconds of income.
    /// Serialized on the server runner; shared so the pacing test measures the same coins.
    /// </summary>
    [Serializable]
    public sealed class TY_GoldCoins {
        [Tooltip("Seconds between gold coins on a plot (random in this range).")]
        public Vector2 interval = new(45f, 90f);
        [Tooltip("Seconds a gold coin waits before it vanishes.")]
        [Min(5f)] public float lifetime = 30f;
        [Tooltip("A gold coin pays this many seconds of the plot's income.")]
        [Min(1f)] public float incomeSeconds = 25f;
        [Min(0)] public long minimum = 50;

        public void Validate() {
            interval.x = Mathf.Max(1f, interval.x);
            interval.y = Mathf.Max(interval.x, interval.y);
            lifetime = Mathf.Max(5f, lifetime);
            incomeSeconds = Mathf.Max(1f, incomeSeconds);
            minimum = Math.Max(0, minimum);
        }

        /// <summary>The average wait between coins.</summary>
        public float MeanInterval => (interval.x + interval.y) * 0.5f;

        /// <summary>What a coin pays a plot earning <paramref name="incomePerSecond"/> (rush included), with Lucky Coins' <paramref name="lucky"/>.</summary>
        public long Reward(double incomePerSecond, float lucky) =>
            (long)Math.Min(long.MaxValue / 4d, Math.Max(minimum, Math.Max(0d, incomePerSecond) * incomeSeconds) * Math.Max(1f, lucky));
    }
}
