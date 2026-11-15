using System;
using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Characters.Shared {
    /// <summary>
    /// The direction and strength of gravity for one character.
    /// <para>
    /// Gravity volumes (see <c>RyanAssets.Platformer.GravityZone</c>) submit requests every physics
    /// step while they contain the character; a request that is not refreshed expires, so leaving a
    /// volume, dying or being teleported out restores the default without exit callbacks.
    /// Movement, ground detection, collision handling, animation and the camera all read
    /// <see cref="Up"/> from here, so a character can stand, walk and jump on walls and ceilings.
    /// </para>
    /// <para>
    /// Only the owning client simulates the character, so only the owner calls <see cref="Resolve"/>;
    /// other peers see the result through the synchronised transform.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterGravity : MonoBehaviour {
        [Tooltip("Direction gravity pulls in when no volume overrides it (world space).")]
        [SerializeField] private Vector3 defaultDown = Vector3.down;

        /// <summary>World-space direction gravity currently pulls in (unit length).</summary>
        public Vector3 Down { get; private set; } = Vector3.down;
        /// <summary>World-space "up" for this character: the opposite of <see cref="Down"/>.</summary>
        public Vector3 Up => -Down;
        /// <summary>Multiplier on the character's gravity strength (1 = normal).</summary>
        public float Scale { get; private set; } = 1f;
        /// <summary>True while gravity points in its default direction.</summary>
        public bool IsDefaultDirection => Vector3.Dot(Down, DefaultDown) > 0.9999f;
        /// <summary>Raised by <see cref="Resolve"/> when <see cref="Down"/> changes; the argument is the previous down.</summary>
        public event Action<Vector3> DirectionChanged;

        private Vector3 DefaultDown => defaultDown.sqrMagnitude > 0.0001f ? defaultDown.normalized : Vector3.down;

        private struct GravityRequest {
            public Vector3? down;
            public float scale;
            public float expires;
            public int order;
        }

        private readonly Dictionary<object, GravityRequest> _requests = new();
        private readonly List<object> _expired = new();
        private int _nextOrder;

        private void Awake() => Down = DefaultDown;

        /// <summary>
        /// Requests gravity from <paramref name="source"/> until <paramref name="duration"/> seconds from now.
        /// A <paramref name="down"/> of null keeps the direction and only scales strength.
        /// When several sources overlap, the direction comes from the one applied most recently
        /// (so stepping into a new volume takes over), and the strength from the one furthest from 1.
        /// </summary>
        public void Request(object source, Vector3? down, float scale, float duration) {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            Vector3? direction = down.HasValue && down.Value.sqrMagnitude > 0.0001f ? down.Value.normalized : null;
            int order = _requests.TryGetValue(source, out GravityRequest existing) ? existing.order : _nextOrder++;
            _requests[source] = new GravityRequest {
                down = direction,
                scale = Mathf.Max(0f, scale),
                expires = Time.fixedTime + Mathf.Max(Time.fixedDeltaTime, duration),
                order = order
            };
        }

        /// <summary>Drops every request and returns to default gravity immediately.</summary>
        public void Clear() {
            _requests.Clear();
            Scale = 1f;
            SetDown(DefaultDown);
        }

        /// <summary>Expires stale requests and recomputes <see cref="Down"/> and <see cref="Scale"/>. Call once per physics step.</summary>
        public void Resolve() {
            float scale = 1f;
            Vector3 down = DefaultDown;
            int newestDirection = int.MinValue;
            foreach (var pair in _requests) {
                GravityRequest request = pair.Value;
                if (request.expires < Time.fixedTime) {
                    _expired.Add(pair.Key);
                    continue;
                }
                if (Mathf.Abs(request.scale - 1f) > Mathf.Abs(scale - 1f))
                    scale = request.scale;
                if (request.down.HasValue && request.order > newestDirection) {
                    newestDirection = request.order;
                    down = request.down.Value;
                }
            }
            foreach (object key in _expired)
                _requests.Remove(key);
            _expired.Clear();
            Scale = scale;
            SetDown(down);
        }

        private void SetDown(Vector3 down) {
            if (Vector3.Dot(down, Down) > 0.99999f)
                return;
            Vector3 previous = Down;
            Down = down;
            DirectionChanged?.Invoke(previous);
        }

        /// <summary>
        /// The shortest rotation from <paramref name="from"/> to <paramref name="to"/>, turning by at most
        /// <paramref name="maxDegrees"/>. Opposite directions (gravity flipped upside down) have no unique
        /// shortest turn, so those roll about <paramref name="rollAxis"/>; pass the way the character or camera
        /// faces so it keeps facing that way.
        /// </summary>
        public static Quaternion TurnToward(Vector3 from, Vector3 to, Vector3 rollAxis, float maxDegrees = 180f) {
            float angle = Vector3.Angle(from, to);
            if (angle < 0.001f)
                return Quaternion.identity;
            Vector3 axis = Vector3.Cross(from, to);
            if (axis.sqrMagnitude < 0.000001f) {
                axis = Vector3.ProjectOnPlane(rollAxis, from);
                if (axis.sqrMagnitude < 0.000001f)
                    axis = Vector3.Cross(from, Mathf.Abs(from.x) < 0.9f ? Vector3.right : Vector3.forward);
            }
            return Quaternion.AngleAxis(Mathf.Min(angle, maxDegrees), axis.normalized);
        }

        /// <summary>The gravity "up" of <paramref name="character"/>, or world up when it has no <see cref="CharacterGravity"/>.</summary>
        public static Vector3 UpOf(Component character) =>
            character != null && character.TryGetComponent(out CharacterGravity gravity) ? gravity.Up : Vector3.up;
    }
}
