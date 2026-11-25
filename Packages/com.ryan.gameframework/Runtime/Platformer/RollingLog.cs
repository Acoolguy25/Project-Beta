using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A log spinning about <see cref="localAxis"/>. Its surface carries whoever stands on it along the
    /// spin, so a log lying along the path pushes a walker sideways and has to be balanced across, and
    /// one lying across the path is a treadmill. The collider stays still (put a box along the log's
    /// upper face on this object); only <see cref="spinVisual"/> turns, on the shared clock.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RollingLog : MonoBehaviour, ISurfaceVelocity {
        [Tooltip("Spin axis in this object's space.")]
        public Vector3 localAxis = Vector3.forward;
        [Tooltip("Speed of the log's surface (m/s); the sign picks the spin direction.")]
        public float surfaceSpeed = 3f;
        [Tooltip("Radius of the drawn log (m), to turn the surface speed into a spin rate.")]
        [Min(0.1f)] public float radius = 1.5f;
        [Tooltip("Child that spins (no collider).")]
        public Transform spinVisual;

        public void Configure(float speed, float logRadius) {
            surfaceSpeed = speed;
            radius = Mathf.Max(0.1f, logRadius);
        }

        Vector3 WorldAxis => transform.TransformDirection(localAxis).normalized;

        public Vector3 GetSurfaceVelocity(Vector3 characterPosition) {
            Vector3 axis = WorldAxis;
            Vector3 offset = Vector3.ProjectOnPlane(characterPosition - transform.position, axis);
            if (offset.sqrMagnitude < 0.0001f) return Vector3.zero;
            return Vector3.Cross(axis, offset.normalized) * surfaceSpeed;
        }

#if !UNITY_SERVER
        void Update() {
            if (spinVisual == null) return;
            float degrees = (float)(PlatformerClock.Now * surfaceSpeed / radius * Mathf.Rad2Deg % 360d);
            spinVisual.localRotation = Quaternion.AngleAxis(degrees, localAxis);
        }
#endif
    }
}
