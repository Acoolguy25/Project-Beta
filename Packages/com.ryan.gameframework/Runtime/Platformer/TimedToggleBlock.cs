using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A block that is solid for part of a repeating cycle, timed by the shared network clock so
    /// every peer sees the same blocks appear at the same moment. Two groups with offset phases
    /// make alternating "beat" paths. It blinks before vanishing.
    /// <para>
    /// The same component times laser gates: put it next to a trigger collider and a
    /// <see cref="HazardVolume"/>, and give it a <see cref="warnMaterial"/> with
    /// <see cref="warnBeforeSolidSeconds"/> so a beam always flickers before it switches on. The
    /// colliders toggle on the server too, so the server's hazard query sees the same beam.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TimedToggleBlock : MonoBehaviour {
        [Min(0.2f)] public float period = 4f;
        [Range(0.05f, 1f)] public float activeFraction = 0.6f;
        [Tooltip("Seconds into the cycle this block becomes solid.")]
        public float phaseOffset;
        [Tooltip("Seconds before vanishing that the block blinks.")]
        [Min(0f)] public float warnSeconds = 0.6f;
        [Tooltip("Seconds before becoming solid that the block flickers in the warn material (0 = no warning).")]
        [Min(0f)] public float warnBeforeSolidSeconds;
        [Tooltip("Blink rate of both warnings (flashes per second).")]
        [Min(0.5f)] public float blinkRate = 8f;
        public Renderer[] visuals;
        public Material solidMaterial, ghostMaterial;
        [Tooltip("Shown while warning that the block is about to become solid. Optional.")]
        public Material warnMaterial;

        enum Shown { Ghost, BlinkOff, Solid, WarnOn }

        Collider[] solids;
        Shown shown = (Shown)(-1);

        /// <summary>True for a block that never switches off (an always-on laser).</summary>
        public bool AlwaysSolid => activeFraction >= 1f;

        public void Configure(float cyclePeriod, float fraction, float offset) {
            period = Mathf.Max(0.2f, cyclePeriod);
            activeFraction = Mathf.Clamp(fraction, 0.05f, 1f);
            phaseOffset = offset;
            shown = (Shown)(-1);
        }

        void Awake() => solids = GetComponents<Collider>();

        /// <summary>Seconds into this block's cycle at the given shared time.</summary>
        public float CycleTime(double time) => PlatformerClock.Cycle(time, period, phaseOffset);

        public bool IsSolidAt(double time) => AlwaysSolid || CycleTime(time) < period * activeFraction;

        void FixedUpdate() {
            double time = PlatformerClock.Now;
            bool solid = IsSolidAt(time);
            foreach (var c in solids) if (c.enabled != solid) c.enabled = solid;
#if !UNITY_SERVER
            Shown state = solid ? Shown.Solid : Shown.Ghost;
            if (!AlwaysSolid) {
                float cycle = CycleTime(time), onFor = period * activeFraction;
                if (solid && onFor - cycle < warnSeconds && Blink(onFor - cycle)) state = Shown.BlinkOff;
                else if (!solid && warnMaterial != null && period - cycle < warnBeforeSolidSeconds && Blink(period - cycle)) state = Shown.WarnOn;
            }
            if (state == shown) return;
            shown = state;
            Material material = state switch {
                Shown.Solid => solidMaterial,
                Shown.WarnOn => warnMaterial,
                _ => ghostMaterial
            };
            if (material == null) return;
            foreach (var r in visuals) if (r != null) r.sharedMaterial = material;
#endif
        }

        bool Blink(float secondsLeft) => Mathf.Repeat(secondsLeft * blinkRate, 1f) < 0.45f;
    }
}
