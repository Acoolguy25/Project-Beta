using UnityEngine;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>Numbers both sides agree on.</summary>
    public static class HS_Rules {
        public const float TagRange = 3.6f;
        public const float PingScatter = 6f;
        public const int PlayersPerSeeker = 4;

        /// <summary>Ping positions are blurred so a ping says "this area", not "this exact spot".</summary>
        public static Vector3 Blur(Vector3 position, System.Random random) {
            double a = random.NextDouble() * Mathf.PI * 2, r = random.NextDouble() * PingScatter;
            return position + new Vector3((float)(System.Math.Cos(a) * r), 0f, (float)(System.Math.Sin(a) * r));
        }
    }
}
