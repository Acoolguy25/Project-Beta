using System;
using RyanAssets.Retention;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>
    /// The day's twist: one map event is featured each UTC day (<see cref="RetentionDay"/>, the daily
    /// challenges' calendar, so every server agrees). It comes every round that day and pays
    /// <see cref="HS_EventTuning.featuredPayMultiplier"/> times the points for its play, so tomorrow's
    /// rounds play differently from today's and not only the challenge list changes. Days walk through
    /// the three events in a shuffled order, never the same one two days running. Shared: the server
    /// plays it, the HUD names it.
    /// </summary>
    public static class HS_Daily {
        static readonly HS_RoundEvent[] Events = { HS_RoundEvent.Blackout, HS_RoundEvent.Frenzy, HS_RoundEvent.Shuffle };

        /// <summary>Today's featured event.</summary>
        public static HS_RoundEvent Today(DateTime utcNow) => Featured(RetentionDay.Of(utcNow));

        /// <summary>
        /// The featured event of <paramref name="day"/>. Days go in blocks of three, each a shuffle of the
        /// events seeded by the block; a block never starts with the event the block before ended on.
        /// Changing this reshuffles the days, so only in a release.
        /// </summary>
        public static HS_RoundEvent Featured(int day) {
            int n = Events.Length;
            int block = FloorDiv(day, n), index = day - block * n;
            int[] order = Shuffle(block);
            if (order[0] == Shuffle(block - 1)[n - 1]) (order[0], order[1]) = (order[1], order[0]);
            return Events[order[index]];
        }

        /// <summary>The twist's name for the HUD ("Blackout Night").</summary>
        public static string Title(HS_RoundEvent kind) => kind switch {
            HS_RoundEvent.Blackout => "Blackout Night",
            HS_RoundEvent.Frenzy => "Frenzy Day",
            HS_RoundEvent.Shuffle => "Shuffle Day",
            _ => "",
        };

        /// <summary>What the twist pays double for, as a short clause ("tags in the dark pay double").</summary>
        public static string Bonus(HS_RoundEvent kind) => kind switch {
            HS_RoundEvent.Blackout => "tags in the dark pay double",
            HS_RoundEvent.Frenzy => "close calls in a frenzy pay double",
            HS_RoundEvent.Shuffle => "changing rooms pays double",
            _ => "",
        };

        /// <summary>"Blackout Night: a blackout every round, tags in the dark pay double."</summary>
        public static string Describe(HS_RoundEvent kind) =>
            kind == HS_RoundEvent.None ? "" : $"{Title(kind)}: a {kind.ToString().ToLowerInvariant()} every round, {Bonus(kind)}.";

        /// <summary>
        /// Points for a play that belongs to <paramref name="play"/>'s event: multiplied while that event runs
        /// on the day it is featured, unchanged otherwise.
        /// </summary>
        public static int Pay(int points, HS_RoundEvent play, HS_RoundEvent active, HS_RoundEvent featured, int multiplier) =>
            play != HS_RoundEvent.None && active == play && featured == play ? points * Math.Max(1, multiplier) : points;

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        static int[] Shuffle(int block) {
            int n = Events.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            ulong state = ((ulong)DailyChallenges.StableHash(HS_SaveData.Universe + ":featured") << 32) ^ (uint)block ^ 0x9E3779B97F4A7C15UL;
            for (int i = n - 1; i > 0; i--) {
                int j = (int)(Next(ref state) % (ulong)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }

        /// <summary>SplitMix64, as the daily challenges use.</summary>
        static ulong Next(ref ulong state) {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
