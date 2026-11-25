using UnityEngine;

namespace RyanAssets.Characters.Shared {
    /// <summary>
    /// The pure maths behind smooth gravity changes (no scene, no time source), so it can be unit tested:
    /// how "up" eases from one direction to another, how a velocity is carried across the turn, and
    /// the debounce that stops gravity flickering at the edge of a volume.
    /// </summary>
    public static class GravityBlend {
        /// <summary>Ease for a blend: 0 at <paramref name="t"/> = 0, 1 at 1, with no speed at either end so a turn never starts or stops with a pop.</summary>
        public static float Ease(float t) {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// The "up" a blend from <paramref name="from"/> to <paramref name="to"/> has reached after
        /// <paramref name="t"/> (0..1 of the blend time). Exactly <paramref name="to"/> at 1. Opposite
        /// directions turn about <paramref name="rollAxis"/> (see <see cref="CharacterGravity.TurnToward"/>).
        /// </summary>
        public static Vector3 Up(Vector3 from, Vector3 to, float t, Vector3 rollAxis) {
            if (t >= 1f)
                return to;
            if (t <= 0f)
                return from;
            Quaternion full = CharacterGravity.TurnToward(from, to, rollAxis);
            return (Quaternion.Slerp(Quaternion.identity, full, Ease(t)) * from).normalized;
        }

        /// <summary>
        /// Carries <paramref name="velocity"/> across a turn of "up" from <paramref name="previousUp"/> to
        /// <paramref name="up"/>. The part that was planar (running, sliding) turns with the frame so the
        /// character keeps running the same way on its new surface instead of ploughing into it; the part
        /// along the old up (a jump or fall) keeps its world direction, so momentum is conserved.
        /// </summary>
        public static Vector3 Reproject(Vector3 velocity, Vector3 previousUp, Vector3 up, Vector3 rollAxis) {
            if (previousUp == up)
                return velocity;
            float along = Vector3.Dot(velocity, previousUp);
            Vector3 planar = velocity - previousUp * along;
            Quaternion turn = CharacterGravity.TurnToward(previousUp, up, rollAxis);
            return turn * planar + previousUp * along;
        }

        /// <summary>
        /// Debounces gravity direction changes. A new direction must be asked for steadily for
        /// <c>confirmSeconds</c>, and no earlier than <c>holdSeconds</c> after the previous change, before it
        /// is accepted. A character brushing the edge of a volume, or two flip pads close together, therefore
        /// settles on one direction instead of flickering between two.
        /// </summary>
        public struct SwitchFilter {
            private Vector3 _accepted;
            private Vector3 _pending;
            private bool _hasPending;
            private float _pendingSince;
            private float _acceptedAt;
            private bool _initialised;

            /// <summary>The direction currently in force.</summary>
            public Vector3 Accepted => _accepted;
            /// <summary>True while a different direction is waiting to be confirmed.</summary>
            public bool HasPending => _hasPending;

            /// <summary>Starts in <paramref name="direction"/> with nothing pending.</summary>
            public void Reset(Vector3 direction, float now) {
                _accepted = direction;
                _hasPending = false;
                _acceptedAt = float.NegativeInfinity;
                _pendingSince = now;
                _initialised = true;
            }

            /// <summary>
            /// Feeds the direction asked for at <paramref name="now"/>; returns true when the accepted
            /// direction changed (it is then <see cref="Accepted"/>).
            /// </summary>
            public bool Update(Vector3 requested, float now, float confirmSeconds, float holdSeconds) {
                if (!_initialised)
                    Reset(requested, now);
                if (Vector3.Dot(requested, _accepted) > 0.99999f) {
                    _hasPending = false;
                    return false;
                }
                if (!_hasPending || Vector3.Dot(requested, _pending) <= 0.99999f) {
                    _pending = requested;
                    _pendingSince = now;
                    _hasPending = true;
                }
                if (now - _pendingSince < confirmSeconds || now - _acceptedAt < holdSeconds)
                    return false;
                _accepted = _pending;
                _acceptedAt = now;
                _hasPending = false;
                return true;
            }
        }
    }
}
