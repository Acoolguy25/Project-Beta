using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A heavy block that waits, shakes, slams down <see cref="drop"/> metres, holds, and slowly rises
    /// again, on the shared clock (a Thwomp). Its <see cref="crushZone"/> (a trigger with a
    /// <see cref="HazardVolume"/> along the block's underside) is enabled only while the block is
    /// slamming or held down, on the server as well, so the server's hazard query decides who was
    /// crushed. Inset the zone from the block's sides so touching a resting crusher is safe.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class Crusher : MonoBehaviour {
        public enum Phase { Waiting, Warning, Slamming, Down, Rising }

        [Tooltip("How far the block falls (m), from its raised pose (where it is placed).")]
        [Min(0.1f)] public float drop = 6f;
        [Tooltip("Seconds for one whole cycle.")]
        [Min(0.5f)] public float period = 3f;
        [Tooltip("Seconds added to the shared clock, so a row of crushers can slam in sequence.")]
        public float phaseOffset;
        [Tooltip("Seconds the slam takes.")]
        [Min(0.02f)] public float slamSeconds = 0.16f;
        [Tooltip("Seconds the block stays down.")]
        [Min(0f)] public float holdSeconds = 0.6f;
        [Tooltip("Seconds it takes to rise again.")]
        [Min(0.05f)] public float riseSeconds = 0.7f;
        [Tooltip("Seconds before the slam that the block shakes (clients only).")]
        [Min(0f)] public float warnSeconds = 0.45f;
        [Tooltip("Shake amplitude (m) while warning.")]
        [Min(0f)] public float warnShake = 0.08f;
        // Appended fields.
        [Tooltip("Shake frequencies (radians per second) across x and z; unrelated values keep the shake from looking like a circle.")]
        public Vector2 warnShakeFrequency = new(71f, 53f);
        [Tooltip("Optional child marking where the block lands (a shadow plate or hazard stripes): authored just under the block, it is moved out from under the block onto the landing spot when the crusher is set up, so it stays put while the block moves.")]
        public Transform landingMark;
        [Tooltip("Trigger with a HazardVolume along the block's underside.")]
        public Collider crushZone;
        [Tooltip("Child that shakes while warning (the solid block itself never shakes).")]
        public Transform visual;

        Rigidbody body;
        Vector3 raised, visualRest, landingSpot, markRest;
        Phase lastPhase = Phase.Waiting;

#if !UNITY_SERVER
        /// <summary>Raised on clients when any crusher lands (games add the thud and shake near the player).</summary>
        public static event System.Action<Crusher> Slammed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Slammed = null;
#endif

        void Awake() {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            raised = transform.position;
            if (visual != null) visualRest = visual.localPosition;
            if (landingMark != null) markRest = landingMark.localPosition;
            PlaceLandingMark();
        }

        /// <summary>Sets up a crusher spawned at runtime; the current position becomes the raised pose.</summary>
        public void Configure(float dropDistance, float cyclePeriod, float offset, float hold) {
            drop = Mathf.Max(0.1f, dropDistance);
            holdSeconds = Mathf.Max(0f, hold);
            period = Mathf.Max(slamSeconds + holdSeconds + riseSeconds + warnSeconds + 0.1f, cyclePeriod);
            phaseOffset = offset;
            raised = transform.position;
            PlaceLandingMark();
        }

        /// <summary>
        /// Puts the landing mark on the landing spot (its authored place under the raised block, lowered
        /// by the drop). The first time, the mark leaves the block for the block's parent, so it stays on
        /// the spot without being moved every frame; it is destroyed with the crusher.
        /// </summary>
        void PlaceLandingMark() {
            if (landingMark == null) return;
            landingSpot = raised + (transform.TransformPoint(markRest) - transform.position) + Vector3.down * drop;
            if (landingMark.IsChildOf(transform)) landingMark.SetParent(transform.parent, true);
            landingMark.position = landingSpot;
        }

        void OnDestroy() {
            if (landingMark != null && !landingMark.IsChildOf(transform)) Destroy(landingMark.gameObject);
        }

        /// <summary>Seconds of the cycle spent fully raised (the safe window, warning included).</summary>
        public float OpenSeconds => period - slamSeconds - holdSeconds - riseSeconds;

        public Phase PhaseAt(double time, out float fraction) {
            float t = PlatformerClock.Cycle(time, period, -phaseOffset);
            float open = OpenSeconds;
            if (t < open) {
                fraction = t / open;
                return t < open - warnSeconds ? Phase.Waiting : Phase.Warning;
            }
            t -= open;
            if (t < slamSeconds) { fraction = t / slamSeconds; return Phase.Slamming; }
            t -= slamSeconds;
            if (t < holdSeconds) { fraction = t / holdSeconds; return Phase.Down; }
            fraction = (t - holdSeconds) / riseSeconds;
            return Phase.Rising;
        }

        /// <summary>Whether the block is slamming or held down (its crush zone is deadly) at a shared clock time.</summary>
        public bool IsDeadlyAt(double time) => PhaseAt(time, out _) is Phase.Slamming or Phase.Down;

        /// <summary>Where the block is at a shared clock time (the server judges a crush at the moment a client saw).</summary>
        public Vector3 PositionAt(double time) => raised + Vector3.down * DepthAt(time);

        /// <summary>How far below its raised pose the block is at a shared clock time.</summary>
        public float DepthAt(double time) {
            Phase phase = PhaseAt(time, out float f);
            return phase switch {
                Phase.Slamming => drop * f * f, // accelerates into the floor
                Phase.Down => drop,
                Phase.Rising => drop * (1f - f),
                _ => 0f
            };
        }

        void FixedUpdate() {
            double time = PlatformerClock.Now;
            Phase phase = PhaseAt(time, out _);
            body.MovePosition(PositionAt(time));
            bool deadly = phase is Phase.Slamming or Phase.Down;
            if (crushZone != null && crushZone.enabled != deadly) crushZone.enabled = deadly;
#if !UNITY_SERVER
            if (visual != null)
                visual.localPosition = phase == Phase.Warning
                    ? visualRest + new Vector3(Mathf.Sin((float)time * warnShakeFrequency.x), 0f, Mathf.Cos((float)time * warnShakeFrequency.y)) * warnShake
                    : visualRest;
            if (phase == Phase.Down && lastPhase == Phase.Slamming) Slammed?.Invoke(this);
#endif
            lastPhase = phase;
        }
    }
}
