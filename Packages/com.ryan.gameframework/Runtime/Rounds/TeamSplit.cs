using System;
using System.Collections.Generic;

namespace RyanAssets.Rounds {
    /// <summary>Fair, shuffled role and team assignment for round-based games.</summary>
    public static class TeamSplit {
        /// <summary>Shuffles in place (Fisher-Yates) with the given random source.</summary>
        public static void Shuffle<T>(IList<T> items, Random random) {
            for (int i = items.Count - 1; i > 0; i--) {
                int j = random.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        /// <summary>
        /// Splits <paramref name="items"/> into <paramref name="teams"/> groups whose sizes differ by at most one.
        /// <paramref name="current"/> (optional) keeps existing members on their team where balance allows,
        /// so a late joiner does not reshuffle everyone.
        /// </summary>
        public static int[] Balance<T>(IReadOnlyList<T> items, int teams, Random random, Func<T, int> current = null) {
            if (teams < 1) throw new ArgumentOutOfRangeException(nameof(teams));
            var result = new int[items.Count];
            var counts = new int[teams];
            var order = new List<int>(items.Count);
            for (int i = 0; i < items.Count; i++) order.Add(i);
            Shuffle(order, random);
            int cap = (items.Count + teams - 1) / teams;
            var unplaced = new List<int>();
            foreach (int i in order) {
                int team = current?.Invoke(items[i]) ?? -1;
                if (team >= 0 && team < teams && counts[team] < cap) { result[i] = team; counts[team]++; }
                else unplaced.Add(i);
            }
            foreach (int i in unplaced) {
                int best = 0;
                for (int t = 1; t < teams; t++) if (counts[t] < counts[best]) best = t;
                result[i] = best;
                counts[best]++;
            }
            return result;
        }

        /// <summary>How many players get the minority role (seekers, murderers): one per <paramref name="perPlayers"/>, at least <paramref name="min"/>, never everyone.</summary>
        public static int MinorityCount(int players, int perPlayers, int min = 1) {
            if (players <= 1) return players;
            int count = Math.Max(min, players / Math.Max(1, perPlayers));
            return Math.Min(count, players - 1);
        }

        /// <summary>Picks <paramref name="count"/> distinct indices, preferring those that had the role least recently.</summary>
        public static List<int> PickRotating(int total, int count, Random random, Func<int, int> timesPicked = null) {
            var order = new List<int>(total);
            for (int i = 0; i < total; i++) order.Add(i);
            Shuffle(order, random);
            if (timesPicked != null) order.Sort((a, b) => timesPicked(a).CompareTo(timesPicked(b)));
            return order.GetRange(0, Math.Min(count, total));
        }
    }
}
