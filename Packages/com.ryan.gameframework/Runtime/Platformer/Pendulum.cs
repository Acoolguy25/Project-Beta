using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A swinging hammer or axe: the body rocks about <see cref="localAxis"/> through the pivot
    /// (this object's origin) on the shared clock, so every peer sees the same swing. Put a
    /// <see cref="KnockbackSurface"/> on the head: it measures the swing and knocks characters it hits
    /// off the path (falls are then judged by the game's server like any other fall).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class Pendulum : MonoBehaviour {
        [Tooltip("Axis the arm swings about, in this object's space at rest.")]
        public Vector3 localAxis = Vector3.forward;
        [Tooltip("Largest angle from straight down (degrees).")]
        [Range(1f, 170f)] public float amplitude = 60f;
        [Tooltip("Seconds for a full swing there and back.")]
        [Min(0.2f)] public float period = 3f;
        [Tooltip("Seconds added to the shared clock, so neighbouring pendulums swing out of step.")]
        public float phaseOffset;
        [Tooltip("The arm, a unit-tall box scaled to the arm length and hung below the pivot.")]
        public Transform arm;
        [Tooltip("The head, hung at the end of the arm.")]
        public Transform head;

        Rigidbody body;
        Quaternion rest;

        void Awake() {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            rest = transform.rotation;
        }

        /// <summary>Sets up a pendulum spawned at runtime; the current rotation becomes the rest pose.</summary>
        public void Configure(float amplitudeDegrees, float swingPeriod, float offset, float armLength) {
            amplitude = Mathf.Clamp(amplitudeDegrees, 1f, 170f);
            period = Mathf.Max(0.2f, swingPeriod);
            phaseOffset = offset;
            rest = transform.rotation;
            SetLength(armLength);
        }

        /// <summary>Hangs the head <paramref name="length"/> below the pivot, stretching the arm to reach it.</summary>
        public void SetLength(float length) {
            length = Mathf.Max(0.1f, length);
            if (arm != null) {
                Vector3 scale = arm.localScale;
                arm.localScale = new Vector3(scale.x, length, scale.z);
                arm.localPosition = Vector3.down * (length * 0.5f);
            }
            if (head != null) head.localPosition = Vector3.down * length;
        }

        /// <summary>Swing angle (degrees from straight down) at a shared clock time.</summary>
        public float AngleAt(double time) =>
            amplitude * Mathf.Sin(PlatformerClock.Cycle(time, period, -phaseOffset) / period * Mathf.PI * 2f);

        void FixedUpdate() => body.MoveRotation(rest * Quaternion.AngleAxis(AngleAt(PlatformerClock.Now), localAxis));
    }
}
