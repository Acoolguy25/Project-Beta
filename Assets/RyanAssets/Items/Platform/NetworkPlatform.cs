using RyanAssets.Core;
using UnityEngine;
using RyanAssets.TweenService.TweenEasing;

namespace RyanAssets.Items.Platform {
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class NetworkPlatform : MonoBehaviour {

        [Header("Base Properties")]
        [SerializeField] private float movementSpeed = 2f; // Movement speed in meters per second
        [SerializeField] private float rotateSpeed = 30f; // Rotation speed in degrees per second

        [Space(10)]
        [Header("Delta Properties")]
        [SerializeField] private Vector3 deltaPosition = Vector3.forward;
        [SerializeField] private Vector3 deltaRotation = Vector3.zero;

        [Space(10)]
        [Header("Tween Properties")]
        [SerializeField] private bool reverseMovement = true;
        [SerializeField] private bool reverseRotation = false;

        private Rigidbody _rb;
        private Vector3 _startPosition, _startRotation;
        private float _movementHalfPeriod, _rotationHalfPeriod;
        private NetworkHelper.MotionClock _clock;
        private bool _placed;

        private bool movementEnabled => deltaPosition != Vector3.zero;
        private bool rotationEnabled => deltaRotation != Vector3.zero;

        private readonly EasingClass _movement_easing = new LinearEasing();
        private readonly EasingClass _rotation_easing = new LinearEasing();

        private void Awake() {
            _rb = GetComponent<Rigidbody>();
            _startPosition = _rb.position;
            _startRotation = _rb.rotation.eulerAngles;
            Precompute();
            // A deterministic platform must not be displaced by gravity or contacts.
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void OnEnable() => Update();

        private void OnDisable() {
            if (_clock != null)
                _clock.PhysicsStep -= MovePlatform;
            _clock = null;
            _placed = false;
        }

        private void Update() {
            var clock = NetworkHelper.GetMotionClock();
            if (ReferenceEquals(_clock, clock))
                return;
            if (_clock != null)
                _clock.PhysicsStep -= MovePlatform;
            _clock = clock;
            if (_clock != null)
                _clock.PhysicsStep += MovePlatform;
        }

        private void FixedUpdate() => _clock?.UnityFixedUpdate();

        private void MovePlatform(double time, double deltaTime) {
            Vector3 targetPosition = _startPosition + deltaPosition * Progress(time, _movementHalfPeriod, reverseMovement, _movement_easing);
            Quaternion targetRotation = RotationAt(time);
            if (!_placed || _clock.IsResynchronizing) {
                // A long pause is a placement at the current server phase. Never sweep
                // that distance in a single physics step or interpolate from the old pose.
                _rb.interpolation = RigidbodyInterpolation.None;
                _rb.position = targetPosition;
                _rb.rotation = targetRotation;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
                _placed = true;
                return;
            }

            // The shared clock already smooths small corrections. A second per-object
            // speed limiter prevents resynchronization and leaves clients on different phases.
            _rb.MovePosition(targetPosition);
            _rb.MoveRotation(targetRotation);
        }

        private Quaternion RotationAt(double time) {
            if (reverseRotation || _rotationHalfPeriod <= 0f)
                return Quaternion.Euler(_startRotation + deltaRotation * Progress(time, _rotationHalfPeriod, reverseRotation, _rotation_easing));

            // Continuous rotation must not jump back after an arbitrary Euler delta
            // (e.g. 90 degrees). Wrap each axis only after a complete revolution,
            // using double precision before converting to Unity's float angles.
            double turns = time / _rotationHalfPeriod;
            return Quaternion.Euler(_startRotation + new Vector3(
                (float)((deltaRotation.x * turns) % 360d),
                (float)((deltaRotation.y * turns) % 360d),
                (float)((deltaRotation.z * turns) % 360d)));
        }

        private void OnValidate() => Precompute();

        private void Precompute() {
            _movementHalfPeriod = movementEnabled && movementSpeed > 0f ? deltaPosition.magnitude / movementSpeed : 0f;
            _rotationHalfPeriod = rotationEnabled && rotateSpeed > 0f ? deltaRotation.magnitude / rotateSpeed : 0f;
        }

        private static float Progress(double time, float halfPeriod, bool reverse, EasingClass easing) {
            if (halfPeriod <= 0f)
                return 0f;

            double cycle = time % (halfPeriod * (reverse ? 2d : 1d));
            double fraction = cycle / halfPeriod;
            if (reverse && fraction > 1d)
                fraction = 2d - fraction;

            return easing.TransformValue((float)fraction);
        }
    }
}
