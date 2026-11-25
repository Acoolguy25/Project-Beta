#if !UNITY_SERVER
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
#endif
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Lava that starts to rise once the local character is over its footprint and chases them up a
    /// climb. Each player has their own chase (it starts when they arrive and restarts when they are
    /// teleported), so on clients this component only draws the local player's lava. It is not a
    /// <see cref="HazardVolume"/>: the game's server runs the same chase per player from
    /// <see cref="Rise"/> and its own record of when each player arrived.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RisingLava : MonoBehaviour {
        [Tooltip("Rise speed (m/s).")]
        [Min(0f)] public float riseSpeed = 1.5f;
        [Tooltip("Seconds after the chase starts before the lava moves.")]
        [Min(0f)] public float delay = 1f;
        [Tooltip("Highest the lava rises above where it starts (m).")]
        [Min(0f)] public float travel = 15f;
        [Tooltip("Footprint (this object's local X and Z, centred on it) the character must be over to start the chase.")]
        public Vector2 footprint = new(10f, 20f);
        [Tooltip("The lava body that moves up (its rest pose is the start).")]
        public Transform surface;

        /// <summary>Metres the lava has risen <paramref name="elapsed"/> seconds after a chase started.</summary>
        public static float Rise(float elapsed, float delay, float speed, float travel) =>
            Mathf.Clamp((elapsed - delay) * speed, 0f, travel);

        /// <summary>Whether <paramref name="point"/> is over a footprint centred at <paramref name="centre"/>, turned by <paramref name="yaw"/>.</summary>
        public static bool IsOver(Vector3 point, Vector3 centre, float yaw, Vector2 footprint) {
            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f)) * (point - centre);
            return Mathf.Abs(local.x) <= footprint.x * 0.5f && Mathf.Abs(local.z) <= footprint.y * 0.5f;
        }

        public void Configure(float speed, float startDelay, float maxRise, Vector2 area) {
            riseSpeed = Mathf.Max(0f, speed);
            delay = Mathf.Max(0f, startDelay);
            travel = Mathf.Max(0f, maxRise);
            footprint = area;
        }

#if !UNITY_SERVER
        /// <summary>Raised on the client when the local player's chase starts.</summary>
        public static event System.Action<RisingLava> ChaseStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ChaseStarted = null;

        float startedAt = -1f;
        Vector3 surfaceRest;

        /// <summary>Metres risen for the local player (0 before their chase starts).</summary>
        public float CurrentRise => startedAt < 0f ? 0f : Rise(Time.time - startedAt, delay, riseSpeed, travel);

        void Awake() {
            if (surface != null) surfaceRest = surface.localPosition;
        }

        void OnEnable() => LocalCharacter.Teleported += OnTeleported;

        void OnDisable() => LocalCharacter.Teleported -= OnTeleported;

        void OnTeleported(LocalCharacter character) {
            if (character == LocalPlayer.Character) startedAt = -1f;
        }

        void FixedUpdate() {
            if (startedAt >= 0f) return;
            var player = CharacterMovement.Local;
            if (player == null || player.Body == null) return;
            if (!IsOver(player.Body.position, transform.position, transform.eulerAngles.y, footprint)) return;
            startedAt = Time.time;
            ChaseStarted?.Invoke(this);
        }

        void Update() {
            if (surface != null) surface.localPosition = surfaceRest + Vector3.up * (CurrentRise / Mathf.Max(0.0001f, transform.lossyScale.y));
        }
#endif
    }
}
