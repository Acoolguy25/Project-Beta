using System;
using System.Collections.Generic;

namespace RyanAssets.Retention {
    /// <summary>
    /// Picks each day's challenges from a game's pool. The pick depends only on the pool (its order and
    /// ids), the day and the universe id, through a hash and a random generator defined here, so every
    /// server shows the same challenges all day and a restart changes nothing. Changing the pool or
    /// this algorithm reshuffles the current day for everyone, so only do it in a release.
    /// </summary>
    public static class DailyChallenges {
        /// <summary>
        /// Adds <paramref name="count"/> challenges for <paramref name="day"/> to <paramref name="into"/>
        /// (fewer when the pool is smaller), preferring different stat keys so one statistic never
        /// completes several challenges, ordered by XP reward (easiest first).
        /// </summary>
        public static void Pick(IReadOnlyList<ChallengeDefinition> pool, int count, int day, string universeId, List<ChallengeDefinition> into) {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (into == null) throw new ArgumentNullException(nameof(into));
            int n = pool.Count;
            count = Math.Min(Math.Max(0, count), n);
            if (count == 0) return;

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            ulong state = Seed(day, universeId);
            for (int i = n - 1; i > 0; i--) {
                int j = (int)(Next(ref state) % (ulong)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }

            int start = into.Count;
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);
            var skipped = new List<ChallengeDefinition>();
            foreach (int index in order) {
                ChallengeDefinition challenge = pool[index];
                if (into.Count - start >= count) break;
                if (usedKeys.Add(challenge.statKey ?? string.Empty)) into.Add(challenge);
                else skipped.Add(challenge);
            }
            for (int i = 0; into.Count - start < count && i < skipped.Count; i++) into.Add(skipped[i]);

            into.Sort(start, into.Count - start, RewardOrder.Instance);
        }

        /// <summary>FNV-1a over the id's UTF-16 code units: stable everywhere, unlike string.GetHashCode.</summary>
        public static uint StableHash(string text) {
            uint hash = 2166136261;
            if (text == null) return hash;
            foreach (char c in text) {
                hash = (hash ^ (byte)c) * 16777619;
                hash = (hash ^ (byte)(c >> 8)) * 16777619;
            }
            return hash;
        }

        static ulong Seed(int day, string universeId) => ((ulong)StableHash(universeId) << 32) ^ (uint)day ^ 0x9E3779B97F4A7C15UL;

        /// <summary>SplitMix64: tiny, well mixed and fully specified here.</summary>
        static ulong Next(ref ulong state) {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        sealed class RewardOrder : IComparer<ChallengeDefinition> {
            public static readonly RewardOrder Instance = new();
            public int Compare(ChallengeDefinition a, ChallengeDefinition b) {
                int byXp = a.xpReward.CompareTo(b.xpReward);
                return byXp != 0 ? byXp : string.CompareOrdinal(a.id, b.id);
            }
        }
    }
}
