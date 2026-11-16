using System;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>What a <see cref="TY_RushHour.Tick"/> changed.</summary>
    public enum TY_RushChange : byte { None, Started, Ended }

    /// <summary>
    /// The "Rush hour" timed event: every few minutes every tycoon earns more for a short while. The
    /// server owns one and steps it with its own clock and random numbers, so the rules stay plain and
    /// testable. Not saved: an event belongs to the running server, not to a player.
    /// </summary>
    [Serializable]
    public sealed class TY_RushHour {
        [Tooltip("Seconds from the end of one rush hour to the start of the next (random in this range).")]
        public Vector2 interval = new(240f, 420f);
        [Tooltip("How long a rush hour lasts, in seconds.")]
        [Min(1f)] public float duration = 30f;
        [Tooltip("Income multiplier while a rush hour runs.")]
        [Min(1f)] public float multiplier = 2f;

        [NonSerialized] float nextAt = float.PositiveInfinity;
        [NonSerialized] float endsAt = float.NegativeInfinity;

        public bool IsActive(float now) => now < endsAt;

        /// <summary>Seconds the running rush hour has left, 0 when none runs.</summary>
        public float Remaining(float now) => Mathf.Max(0f, endsAt - now);

        /// <summary>Income multiplier at <paramref name="now"/> (1 outside a rush hour).</summary>
        public float Multiplier(float now) => IsActive(now) ? multiplier : 1f;

        /// <summary>When the next rush hour is due (for probes and the top message).</summary>
        public float NextAt => nextAt;

        /// <summary>Plans the next rush hour; <paramref name="random01"/> picks a point in <see cref="interval"/>.</summary>
        public void Schedule(float now, float random01) {
            Validate();
            nextAt = now + Mathf.Lerp(interval.x, interval.y, Mathf.Clamp01(random01));
        }

        /// <summary>Starts a rush hour right away (server console and probes).</summary>
        public void Begin(float now) {
            Validate();
            endsAt = now + duration;
            nextAt = float.PositiveInfinity;
        }

        /// <summary>
        /// Advances the event. While <paramref name="enabled"/> is false (nobody to earn) a due rush
        /// hour is pushed back instead of starting, so it never fires the moment someone claims a plot.
        /// </summary>
        public TY_RushChange Tick(float now, float random01, bool enabled) {
            if (endsAt > float.NegativeInfinity) {
                if (now < endsAt) return TY_RushChange.None;
                endsAt = float.NegativeInfinity;
                Schedule(now, random01);
                return TY_RushChange.Ended;
            }
            if (now < nextAt) return TY_RushChange.None;
            if (!enabled) {
                Schedule(now, random01);
                return TY_RushChange.None;
            }
            Begin(now);
            return TY_RushChange.Started;
        }

        /// <summary>Keeps the tuning sane (called on schedule and from the runner's OnValidate).</summary>
        public void Validate() {
            interval.x = Mathf.Max(1f, interval.x);
            interval.y = Mathf.Max(interval.x, interval.y);
            duration = Mathf.Max(1f, duration);
            multiplier = Mathf.Max(1f, multiplier);
        }
    }
}
