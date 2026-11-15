using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Rounds {
    /// <summary>
    /// Picks the interaction target a player is looking at: in reach, in front of the camera, best
    /// aligned. Clients use it for the [E] prompt; servers re-check reach before acting on a request.
    /// </summary>
    public static class FocusPicker {
        /// <returns>Index into <paramref name="targets"/>, or -1.</returns>
        public static int Pick(Vector3 eye, Vector3 forward, IReadOnlyList<Vector3> targets, float reach, float minAlignment = 0.55f,
            System.Predicate<int> allowed = null, Vector3? body = null) {
            int best = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < targets.Count; i++) {
                if (allowed != null && !allowed(i)) continue;
                Vector3 target = targets[i];
                // Reach is measured from the body (third-person cameras sit metres behind it).
                float distance = Vector3.Distance(body ?? eye, target);
                if (distance > reach) continue;
                Vector3 direction = target - eye;
                float alignment = direction.sqrMagnitude < 0.01f ? 1f : Vector3.Dot(forward, direction.normalized);
                if (alignment < minAlignment && distance > reach * 0.35f) continue;
                float score = alignment - distance / reach * 0.5f;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        /// <summary>Server-side reach check for a request about <paramref name="target"/>.</summary>
        public static bool InReach(Vector3 body, Vector3 target, float reach, float slack = 1f) =>
            (body - target).sqrMagnitude <= (reach + slack) * (reach + slack);
    }
}
