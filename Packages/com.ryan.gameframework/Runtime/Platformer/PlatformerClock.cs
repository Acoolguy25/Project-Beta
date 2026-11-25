using RyanAssets.Core;
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// The time every timed platformer piece (beat blocks, lasers, crushers, pendulums) runs on: the
    /// shared network motion clock when it is ready, so the server and every client see the same phase,
    /// or local time before a connection exists (editor previews).
    /// </summary>
    public static class PlatformerClock {
        public static double Now {
            get {
                var clock = NetworkHelper.GetMotionClock();
                return clock != null && clock.IsReady ? clock.Time : Time.timeAsDouble;
            }
        }

        /// <summary>Seconds into a repeating cycle of <paramref name="period"/> that starts at <paramref name="offset"/>.</summary>
        public static float Cycle(double time, float period, float offset) {
            if (period <= 0f) return 0f;
            double t = (time - offset) % period;
            if (t < 0) t += period;
            return (float)t;
        }
    }
}
