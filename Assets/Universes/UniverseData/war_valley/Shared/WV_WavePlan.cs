using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// How hard the waves get, and how fast. Serialized on the server runner so a designer tunes
    /// difficulty in the Inspector rather than hand-writing every wave.
    /// </summary>
    [Serializable]
    public sealed class WV_WaveTuning {
        [Tooltip("Waves in a round. Surviving the last one wins it.")]
        [Min(1)] public int WaveCount = 20;

        [Tooltip("Strength of the first wave, in knifemen. Tougher troops are worth more.")]
        [Min(1f)] public float BaseStrength = 5f;

        [Tooltip("Strength added every wave.")]
        [Min(0f)] public float StrengthPerWave = 2.5f;

        [Tooltip("Extra strength that compounds: later waves grow faster than early ones.")]
        [Min(0f)] public float StrengthAcceleration = 0.18f;

        [Tooltip("Every Nth wave is a surge: bigger, and announced. 0 turns surges off.")]
        [Min(0)] public int SurgeEvery = 5;

        [Tooltip("How much bigger a surge wave is.")]
        [Min(1f)] public float SurgeMultiplier = 1.5f;

        [Tooltip("Extra enemy health per wave, as a fraction: 0.06 is +6% a wave.")]
        [Min(0f)] public float HealthGrowthPerWave = 0.06f;

        [Tooltip("Most enemies one wave may field, so late waves stay playable.")]
        [Min(1)] public int MaxEnemiesPerWave = 60;

        [Tooltip("Seconds from the start of one wave to the next, early on.")]
        [Min(5)] public int FirstIntermissionSeconds = 45;

        [Tooltip("Shortest gap between waves, reached as the round goes on.")]
        [Min(5)] public int MinimumIntermissionSeconds = 25;

        [Tooltip("Seconds taken off the gap each wave until it reaches the minimum.")]
        [Min(0f)] public float IntermissionShrinkPerWave = 1f;
    }

    /// <summary>One batch of a single troop kind within a wave.</summary>
    public readonly struct WV_WaveGroup {
        public readonly WV_TroopKind Kind;
        public readonly int Count;

        public WV_WaveGroup(WV_TroopKind kind, int count) {
            Kind = kind;
            Count = count;
        }
    }

    /// <summary>Everything the runner needs to field one wave.</summary>
    public sealed class WV_Wave {
        /// <summary>Counting from 1.</summary>
        public int Number { get; }
        public IReadOnlyList<WV_WaveGroup> Groups { get; }
        public float HealthMultiplier { get; }
        public int IntermissionSeconds { get; }
        public bool IsSurge { get; }
        public int TotalCount { get; }

        public WV_Wave(int number, IReadOnlyList<WV_WaveGroup> groups, float healthMultiplier, int intermissionSeconds, bool isSurge) {
            Number = number;
            Groups = groups;
            HealthMultiplier = healthMultiplier;
            IntermissionSeconds = intermissionSeconds;
            IsSurge = isSurge;
            int total = 0;
            foreach (WV_WaveGroup group in groups)
                total += group.Count;
            TotalCount = total;
        }
    }

    /// <summary>
    /// Builds each wave from its number, so the round gets progressively harder without a hand-
    /// written table.
    /// <para>
    /// A wave has a strength budget that grows every wave and faster as the round goes on. New
    /// kinds of troop join the waves as they unlock (<see cref="WV_TroopProfile.FirstWave"/>) - the
    /// Shrimp swarm, then the Speedy, the Skinny Legend, and finally the Punk - and each kind is
    /// paid for from the budget at its threat, so a wave of Punks is small and a wave of Shrimp is
    /// large. The newest kind leads the wave it arrives in, earlier kinds keep a share, and enemy
    /// health climbs a little every wave on top. Every few waves a surge wave is bigger again.
    /// </para>
    /// <para>
    /// Deterministic: the same number and tuning always give the same wave, which is what lets the
    /// difficulty curve be tested and tuned.
    /// </para>
    /// </summary>
    public static class WV_WavePlan {
        public static WV_Wave Build(int waveNumber, WV_WaveTuning tuning) {
            if (tuning == null)
                throw new ArgumentNullException(nameof(tuning));

            waveNumber = Mathf.Max(1, waveNumber);
            int step = waveNumber - 1;
            bool surge = tuning.SurgeEvery > 0 && waveNumber % tuning.SurgeEvery == 0;

            float strength = GetStrength(waveNumber, tuning);
            List<WV_WaveGroup> groups = Compose(waveNumber, strength);
            groups = CapCount(groups, tuning.MaxEnemiesPerWave);

            float health = 1f + tuning.HealthGrowthPerWave * step;
            int intermission = Mathf.Max(
                tuning.MinimumIntermissionSeconds,
                Mathf.RoundToInt(tuning.FirstIntermissionSeconds - tuning.IntermissionShrinkPerWave * step));
            return new WV_Wave(waveNumber, groups, health, intermission, surge);
        }

        /// <summary>The wave's strength budget, in knifemen.</summary>
        public static float GetStrength(int waveNumber, WV_WaveTuning tuning) {
            int step = Mathf.Max(0, waveNumber - 1);
            float strength = tuning.BaseStrength
                             + tuning.StrengthPerWave * step
                             + tuning.StrengthAcceleration * step * step;
            bool surge = tuning.SurgeEvery > 0 && waveNumber % tuning.SurgeEvery == 0;
            return surge ? strength * tuning.SurgeMultiplier : strength;
        }

        /// <summary>
        /// Splits the budget across every kind already unlocked. The kind that unlocked most recently
        /// gets the largest share, so a new enemy is noticed the wave it appears; every other kind
        /// gets an even share of the rest, and every unlocked kind fields at least one soldier.
        /// </summary>
        static List<WV_WaveGroup> Compose(int waveNumber, float strength) {
            var unlocked = new List<WV_TroopProfile>();
            WV_TroopProfile newest = null;
            foreach (WV_TroopProfile profile in WV_TroopCatalog.All) {
                if (profile.FirstWave > waveNumber)
                    continue;
                unlocked.Add(profile);
                if (newest == null || profile.FirstWave > newest.FirstWave)
                    newest = profile;
            }

            var groups = new List<WV_WaveGroup>();
            if (unlocked.Count == 0)
                return groups;

            const float newestShare = 0.4f;
            float newestBudget = unlocked.Count == 1 ? strength : strength * newestShare;
            float otherBudget = unlocked.Count == 1 ? 0f : (strength - newestBudget) / (unlocked.Count - 1);

            foreach (WV_TroopProfile profile in unlocked) {
                float budget = profile == newest ? newestBudget : otherBudget;
                int count = Mathf.Max(1, Mathf.RoundToInt(budget / Mathf.Max(0.1f, profile.Threat)));
                groups.Add(new WV_WaveGroup(profile.Kind, count));
            }
            return groups;
        }

        /// <summary>Scales every group down evenly when a wave would field more than the cap.</summary>
        static List<WV_WaveGroup> CapCount(List<WV_WaveGroup> groups, int maxEnemies) {
            int total = 0;
            foreach (WV_WaveGroup group in groups)
                total += group.Count;
            if (total <= maxEnemies)
                return groups;

            float scale = maxEnemies / (float)total;
            var capped = new List<WV_WaveGroup>(groups.Count);
            int remaining = maxEnemies;
            foreach (WV_WaveGroup group in groups) {
                int count = Mathf.Clamp(Mathf.FloorToInt(group.Count * scale), 1, Mathf.Max(1, remaining));
                remaining -= count;
                capped.Add(new WV_WaveGroup(group.Kind, count));
            }
            return capped;
        }
    }
}
