using System;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// How the flag soaks a siege. Without it a handful of knifemen tore the 1000-health flag down
    /// in two or three seconds of reaching it, too fast for a commander to see the alarm, look and
    /// answer. Now each hit is cut and the flag can lose only so much health a second, so a mob at
    /// the flag is a race the defenders can still win.
    /// </summary>
    [Serializable]
    public sealed class WV_FlagArmorTuning {
        [Tooltip("Share of each hit the flag actually takes.")]
        [Range(0.05f, 1f)] public float DamageTakenMultiplier = 0.5f;

        [Tooltip("Most of its maximum health the flag can lose in one second, however many attack it. " +
                 "0.035 means a full flag holds for about 27 seconds against any crowd.")]
        [Range(0.005f, 1f)] public float MaxLossPerSecondFraction = 0.035f;

        [Tooltip("Seconds of that maximum loss that can land at once, so the first blows still hurt.")]
        [Min(0.1f)] public float BurstSeconds = 1.5f;

        /// <summary>Shortest time the flag can last from full health, for tests and tooltips.</summary>
        public float MinimumSecondsFromFull(float difficultyMultiplier = 1f) {
            float perSecond = MaxLossPerSecondFraction * Mathf.Max(0.1f, difficultyMultiplier);
            return Mathf.Max(0f, (1f - perSecond * BurstSeconds) / perSecond);
        }
    }

    /// <summary>
    /// The flag's running armor state: a budget of health it may still lose that refills at the
    /// capped rate. Pure, so the cap is covered by EditMode tests.
    /// </summary>
    public struct WV_FlagArmor {
        float budget;
        float carry;
        float lastTime;
        bool started;

        /// <summary>
        /// The health a hit of <paramref name="damage"/> actually removes at time <paramref name="now"/>,
        /// for a flag of <paramref name="maxHealth"/>. <paramref name="difficultyMultiplier"/> scales
        /// both the cut and the cap. Fractions of a point carry over to the next hit.
        /// </summary>
        public long Absorb(long damage, float now, long maxHealth, WV_FlagArmorTuning tuning, float difficultyMultiplier = 1f) {
            if (damage <= 0 || maxHealth <= 0 || tuning == null)
                return Math.Max(0, damage);

            float multiplier = Mathf.Max(0.1f, difficultyMultiplier);
            float perSecond = maxHealth * tuning.MaxLossPerSecondFraction * multiplier;
            float capacity = perSecond * Mathf.Max(0.1f, tuning.BurstSeconds);
            if (!started) {
                started = true;
                budget = capacity;
            } else {
                budget = Mathf.Min(capacity, budget + perSecond * Mathf.Max(0f, now - lastTime));
            }
            lastTime = now;

            float wanted = damage * Mathf.Clamp(tuning.DamageTakenMultiplier * multiplier, 0f, 1f) + carry;
            float taken = Mathf.Min(wanted, budget);
            long whole = (long)taken;
            budget -= whole;
            carry = Mathf.Min(taken - whole, 1f);
            return whole;
        }

        /// <summary>Forgets the running budget, as at the start of a round.</summary>
        public void Reset() => this = default;
    }
}
