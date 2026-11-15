using System;
using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Rounds {
    /// <summary>
    /// A polyline walked by distance: enemy lanes, ore belts, patrol routes. Every peer that builds the
    /// track from the same points gets the same positions, so only distances need to go over the wire.
    /// </summary>
    [Serializable]
    public sealed class PathTrack {
        readonly Vector3[] points;
        readonly float[] cumulative;

        public PathTrack(IReadOnlyList<Vector3> points) {
            if (points == null || points.Count < 2) throw new ArgumentException("A track needs at least two points.", nameof(points));
            this.points = new Vector3[points.Count];
            cumulative = new float[points.Count];
            for (int i = 0; i < points.Count; i++) {
                this.points[i] = points[i];
                if (i > 0) cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            }
        }

        public float Length => cumulative[^1];
        public int Count => points.Length;
        public Vector3 this[int index] => points[index];

        int Segment(float distance) {
            int lo = 0, hi = cumulative.Length - 1;
            while (hi - lo > 1) {
                int mid = (lo + hi) / 2;
                if (cumulative[mid] <= distance) lo = mid; else hi = mid;
            }
            return lo;
        }

        /// <summary>Position at <paramref name="distance"/> along the track, clamped to its ends.</summary>
        public Vector3 Evaluate(float distance) => Evaluate(distance, out _);

        public Vector3 Evaluate(float distance, out Vector3 forward) {
            distance = Mathf.Clamp(distance, 0f, Length);
            int i = Segment(distance);
            Vector3 a = points[i], b = points[Mathf.Min(i + 1, points.Length - 1)];
            float span = cumulative[Mathf.Min(i + 1, cumulative.Length - 1)] - cumulative[i];
            forward = (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : Vector3.forward;
            return span > 1e-5f ? Vector3.Lerp(a, b, (distance - cumulative[i]) / span) : a;
        }

        /// <summary>Distance along the track of the point closest to <paramref name="position"/>.</summary>
        public float Project(Vector3 position) {
            float best = float.MaxValue, bestDistance = 0f;
            for (int i = 0; i + 1 < points.Length; i++) {
                Vector3 a = points[i], ab = points[i + 1] - a;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(position - a, ab) / ab.sqrMagnitude) : 0f;
                float d = (a + ab * t - position).sqrMagnitude;
                if (d < best) { best = d; bestDistance = cumulative[i] + (cumulative[i + 1] - cumulative[i]) * t; }
            }
            return bestDistance;
        }
    }
}
