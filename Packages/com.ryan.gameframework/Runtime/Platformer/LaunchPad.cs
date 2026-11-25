#if !UNITY_SERVER
using RyanAssets.Characters.Client;
#endif
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Springs and boost panels. A trigger volume on the pad launches the local character with
    /// <see cref="localLaunchVelocity"/> (pad space, so a rotated pad launches sideways).
    /// The vertical part replaces the character's vertical speed; the horizontal part becomes
    /// momentum that fades while the player keeps steering.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LaunchPad : MonoBehaviour {
        [Tooltip("Launch velocity in this pad's local space. A spring is (0, 20, 0); a boost panel adds forward Z.")]
        public Vector3 localLaunchVelocity = new(0f, 20f, 0f);
        [Min(0f)] public float cooldown = 0.25f;
        [Tooltip("Optional visual that stretches when the pad fires.")]
        public Transform stretchVisual;
        [Min(0f)] public float stretchAmount = 0.6f;
        public AudioSource launchAudio;
        [Tooltip("Optional visual (a cannon barrel) turned so its +Z points along the launch velocity.")]
        public Transform aimVisual;

        public Vector3 WorldLaunchVelocity => transform.TransformDirection(localLaunchVelocity);

        public void Configure(Vector3 localVelocity) {
            localLaunchVelocity = localVelocity;
            if (aimVisual != null && localVelocity.sqrMagnitude > 0.0001f)
                aimVisual.localRotation = Quaternion.LookRotation(localVelocity.normalized,
                    Mathf.Abs(localVelocity.normalized.y) > 0.99f ? Vector3.forward : Vector3.up);
        }

#if !UNITY_SERVER
        /// <summary>
        /// Raised on the client right after a pad launches the local character (games add their own
        /// sound, particles or stats). Client only: not compiled into servers.
        /// </summary>
        public static event System.Action<LaunchPad, CharacterMovement> Launched;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Launched = null;

        const float StretchSeconds = 0.25f;
        float nextLaunch, stretchUntil = float.NegativeInfinity;
        Vector3 visualScale;

        void Awake() {
            if (stretchVisual != null) visualScale = stretchVisual.localScale;
        }

        void OnTriggerStay(Collider other) {
            if (Time.time < nextLaunch || !LocalBody.TryGet(other, out CharacterMovement movement)) return;
            // Mid pull-up or ragdolled: the body is kinematic and cannot be launched; try again once it is free.
            if (movement.Body == null || movement.Body.isKinematic) return;
            Vector3 velocity = WorldLaunchVelocity;
            // A character already rising fast from this pad must not be relaunched every step.
            float launchRise = Vector3.Dot(velocity, movement.Up);
            if (launchRise > 0f && Vector3.Dot(movement.Body.linearVelocity, movement.Up) > launchRise * 0.5f) return;
            nextLaunch = Time.time + cooldown;
            movement.Launch(velocity);
            stretchUntil = Time.time + StretchSeconds;
            if (launchAudio != null) launchAudio.Play();
            Launched?.Invoke(this, movement);
        }

        void Update() {
            if (stretchVisual == null) return;
            float t = Mathf.Clamp01((stretchUntil - Time.time) / StretchSeconds);
            float pulse = Mathf.Sin(t * Mathf.PI);
            stretchVisual.localScale = new Vector3(visualScale.x * (1f - 0.15f * pulse),
                visualScale.y * (1f + stretchAmount * pulse), visualScale.z * (1f - 0.15f * pulse));
        }
#endif
    }
}
