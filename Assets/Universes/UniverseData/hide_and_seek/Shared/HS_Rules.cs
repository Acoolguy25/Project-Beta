using UnityEngine;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>Numbers both sides agree on.</summary>
    public static class HS_Rules {
        public const float TagRange = 3.6f;
        public const float PingScatter = 6f;
        public const int PlayersPerSeeker = 4;
        /// <summary>Seconds between two taunts by the same hider (the HUD shows the cooldown).</summary>
        public const float TauntCooldown = 12f;
        /// <summary>Taunts a hider may make in one round, so taunting cannot be farmed for XP.</summary>
        public const int TauntsPerRound = 6;

        /// <summary>Ping positions are blurred so a ping says "this area", not "this exact spot".</summary>
        public static Vector3 Blur(Vector3 position, System.Random random) {
            double a = random.NextDouble() * Mathf.PI * 2, r = random.NextDouble() * PingScatter;
            return position + new Vector3((float)(System.Math.Cos(a) * r), 0f, (float)(System.Math.Sin(a) * r));
        }

        /// <summary>Whether a taunt is allowed now: hiders only, while seekers hunt, within the cooldown and the round's allowance.</summary>
        public static bool CanTaunt(HS_Phase phase, HS_Role role, float secondsSinceLastTaunt, int tauntsThisRound) =>
            phase == HS_Phase.Seeking && role == HS_Role.Hider && secondsSinceLastTaunt >= TauntCooldown && tauntsThisRound < TauntsPerRound;

        /// <summary>
        /// XP for a taunt. Taunting with a seeker within <paramref name="braveRange"/> is "brave" and pays
        /// <paramref name="braveXP"/> instead of <paramref name="baseXP"/>: the closer the danger, the bigger the reward.
        /// </summary>
        public static int TauntReward(float nearestSeekerDistance, float braveRange, int baseXP, int braveXP, out bool brave) {
            brave = nearestSeekerDistance <= braveRange;
            return brave ? braveXP : baseXP;
        }

        /// <summary>0 when the nearest seeker is <paramref name="far"/> or further, 1 at <paramref name="near"/> or closer, eased in between.</summary>
        public static float Proximity(float distance, float near, float far) {
            if (far <= near) return distance <= near ? 1f : 0f;
            float t = Mathf.Clamp01((far - distance) / (far - near));
            return t * t * (3f - 2f * t);
        }
    }
}
