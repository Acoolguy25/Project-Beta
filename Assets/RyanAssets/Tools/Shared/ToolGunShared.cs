using RyanAssets.Shared.Combat;
using UnityEngine;
using RpcGen;

namespace RyanAssets.Tools.Shared {
    public partial class ToolGunShared : ToolBaseShared {
        [Header("Gun Stats")]
        [SerializeField]
        public int BestAccuracy = 240; // 0.0 to 360.0, where 0.0 is perfect accuracy
        [SerializeField]
        public int WorstAccuracy = 300; // 0.0 to 360.0, where 0.0 is perfect accuracy
        [SerializeField]
        public float MaxRange = 30f;
        [SerializeField]
        public float FireRate = 0.3f;
        [SerializeField, Min(0.01f)]
        public float MuzzleCollisionRadius = 0.1f;

        [Header("Gun Fire Mode")]
        [SerializeField]
        public int BurstCount = 1;
        [SerializeField]
        public float BurstDelay = 0.1f;

        public ParticleSystem FireParticleSystem;

        /// <summary>
        /// Traces one shot toward <paramref name="targetLocation"/>.
        /// <para>
        /// The trace itself lives in <see cref="HitscanShot"/> so a turret or a vehicle mount fires
        /// through exactly the same muzzle check, spread cone, and hit resolution this gun does.
        /// </para>
        /// </summary>
        public RaycastHit? Shoot(Vector3 targetLocation) {
            Vector3 origin = weaponRoot.transform.position;
            return HitscanShot.Fire(
                origin,
                targetLocation,
                MaxRange,
                UnityEngine.Random.Range(WorstAccuracy / 360f, BestAccuracy / 360f),
                connectedCharacter != null ? connectedCharacter.transform : null,
                MuzzleCollisionRadius,
                // The whole tool, not just its weapon root: the muzzle check must not treat the
                // gun's own casing as something it is buried inside.
                transform);
        }

        public void VisualizeBulletLocally(RaycastHit? hit) {
            if (hit == null)
                return;

            // The tracer starts at the particle system rather than at the trace origin, because the
            // authored muzzle effect is where a player sees the shot leave the weapon.
            Vector3 muzzlePosition = FireParticleSystem != null
                ? FireParticleSystem.transform.position
                : weaponRoot.transform.position;

            WeaponFireEffects.Play(muzzlePosition, hit.Value, FireParticleSystem, audioSource, attackAudio);
        }

        [SharedRpc(RunOnServer = true, RunOnCallingClient = false)]
        public void VisualizeBullet(Vector3 targetLocation) {
            VisualizeBulletLocally(Shoot(targetLocation));
        }
    }
}
