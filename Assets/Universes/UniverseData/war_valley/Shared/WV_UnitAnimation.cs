using FishNet.Object;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Presentation for a unit: locomotion animation for infantry, spinning rotors for rotorcraft,
    /// and a muzzle flash when the unit fires.
    /// <para>
    /// Speed is derived from the transform rather than from the NavMeshAgent, because on a client the
    /// agent does not exist - the unit is moved by its NetworkTransform. One derivation therefore
    /// drives both builds and cannot desync from what the player actually sees.
    /// </para>
    /// </summary>
    public sealed class WV_UnitAnimation : NetworkBehaviour {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        static readonly int GunHoldId = Animator.StringToHash("GunHold");
        static readonly int GunFireId = Animator.StringToHash("GunFire");

        [Header("Infantry")]
        [Tooltip("Optional. Locomotion animator on the unit's rig.")]
        [SerializeField] Animator animator;
        [Tooltip("Smoothing applied to the derived speed so the blend tree does not jitter.")]
        [SerializeField, Min(0.01f)] float speedSmoothing = 0.15f;

        [Header("Rotorcraft")]
        [Tooltip("Optional. Rotors and propellers spun continuously while the unit is alive.")]
        [SerializeField] Transform[] rotors = System.Array.Empty<Transform>();
        [SerializeField] Vector3 rotorAxis = Vector3.up;
        [SerializeField] float rotorDegreesPerSecond = 1440f;

        [Header("Weapon")]
        [Tooltip("Optional. Particle effect played at the muzzle on each shot.")]
        [SerializeField] ParticleSystem muzzleFlash;

        // GunFire is a bool on the shared robot controller rather than a trigger, so it has to be
        // released explicitly instead of being consumed by the state machine.
        const float GunFireHoldSeconds = 0.12f;

        Vector3 lastPosition;
        float smoothedSpeed;
        float gunFireReleaseTime;
        bool hasAnimator;

        void Awake() {
            hasAnimator = animator != null;
            lastPosition = transform.position;
            if (hasAnimator) {
                animator.SetBool(GroundedId, true);
                animator.SetFloat(MotionSpeedId, 1f);
                animator.SetBool(GunHoldId, true);
            }
        }

        void Update() {
            float delta = Time.deltaTime;
            if (delta <= 0f)
                return;

            SpinRotors(delta);

            if (!hasAnimator)
                return;

            if (gunFireReleaseTime > 0f && Time.time >= gunFireReleaseTime) {
                gunFireReleaseTime = 0f;
                animator.SetBool(GunFireId, false);
            }

            Vector3 position = transform.position;
            Vector3 movement = position - lastPosition;
            movement.y = 0f;
            lastPosition = position;

            float rawSpeed = movement.magnitude / delta;
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, rawSpeed, delta / speedSmoothing);
            animator.SetFloat(SpeedId, smoothedSpeed);
        }

        void SpinRotors(float delta) {
            if (rotors.Length == 0)
                return;

            float step = rotorDegreesPerSecond * delta;
            foreach (Transform rotor in rotors) {
                if (rotor != null)
                    rotor.Rotate(rotorAxis, step, Space.Self);
            }
        }

        /// <summary>Called on the server when the unit fires; mirrored to observers for presentation.</summary>
        [Server]
        public void PlayAttack() {
            PlayAttackObservers();
        }

        [ObserversRpc(RunLocally = true, BufferLast = false)]
        void PlayAttackObservers() {
            if (hasAnimator) {
                animator.SetBool(GunFireId, true);
                gunFireReleaseTime = Time.time + GunFireHoldSeconds;
            }
            if (muzzleFlash != null)
                muzzleFlash.Play();
        }
    }
}
