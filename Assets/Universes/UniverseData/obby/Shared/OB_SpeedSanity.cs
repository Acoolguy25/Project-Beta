using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// A cheap server-side speed check for client-authoritative movement (the Dash and launches are decided on the client).
    /// The envelope bounds where a racer may be; this bounds how fast the position may change, averaged over a window,
    /// so a teleport or a speed hack cannot cross the course between two envelope checks.
    /// </summary>
    public static class OB_SpeedSanity {
        /// <summary>Default planar speed limit (m/s): a cannon or ring launch (24) plus dash (19) plus a ride on a mover, with room.</summary>
        public const float DefaultLimit = 60f;

        /// <summary>Whether the sideways (horizontal) distance from <paramref name="from"/> to <paramref name="to"/> in <paramref name="seconds"/> is faster than <paramref name="limit"/>.</summary>
        public static bool Exceeds(Vector3 from, Vector3 to, float seconds, float limit) {
            if (seconds <= 0f) return false;
            Vector3 delta = to - from;
            delta.y = 0f;
            return delta.magnitude / seconds > limit;
        }
    }
}
