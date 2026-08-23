using System;
using System.Collections.Generic;
using System.Threading;

namespace RyanAssets.Core {
    public static class MathHelper {
        public static int Mod(int x, int m) => (x % m + m) % m;

        public static void Shuffle<T>(this List<T> list) {
            for (int i = list.Count - 1; i > 0; i--) {
                int j = UnityEngine.Random.Range(0, i + 1);

                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // Returns a random float between min [inclusive] and max [inclusive].
        // Thread safe version of UnityEngine.Random.Range for floats.
        private static readonly ThreadLocal<System.Random> _random =
    new(() => new System.Random(Guid.NewGuid().GetHashCode()));

        public static float Range(float min, float max) {
            return (float)(_random.Value!.NextDouble() * (max - min) + min);
        }
    }
}