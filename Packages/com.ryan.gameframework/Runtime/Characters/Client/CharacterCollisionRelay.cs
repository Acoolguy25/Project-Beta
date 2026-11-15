using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.Characters.Client {
    /// <summary>
    /// Receives collision callbacks on the character Rigidbody for <see cref="CharacterMovement"/>,
    /// which lives on the client object and cannot receive them directly. It
    /// <list type="bullet">
    /// <item>reports touched wall-jump surfaces, and</item>
    /// <item>removes the upward velocity the solver creates when the character runs into a wall.</item>
    /// </list>
    /// "Up" and "wall" are measured against the character's gravity, so both work on any surface.
    /// </summary>
    public sealed class CharacterCollisionRelay : MonoBehaviour {
        [Tooltip("Contacts whose normal points less than this far along up (dot product) count as walls.")]
        [SerializeField, Range(0f, 1f)] private float maximumWallUpDot = 0.35f;
        [Tooltip("Impacts slower than this (m/s) are treated as resting beside a wall, not running into it.")]
        [SerializeField, Min(0f)] private float minimumIntoWallSpeed = 0.01f;

        private Rigidbody _rigidbody;
        private CharacterMovement _movement;
        private Collider _lastWallCollider;
        private IWallJumpSurface _lastWallSurface;

        public void Initialize(Rigidbody body, CharacterMovement movement) {
            _rigidbody = body;
            _movement = movement;
        }

        private void OnCollisionEnter(Collision collision) => HandleCollision(collision);
        private void OnCollisionStay(Collision collision) => HandleCollision(collision);

        private void HandleCollision(Collision collision) {
            if (_movement == null || _rigidbody == null)
                return;
            Vector3 up = _movement.Up;
            if (!ReportWallJumpSurface(collision, up))
                StabilizeWallCollision(collision, up);
        }

        /// <summary>
        /// Tells movement about a touched wall-jump surface. Those walls are exempt from the upward
        /// velocity stabilization below, which would otherwise cancel every kick up a shaft.
        /// </summary>
        private bool ReportWallJumpSurface(Collision collision, Vector3 up) {
            if (collision.collider != _lastWallCollider) {
                _lastWallCollider = collision.collider;
                _lastWallSurface = collision.collider != null ? collision.collider.GetComponentInParent<IWallJumpSurface>() : null;
            }
            if (_lastWallSurface == null)
                return false;
            for (int i = 0; i < collision.contactCount; i++) {
                Vector3 normal = collision.GetContact(i).normal;
                if (Mathf.Abs(Vector3.Dot(normal, up)) <= maximumWallUpDot) {
                    _movement.ReportWallContact(normal, _lastWallSurface);
                    break;
                }
            }
            return true;
        }

        private void StabilizeWallCollision(Collision collision, Vector3 up) {
            float risingSpeed = Vector3.Dot(_rigidbody.linearVelocity, up);
            if (risingSpeed <= 0f || _movement.IsJumpVelocityProtected)
                return;

            // The solver may already have removed the into-wall portion from the Rigidbody by
            // the time this callback runs. Collision.relativeVelocity retains the impact that
            // produced the contact, which lets us distinguish a wall hit from merely resting
            // beside a wall.
            Vector3 planarImpactVelocity = Vector3.ProjectOnPlane(collision.relativeVelocity, up);
            if (planarImpactVelocity.sqrMagnitude < minimumIntoWallSpeed * minimumIntoWallSpeed)
                return;
            for (int i = 0; i < collision.contactCount; i++) {
                Vector3 normal = collision.GetContact(i).normal;
                if (Vector3.Dot(normal, up) > maximumWallUpDot)
                    continue;
                if (Vector3.ProjectOnPlane(normal, up).sqrMagnitude < 0.001f)
                    continue;
                _rigidbody.linearVelocity -= up * risingSpeed;
                return;
            }
        }
    }
}
