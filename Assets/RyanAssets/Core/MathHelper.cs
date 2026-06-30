using System.Collections;
using UnityEngine;

namespace RyanAssets.Core {
    public static class MathHelper {
        public static int Mod(int x, int m) => (x % m + m) % m;
    }
}