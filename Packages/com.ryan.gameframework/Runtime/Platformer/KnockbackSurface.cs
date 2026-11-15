using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A moving obstacle (spinner bar, swinging hammer) that knocks characters flying instead of
    /// killing them. It measures its own motion each physics step, so it works however it is moved,
    /// and a hit flings the character along the obstacle's velocity at the point of contact, plus a lift.
    /// Characters with <c>CharacterParkour</c> go limp on a hard enough hit.
    /// </summary>
    public sealed class KnockbackSurface : MonoBehaviour, IKnockbackSurface {
        [Tooltip("Multiplies the obstacle's speed at the contact point.")]
        [SerializeField, Min(0f)] private float strength = 1.15f;
        [Tooltip("Extra speed along the character's up (m/s), so hits pop characters off the floor.")]
        [SerializeField, Min(0f)] private float lift = 7f;
        [Tooltip("Hits slower than this (m/s at the contact point) do nothing special.")]
        [SerializeField, Min(0f)] private float minimumSpeed = 1.5f;
        [Tooltip("Fastest fling (m/s).")]
        [SerializeField, Min(0f)] private float maximumSpeed = 26f;

        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private Vector3 _velocity, _angularVelocity;
        private bool _tracking;

        private void OnEnable() => _tracking = false;

        private void FixedUpdate() {
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            if (_tracking) {
                float dt = Time.fixedDeltaTime;
                _velocity = (position - _lastPosition) / dt;
                (rotation * Quaternion.Inverse(_lastRotation)).ToAngleAxis(out float degrees, out Vector3 axis);
                if (degrees > 180f) degrees -= 360f;
                _angularVelocity = float.IsFinite(axis.x) ? axis * (degrees * Mathf.Deg2Rad / dt) : Vector3.zero;
            }
            _lastPosition = position;
            _lastRotation = rotation;
            _tracking = true;
        }

        /// <summary>Velocity of the obstacle's surface at <paramref name="point"/>.</summary>
        public Vector3 VelocityAt(Vector3 point) => _velocity + Vector3.Cross(_angularVelocity, point - transform.position);

        public Vector3 GetKnockback(Vector3 point, Vector3 up) {
            Vector3 hit = Vector3.ProjectOnPlane(VelocityAt(point), up);
            float speed = hit.magnitude;
            if (speed < minimumSpeed)
                return Vector3.zero;
            return Vector3.ClampMagnitude(hit * strength, maximumSpeed) + up * lift;
        }
    }
}
