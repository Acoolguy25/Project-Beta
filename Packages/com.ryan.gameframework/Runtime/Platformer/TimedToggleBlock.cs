using RyanAssets.Core;
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A block that is solid for part of a repeating cycle, timed by the shared network clock so
    /// every peer sees the same blocks appear at the same moment. Two groups with offset phases
    /// make alternating "beat" paths. It blinks before vanishing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TimedToggleBlock : MonoBehaviour {
        [Min(0.2f)] public float period = 4f;
        [Range(0.05f, 0.95f)] public float activeFraction = 0.6f;
        [Tooltip("Seconds into the cycle this block becomes solid.")]
        public float phaseOffset;
        [Min(0f)] public float warnSeconds = 0.6f;
        public Renderer[] visuals;
        public Material solidMaterial, ghostMaterial;

        Collider[] solids;
        int shownState = -1;

        public void Configure(float cyclePeriod, float fraction, float offset) {
            period = Mathf.Max(0.2f, cyclePeriod);
            activeFraction = Mathf.Clamp(fraction, 0.05f, 0.95f);
            phaseOffset = offset;
            shownState = -1;
        }

        void Awake() => solids = GetComponents<Collider>();

        static double SharedTime() {
            var clock = NetworkHelper.GetMotionClock();
            return clock != null && clock.IsReady ? clock.Time : Time.timeAsDouble;
        }

        /// <summary>Seconds into this block's cycle at the given shared time.</summary>
        public float CycleTime(double time) {
            double t = (time - phaseOffset) % period;
            if (t < 0) t += period;
            return (float)t;
        }

        public bool IsSolidAt(double time) => CycleTime(time) < period * activeFraction;

        void FixedUpdate() {
            double time = SharedTime();
            bool solid = IsSolidAt(time);
            foreach (var c in solids) if (c.enabled != solid) c.enabled = solid;
#if !UNITY_SERVER
            float untilOff = period * activeFraction - CycleTime(time);
            bool blinkOff = solid && untilOff < warnSeconds && Mathf.Repeat(untilOff * 8f, 1f) < 0.45f;
            int state = !solid ? 0 : blinkOff ? 1 : 2;
            if (state == shownState) return;
            shownState = state;
            Material material = state == 2 ? solidMaterial : ghostMaterial;
            if (material == null) return;
            foreach (var r in visuals) if (r != null) r.sharedMaterial = material;
#endif
        }
    }
}
