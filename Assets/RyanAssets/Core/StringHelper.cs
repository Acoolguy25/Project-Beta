using System.Collections;
using UnityEngine;

namespace RyanAssets.Core {
    public static class StringHelper {
        public static string Capitalize(string value) {
            if (string.IsNullOrEmpty(value))
                return value;

            return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
        }
    }
}