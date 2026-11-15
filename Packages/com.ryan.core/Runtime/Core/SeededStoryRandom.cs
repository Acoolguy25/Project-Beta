using System;
using System.Collections.Generic;

namespace RyanAssets.Core {
    /// <summary>Stable, isolated randomness for reproducible stories and layouts.
    /// Does not change Unity's global random state or depend on runtime RNG versions.</summary>
    public sealed class SeededStoryRandom {
        uint state;
        public SeededStoryRandom(int seed) => state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        public int Next(int count) {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (int)((ulong)state * (uint)count >> 32);
        }
        public T Pick<T>(IReadOnlyList<T> values) => values[Next(values.Count)];
        public void Shuffle<T>(IList<T> values) {
            for (int i = values.Count - 1; i > 0; i--) {
                int j = Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }
        public static string Expand(string template, IReadOnlyDictionary<string, string> tokens) {
            foreach (var token in tokens) template = template.Replace("{" + token.Key + "}", token.Value);
            return template;
        }
    }
}
