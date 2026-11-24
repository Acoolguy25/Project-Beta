#if !UNITY_SERVER
using RyanAssets.Characters.Client;
#endif
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A gust field: while the local character is inside this trigger it is pushed along
    /// <see cref="localDirection"/> at <see cref="speed"/>, on the ground and in the air. Gusts swell
    /// and ease on the shared clock (<see cref="PlatformerClock"/>), so every racer feels the same
    /// wind at the same moment. Optional streak particles show which way it blows.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WindVolume : MonoBehaviour {
        [Tooltip("Direction the wind blows, in this object's space.")]
        public Vector3 localDirection = Vector3.right;
        [Tooltip("Steady push (m/s) at the calm point of a gust.")]
        [Min(0f)] public float speed = 3f;
        [Tooltip("How much stronger the wind is at the height of a gust, as a share of speed (0 = steady).")]
        [Range(0f, 1f)] public float gustStrength = 0.4f;
        [Tooltip("Seconds from one gust to the next (0 = steady).")]
        [Min(0f)] public float gustPeriod = 3f;
        [Tooltip("Offset into the gust cycle, so neighbouring fields can gust in turn.")]
        public float gustPhase;
        [Tooltip("Optional streak particles; their speed and box follow the field.")]
        public ParticleSystem streaks;
        [Tooltip("Streaks emitted per second per cubic metre of field.")]
        [Min(0f)] public float streakDensity = 0.12f;
        [Tooltip("Most streaks emitted per second, however big the field.")]
        [Min(1f)] public float maxStreakRate = 90f;
        [Tooltip("Streaks fly this many times the wind speed, so the direction reads at a glance.")]
        [Min(0f)] public float streakSpeedScale = 2.5f;

        public Vector3 WorldDirection => transform.TransformDirection(localDirection).normalized;

        /// <summary>Wind speed at <paramref name="time"/> on the shared clock.</summary>
        public float SpeedAt(double time) {
            if (gustPeriod <= 0f || gustStrength <= 0f) return speed;
            float t = PlatformerClock.Cycle(time, gustPeriod, gustPhase) / gustPeriod;
            // 0 at the calm point, 1 at the height of the gust.
            float gust = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f);
            return speed * (1f + gustStrength * gust);
        }

        public void Configure(Vector3 direction, float windSpeed, float gust, float period, float phase) {
            localDirection = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.right;
            speed = Mathf.Max(0f, windSpeed);
            gustStrength = Mathf.Clamp01(gust);
            gustPeriod = Mathf.Max(0f, period);
            gustPhase = phase;
#if !UNITY_SERVER
            FitStreaks();
#endif
        }

        /// <summary>Sizes the field: its box trigger (centred on this object) and the streaks' emitter.</summary>
        public void Resize(Vector3 size) {
            if (TryGetComponent(out BoxCollider box)) {
                box.isTrigger = true;
                box.center = Vector3.zero;
                box.size = size;
            }
#if !UNITY_SERVER
            if (streaks == null) return;
            var shape = streaks.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = size;
            var emission = streaks.emission;
            emission.rateOverTime = Mathf.Clamp(size.x * size.y * size.z * streakDensity, 4f, maxStreakRate);
#endif
        }

#if !UNITY_SERVER
        /// <summary>Raised on the client when the local character enters a wind field (games add the sound).</summary>
        public static event System.Action<WindVolume> Entered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Entered = null;

        void Start() => FitStreaks();

        void OnTriggerEnter(Collider other) {
            if (LocalBody.TryGet(other, out _)) Entered?.Invoke(this);
        }

        void OnTriggerStay(Collider other) {
            if (!LocalBody.TryGet(other, out CharacterMovement movement)) return;
            movement.AddPush(WorldDirection * SpeedAt(PlatformerClock.Now));
        }

        /// <summary>Sends the streaks down the wind (in this object's space).</summary>
        void FitStreaks() {
            if (streaks == null) return;
            var velocity = streaks.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            Vector3 v = localDirection.normalized * speed * streakSpeedScale;
            velocity.x = new ParticleSystem.MinMaxCurve(v.x);
            velocity.y = new ParticleSystem.MinMaxCurve(v.y);
            velocity.z = new ParticleSystem.MinMaxCurve(v.z);
        }
#endif
    }
}
