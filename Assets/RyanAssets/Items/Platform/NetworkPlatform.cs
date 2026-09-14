using FishNet;
using UnityEngine;
using RyanAssets.TweenService.TweenEasing;

namespace RyanAssets.Items.Platform {
    [RequireComponent(typeof(Rigidbody))]
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

        [Header("Clock Synchronization")]
        [Tooltip("Maximum fractional speed adjustment while correcting network clock drift. Keeps time moving forward without snapping.")]
        [SerializeField, Range(0.01f, 0.5f)] private float clockCorrectionRate = 0.1f;

        private Rigidbody _rb;
        private Vector3 _startPosition, _startRotation;
        private float _movementHalfPeriod, _rotationHalfPeriod;
        private double _motionTime;
        private bool _clockInitialized;

        private bool movementEnabled => deltaPosition != Vector3.zero;
        private bool rotationEnabled => deltaRotation != Vector3.zero;

        private readonly EasingClass _movement_easing = new LinearEasing();
        private readonly EasingClass _rotation_easing = new LinearEasing();

        private void Awake() {
            _rb = GetComponent<Rigidbody>();
            _startPosition = _rb.position;
            _startRotation = _rb.rotation.eulerAngles;
            Precompute();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void FixedUpdate() {
            // FishNet's estimated server Tick can jump in either direction. Seed once,
            // then slew a local physics clock toward it instead of teleporting the pose.
            var timeManager = InstanceFinder.TimeManager;
            if (timeManager == null || (!InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted)) {
                _clockInitialized = false;
                return;
            }

            double networkTime = timeManager.TicksToTime(timeManager.Tick) + timeManager.GetTickElapsedAsDouble();
            if (!_clockInitialized) {
                _motionTime = networkTime;
                _clockInitialized = true;
                // Initial alignment is a placement, not a physics move across the route.
                _rb.position = _startPosition + deltaPosition * Progress(_motionTime, _movementHalfPeriod, reverseMovement, _movement_easing);
                _rb.rotation = Quaternion.Euler(_startRotation + deltaRotation * Progress(_motionTime, _rotationHalfPeriod, reverseRotation, _rotation_easing));
                return;
            }

            _motionTime = AdvanceClock(_motionTime, networkTime, Time.fixedDeltaTime, clockCorrectionRate);

            float movementPercentage = Progress(_motionTime, _movementHalfPeriod, reverseMovement, _movement_easing);
            float rotationPercentage = Progress(_motionTime, _rotationHalfPeriod, reverseRotation, _rotation_easing);

            Vector3 targetPosition = _startPosition + deltaPosition * movementPercentage;
            Quaternion targetRotation = Quaternion.Euler(_startRotation + deltaRotation * rotationPercentage);

            _rb.MovePosition(targetPosition);
            _rb.MoveRotation(targetRotation);
        }

        private static double AdvanceClock(double currentTime, double networkTime, double deltaTime, float correctionRate) {
            double nextTime = currentTime + deltaTime;
            double maxCorrection = deltaTime * Mathf.Clamp(correctionRate, 0.01f, 0.5f);
            return nextTime + System.Math.Max(-maxCorrection, System.Math.Min(maxCorrection, networkTime - nextTime));
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
