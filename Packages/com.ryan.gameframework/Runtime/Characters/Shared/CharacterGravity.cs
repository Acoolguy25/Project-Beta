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
    /// </para>
    /// <para>
    /// Two directions are kept. <see cref="TargetDown"/> is the one the rules have decided on: it changes
    /// at once (after a short debounce, so brushing a volume's edge never flickers) and is what game logic
    /// should compare against. <see cref="Down"/> and <see cref="Up"/> are the smoothed frame: they ease
    /// toward the target over <see cref="BlendSeconds"/>, and movement, ground detection, collision handling,
    /// animation and the camera all read them, so a character turns over to stand on a wall or ceiling
    /// instead of snapping. With default gravity the two are always identical.
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

        [Header("Smoothing")]
        [Tooltip("Seconds the up direction takes to ease over a quarter turn (a half turn takes 1.5x, a small change less). 0 = snap.")]
        [SerializeField, Min(0f)] private float blendSeconds = 0.25f;
        [Tooltip("A new direction must be asked for steadily this long (s) before it is accepted, so brushing a volume's edge does not flicker.")]
        [SerializeField, Min(0f)] private float confirmSeconds = 0.06f;
        [Tooltip("Minimum seconds between two direction changes. Chained flip pads or a character sliding along a volume's edge settle on one direction.")]
        [SerializeField, Min(0f)] private float holdSeconds = 0.35f;

        /// <summary>World-space direction gravity pulls in right now: the smoothed frame, easing toward <see cref="TargetDown"/>.</summary>
        public Vector3 Down => -Up;
        /// <summary>World-space "up" for this character (smoothed): the opposite of <see cref="Down"/>.</summary>
        public Vector3 Up {
            get {
                if (!_blending)
                    return _blendTo;
                float now = Time.time;
                if (now != _cacheTime) {
                    _cacheTime = now;
                    _cacheUp = GravityBlend.Up(_blendFrom, _blendTo, (now - _blendStart) / _blendDuration, _rollAxis);
                    if (now - _blendStart >= _blendDuration)
                        _blending = false;
                }
                return _cacheUp;
            }
        }
        /// <summary>The direction gravity has been decided to pull in (instant, never smoothed). Compare against this in game rules.</summary>
        public Vector3 TargetDown { get; private set; } = Vector3.down;
        /// <summary>The opposite of <see cref="TargetDown"/>.</summary>
        public Vector3 TargetUp => -TargetDown;
        /// <summary>Multiplier on the character's gravity strength (1 = normal).</summary>
        public float Scale { get; private set; } = 1f;
        /// <summary>True while the decided direction is the default one.</summary>
        public bool IsDefaultDirection => Vector3.Dot(TargetDown, DefaultDown) > 0.9999f;
        /// <summary>True while <see cref="Up"/> is still easing toward <see cref="TargetUp"/>.</summary>
        public bool IsBlending => _blending && Time.time - _blendStart < _blendDuration;
        /// <summary>Seconds <see cref="Up"/> takes to ease over a quarter turn.</summary>
        public float BlendSeconds => blendSeconds;
        /// <summary>Raised by <see cref="Resolve"/> when <see cref="TargetDown"/> changes; the argument is the previous target down.</summary>
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

        private GravityBlend.SwitchFilter _filter;
        private bool _blending;
        private Vector3 _blendFrom = Vector3.up, _blendTo = Vector3.up, _rollAxis = Vector3.forward;
        private float _blendStart, _blendDuration = 1f;
        private float _cacheTime = float.NaN;
        private Vector3 _cacheUp = Vector3.up;

        private void Awake() {
            TargetDown = DefaultDown;
            _blendFrom = _blendTo = _cacheUp = -TargetDown;
            _filter.Reset(TargetDown, Time.fixedTime);
        }

        /// <summary>
        /// Requests gravity from <paramref name="source"/> until <paramref name="duration"/> seconds from now.
        /// A <paramref name="down"/> of null keeps the direction and only scales strength.
        /// When several sources overlap, the direction comes from the one whose direction was set most
        /// recently (stepping into a new volume, or a pad asking for a different direction, takes over),
        /// and the strength from the one furthest from 1.
        /// </summary>
        public void Request(object source, Vector3? down, float scale, float duration) {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            Vector3? direction = down.HasValue && down.Value.sqrMagnitude > 0.0001f ? down.Value.normalized : null;
            int order;
            if (_requests.TryGetValue(source, out GravityRequest existing)
                && SameDirection(existing.down, direction))
                order = existing.order;
            else
                order = _nextOrder++;
            _requests[source] = new GravityRequest {
                down = direction,
                scale = Mathf.Max(0f, scale),
                expires = Time.fixedTime + Mathf.Max(Time.fixedDeltaTime, duration),
                order = order
            };
        }

        private static bool SameDirection(Vector3? a, Vector3? b) =>
            a.HasValue == b.HasValue && (!a.HasValue || Vector3.Dot(a.Value, b.Value) > 0.99999f);

        /// <summary>Drops every request and returns to default gravity at once, with no easing (respawns and teleports).</summary>
        public void Clear() {
            _requests.Clear();
            Scale = 1f;
            Vector3 previous = TargetDown;
            TargetDown = DefaultDown;
            _filter.Reset(TargetDown, Time.fixedTime);
            _blending = false;
            _blendFrom = _blendTo = _cacheUp = -TargetDown;
            _cacheTime = float.NaN;
            if (Vector3.Dot(previous, TargetDown) <= 0.99999f)
                DirectionChanged?.Invoke(previous);
        }

        /// <summary>Expires stale requests and recomputes <see cref="TargetDown"/>, <see cref="Scale"/> and the smoothed frame. Call once per physics step.</summary>
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
            if (_filter.Update(down, Time.fixedTime, confirmSeconds, holdSeconds))
                SetTarget(_filter.Accepted);
        }

        private void SetTarget(Vector3 down) {
            Vector3 previous = TargetDown;
            Vector3 currentUp = Up;
            TargetDown = down;
            Vector3 targetUp = -down;
            float angle = Vector3.Angle(currentUp, targetUp);
            if (blendSeconds <= 0f || angle < 0.01f) {
                _blending = false;
                _blendFrom = _blendTo = _cacheUp = targetUp;
                _cacheTime = float.NaN;
            } else {
                // Roll about the way the character faces, so a flip upside down keeps it facing the same way.
                _rollAxis = transform.forward;
                _blendFrom = currentUp;
                _blendTo = targetUp;
                _blendStart = Time.time;
                _blendDuration = blendSeconds * Mathf.Clamp(angle / 90f, 0.5f, 1.5f);
                _blending = true;
                _cacheTime = float.NaN;
            }
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
