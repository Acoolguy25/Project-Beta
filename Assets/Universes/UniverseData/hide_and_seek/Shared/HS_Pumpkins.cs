using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>What happened to a pumpkin: it appeared, a hider grabbed it, or a seeker snuffed it out.</summary>
    public enum HS_PumpkinTake : byte { None, Collected, Snuffed }

    /// <summary>One glowing pumpkin out on the map (ids are unique per server run, never reused).</summary>
    public struct HS_Pumpkin {
        public int id;
        public Vector3 position;
    }

    /// <summary>The pumpkin hunt's numbers (world units, seconds, points). Serialized on the server runner.</summary>
    [Serializable]
    public sealed class HS_PumpkinTuning {
        [Tooltip("Glowing pumpkins out at once while the seekers hunt (0 turns the hunt off).")]
        [Range(0, 8)] public int liveCount = 3;
        [Tooltip("Seconds into the seek phase before the first pumpkins light up, so hiders settle in first.")]
        [Min(0f)] public float firstSpawnDelay = 12f;
        [Tooltip("Seconds after a pumpkin is taken before another one lights up somewhere else.")]
        [Min(1f)] public float respawnSeconds = 16f;
        [Tooltip("How close (horizontally) a player's feet must come to take a pumpkin.")]
        [Min(0.5f)] public float pickupRadius = 3.2f;
        [Tooltip("Height difference allowed between feet and pumpkin, so nobody takes one through a floor.")]
        [Min(0.5f)] public float verticalReach = 3f;
        [Tooltip("Live pumpkins are at least this far apart, so they spread over the house and grounds.")]
        [Min(0f)] public float minSpacing = 30f;
        [Tooltip("A pumpkin never lights up this close to a player, so nobody is handed points where they stand.")]
        [Min(0f)] public float minPlayerDistance = 12f;

        [Header("Points")]
        [Tooltip("A hider grabbed a pumpkin. Everyone hears it and seekers see where.")]
        [Min(0)] public int collectPoints = 8;
        [Tooltip("Every this many pumpkins one hider grabs in a round fills a bag and pays Bag Points.")]
        [Min(1)] public int bagSize = 3;
        [Min(0)] public int bagPoints = 15;
        [Tooltip("A seeker (or found hider) snuffed a pumpkin out before a hider could grab it.")]
        [Min(0)] public int snuffPoints = 3;
        [Tooltip("Pumpkins one player is paid for in a round, so a long round cannot be farmed.")]
        [Min(1)] public int paidPerRound = 6;

        public void Validate() {
            liveCount = Mathf.Clamp(liveCount, 0, 8);
            firstSpawnDelay = Mathf.Max(0f, firstSpawnDelay);
            respawnSeconds = Mathf.Max(1f, respawnSeconds);
            pickupRadius = Mathf.Max(0.5f, pickupRadius);
            verticalReach = Mathf.Max(0.5f, verticalReach);
            minSpacing = Mathf.Max(0f, minSpacing);
            minPlayerDistance = Mathf.Max(0f, minPlayerDistance);
            collectPoints = Mathf.Max(0, collectPoints);
            bagSize = Mathf.Max(1, bagSize);
            bagPoints = Mathf.Max(0, bagPoints);
            snuffPoints = Mathf.Max(0, snuffPoints);
            paidPerRound = Mathf.Max(1, paidPerRound);
        }
    }

    /// <summary>
    /// The pumpkin hunt: while the seekers hunt, a few glowing jack-o'-lanterns light up around the manor.
    /// A hider who grabs one scores, but the crunch carries and seekers see where it happened, so leaving a
    /// good spot for one is a gamble. Seekers who reach one first snuff it out. Pure, so the server and
    /// tests agree on it.
    /// </summary>
    public static class HS_Pumpkins {
        /// <summary>What touching a pumpkin does for <paramref name="role"/>: hiders collect, the seekers' side snuffs, others nothing.</summary>
        public static HS_PumpkinTake TakeBy(HS_Role role) => role switch {
            HS_Role.Hider => HS_PumpkinTake.Collected,
            HS_Role.Seeker or HS_Role.Caught => HS_PumpkinTake.Snuffed,
            _ => HS_PumpkinTake.None,
        };

        /// <summary>Whether feet at <paramref name="feet"/> reach a pumpkin at <paramref name="pumpkin"/>.</summary>
        public static bool InReach(Vector3 feet, Vector3 pumpkin, float radius, float verticalReach) {
            if (Mathf.Abs(feet.y - pumpkin.y) > verticalReach) return false;
            float dx = feet.x - pumpkin.x, dz = feet.z - pumpkin.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>
        /// Picks where the next pumpkin lights up: a random point at least <paramref name="minSpacing"/> from every
        /// live pumpkin and <paramref name="minPlayerDistance"/> from every player. When no point is that clear
        /// (a small map, a crowd), the point farthest from all of them is used. -1 when there are no points.
        /// </summary>
        public static int PickSpot(IReadOnlyList<Vector3> points, IReadOnlyList<Vector3> live, IReadOnlyList<Vector3> players,
            float minSpacing, float minPlayerDistance, System.Random random) {
            if (points == null || points.Count == 0) return -1;
            int chosen = -1, seen = 0, fallback = -1;
            float fallbackScore = float.MinValue;
            for (int i = 0; i < points.Count; i++) {
                float spacing = Nearest(points[i], live), crowd = Nearest(points[i], players);
                if (spacing >= minSpacing && crowd >= minPlayerDistance) {
                    // Reservoir sampling: every clear point is equally likely, without a list.
                    if (random.Next(++seen) == 0) chosen = i;
                    continue;
                }
                // Score by the worse of the two shortfalls, so the fallback is as clear as the map allows.
                float score = Mathf.Min(spacing - minSpacing, crowd - minPlayerDistance);
                if (score > fallbackScore) { fallbackScore = score; fallback = i; }
            }
            return chosen >= 0 ? chosen : fallback;
        }

        /// <summary>Distance from <paramref name="point"/> to the nearest of <paramref name="others"/> (infinite when there are none).</summary>
        static float Nearest(Vector3 point, IReadOnlyList<Vector3> others) {
            float best = float.PositiveInfinity;
            if (others == null) return best;
            for (int i = 0; i < others.Count; i++) best = Mathf.Min(best, Vector3.Distance(point, others[i]));
            return best;
        }

        /// <summary>Whether a hider's <paramref name="collected"/>th pumpkin this round fills a bag of <paramref name="bagSize"/>.</summary>
        public static bool FillsBag(int collected, int bagSize) => bagSize > 0 && collected > 0 && collected % bagSize == 0;

        /// <summary>How many pumpkins of the current bag a hider holds after <paramref name="collected"/> this round (a full bag shows as full).</summary>
        public static int InBag(int collected, int bagSize) {
            if (bagSize <= 0 || collected <= 0) return 0;
            int rest = collected % bagSize;
            return rest == 0 ? bagSize : rest;
        }

        /// <summary>
        /// Removes the spawn times in <paramref name="schedule"/> that are due at <paramref name="now"/> and returns how
        /// many there were (the server schedules the first wave at the seek start and one more after each take).
        /// </summary>
        public static int TakeDue(List<float> schedule, float now) {
            int due = 0;
            for (int i = schedule.Count - 1; i >= 0; i--) {
                if (schedule[i] > now) continue;
                schedule.RemoveAt(i);
                due++;
            }
            return due;
        }
    }
}
