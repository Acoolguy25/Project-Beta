using System;
using RyanAssets.Retention;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>The day's twist on every game. A wire enum: APPEND-ONLY, never renumber.</summary>
    public enum TD_Modifier : byte { None, Fog, GoldRush, Blitz, Overcharge, IronHulls }

    /// <summary>What a daily modifier changes. Every one gives something and takes something, so no day is a free win.</summary>
    public readonly struct TD_ModifierStats {
        public readonly string name;
        /// <summary>One line for the vote and the mode banner ("Towers see 15% less far; kills pay 15% more").</summary>
        public readonly string blurb;
        /// <summary>Multipliers on tower range and fire rate.</summary>
        public readonly float rangeScale, fireRateScale;
        /// <summary>Multipliers on enemy health (the Airstrike keeps pace) and speed.</summary>
        public readonly float healthScale, speedScale;
        /// <summary>Multipliers on kill cash and the end-of-wave bonus.</summary>
        public readonly float rewardScale, waveBonusScale;
        /// <summary>Added to the mode's starting cash (may be negative).</summary>
        public readonly int startCash;

        public TD_ModifierStats(string name, string blurb, float rangeScale = 1f, float fireRateScale = 1f, float healthScale = 1f,
            float speedScale = 1f, float rewardScale = 1f, float waveBonusScale = 1f, int startCash = 0) {
            this.name = name; this.blurb = blurb; this.rangeScale = rangeScale; this.fireRateScale = fireRateScale;
            this.healthScale = healthScale; this.speedScale = speedScale; this.rewardScale = rewardScale;
            this.waveBonusScale = waveBonusScale; this.startCash = startCash;
        }
    }

    /// <summary>
    /// The daily modifier: one twist for every game of the day, on every server, picked from the UTC day
    /// (<see cref="RetentionDay"/>) the way the daily challenges are, so tomorrow's game is itself a new
    /// puzzle and not only its challenges. Days walk through every modifier in a shuffled order and the
    /// same one never comes two days running. Shared: the server plays it, the client draws and explains it.
    /// </summary>
    public static class TD_Daily {
        static readonly TD_ModifierStats[] Modifiers = {
            new("", ""),
            new("Fog", "Towers see 10% less far; kills pay 20% more.", rangeScale: 0.9f, rewardScale: 1.2f),
            new("Gold Rush", "Kills pay 30% more; enemies are 12% tougher.", rewardScale: 1.3f, healthScale: 1.12f),
            new("Blitz", "Enemies move 15% faster; the wave bonus is 30% bigger.", speedScale: 1.15f, waveBonusScale: 1.3f),
            new("Overcharge", "Towers fire 10% faster; you start with $150 less.", fireRateScale: 1.1f, startCash: -150),
            new("Iron Hulls", "Enemies are 15% tougher; you start with $200 more.", healthScale: 1.15f, startCash: 200),
        };

        /// <summary>Modifiers including <see cref="TD_Modifier.None"/>.</summary>
        public static int Count => Modifiers.Length;

        /// <summary>The modifiers a day can have (all but None).</summary>
        static int Daily => Modifiers.Length - 1;

        public static TD_ModifierStats Get(TD_Modifier modifier) => Modifiers[IsValid(modifier) ? (int)modifier : 0];

        public static bool IsValid(TD_Modifier modifier) => (int)modifier < Modifiers.Length;

        /// <summary>Today's modifier (UTC).</summary>
        public static TD_Modifier Today(DateTime utcNow) => For(RetentionDay.Of(utcNow));

        /// <summary>
        /// The modifier of <paramref name="day"/> (a <see cref="RetentionDay"/> number). Days go in blocks of
        /// <see cref="Daily"/>, each a shuffle of every modifier seeded by the block; a block never starts with
        /// the modifier the one before it ended on. Changing the table or this reshuffles the days, so only in a release.
        /// </summary>
        public static TD_Modifier For(int day) {
            int n = Daily;
            int block = FloorDiv(day, n), index = day - block * n;
            var order = Shuffle(block);
            if (n > 1 && order[0] == Shuffle(block - 1)[n - 1]) (order[0], order[1]) = (order[1], order[0]);
            return (TD_Modifier)(1 + order[index]);
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>A shuffle of 0..Daily-1 for <paramref name="block"/> (only its last entry is read for the block before, which the swap above never moves).</summary>
        static int[] Shuffle(int block) {
            int n = Daily;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            ulong state = ((ulong)DailyChallenges.StableHash(TD_Retention.Universe + ":modifier") << 32) ^ (uint)block ^ 0x9E3779B97F4A7C15UL;
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

        // ---------------------------------------------------------------- the numbers it changes (server and client use the same)

        public static float Range(TD_TowerKind kind, int level, TD_Modifier modifier) => TD_Rules.Range(kind, level) * Get(modifier).rangeScale;

        public static float FireRate(TD_TowerKind kind, int level, TD_Modifier modifier) => TD_Rules.FireRate(kind, level) * Get(modifier).fireRateScale;

        public static float Speed(TD_EnemyKind kind, TD_Modifier modifier) => TD_Rules.Enemy(kind).speed * Get(modifier).speedScale;

        public static int KillReward(TD_EnemyKind kind, TD_Difficulty mode, TD_Modifier modifier) =>
            Mathf.Max(1, Mathf.RoundToInt(TD_Rules.KillReward(kind, mode) * Get(modifier).rewardScale));

        public static int WaveBonus(int wave, TD_Difficulty mode, TD_Modifier modifier) =>
            Mathf.RoundToInt(TD_Rules.WaveBonus(wave, mode) * Get(modifier).waveBonusScale);

        public static int StartCash(TD_Difficulty mode, TD_Modifier modifier) => Mathf.Max(0, TD_Modes.Get(mode).startCash + Get(modifier).startCash);

        public static float HealthScale(int wave, TD_Difficulty mode, TD_Modifier modifier) => TD_Rules.HealthScale(wave, mode) * Get(modifier).healthScale;
    }
}
