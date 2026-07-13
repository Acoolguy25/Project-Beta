using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Core {
    public static class MathHelper {
        public static int Mod(int x, int m) => (x % m + m) % m;

        public static void Shuffle<T>(this List<T> list) {
            for (int i = list.Count - 1; i > 0; i--) {
                int j = Random.Range(0, i + 1);

                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}