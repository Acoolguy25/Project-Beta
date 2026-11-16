using System;
using FishNet.Object;
using RyanAssets.Shared.Combat;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Presentation for a unit: locomotion animation for infantry, spinning rotors for rotorcraft,
    /// and a muzzle flash, tracer, impact, and report when the unit fires.
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
        [Tooltip("Optional. Spatial settings the report is played with.")]
        [SerializeField] AudioSource audioSource;
        [Tooltip("Optional. The weapon's report.")]
        [SerializeField] AudioClip fireAudio;

        /// <summary>
        /// Raised on each client when a unit's shot is shown, with the muzzle and the point it struck,
        /// so a game's client layer can add its own reaction (an explosion for a shell) on top.
        /// </summary>
        public static event Action<WV_UnitAnimation, Vector3, Vector3> ShotShown;

        // GunFire is a bool on the shared robot controller rather than a trigger, so it has to be
        // released explicitly instead of being consumed by the state machine.
        const float GunFireHoldSeconds = 0.12f;

        Vector3 lastPosition;
        float smoothedSpeed;
        float gunFireReleaseTime;
        bool hasAnimator;
        // Shots leave from the muzzle the server aims from (WV_Unit.WeaponMuzzle), so the tracer and
        // the hit always agree.
        Transform muzzle;

        void Awake() {
            hasAnimator = animator != null;
            muzzle = TryGetComponent(out WV_Unit unit) ? unit.WeaponMuzzle : transform;
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

        /// <summary>
        /// Called on the server when the unit fires at <paramref name="impactPoint"/>; mirrored to
        /// observers for presentation.
        /// </summary>
        [Server]
        public void PlayAttack(Vector3 impactPoint) {
            PlayAttackObservers(impactPoint);
        }

        [ObserversRpc(RunLocally = true, BufferLast = false)]
        void PlayAttackObservers(Vector3 impactPoint) {
            if (hasAnimator) {
                animator.SetBool(GunFireId, true);
                gunFireReleaseTime = Time.time + GunFireHoldSeconds;
            }
            Vector3 origin = muzzle.position;
            Vector3 normal = origin - impactPoint;
            normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            // The same tracer, flash, and report a handheld gun and a turret use. Compiled to nothing
            // on a dedicated server.
            WeaponFireEffects.Play(origin, impactPoint, normal, true, muzzleFlash, audioSource, fireAudio);
            ShotShown?.Invoke(this, origin, impactPoint);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ShotShown = null;
    }
}
