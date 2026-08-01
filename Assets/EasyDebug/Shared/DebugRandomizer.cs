using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace EasyDebug.Shared {
    public class DebugRandomizer: List<float> {
        public int Random() {
            if (Count == 0) {
                Debug.LogWarning("DebugRandomize: List is empty, returning -1");
                return -1;
            }
            float total = this.Sum();
            float randomValue = UnityEngine.Random.Range(0f, total);
            float cumulative = 0f;
            for (int i = 0; i < Count; i++) {
                cumulative += this[i];
                if (randomValue <= cumulative) {
                    return i;
                }
            }
            return Count - 1; // Fallback in case of rounding errors
        }
    }
}