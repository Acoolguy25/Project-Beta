using FishNet.Object;
using RpcGen;
using UnityEngine;

namespace RyanAssets.Shared.Combat {
    /// <summary>
    /// Makes one weapon's shots visible and audible to everyone, wherever the weapon is mounted.
    /// <para>
    /// A handheld gun fires on the client that owns it; a defensive structure fires on the server.
    /// Both need the same thing afterwards - every observer drawing the same tracer, muzzle flash,
    /// and report - so both go through this one component. <see cref="SharedRpcAttribute"/> handles
    /// the difference: a server caller fans the shot straight out to observers, a client caller
    /// draws it locally and relays it through the server.
    /// </para>
    /// <para>
    /// Only the two ends of the trace cross the wire. Everything else - which particle system, which
    /// clip, how the audio is spatialised - is authored on the prefab this component sits on, so it
    /// is already identical on every machine.
    /// </para>
    /// </summary>
    public sealed class NetworkedWeaponFire : NetworkBehaviour {
        [Header("Authored References")]
        [Tooltip("Where shots leave the weapon. Falls back to this transform when unset.")]
        [SerializeField] Transform muzzle;
        [Tooltip("Optional. Muzzle flash played on each shot.")]
        [SerializeField] ParticleSystem muzzleFlash;
        [Tooltip("Optional. Spatial settings the report is played with.")]
        [SerializeField] AudioSource audioSource;
        [Tooltip("Optional. The weapon's report.")]
        [SerializeField] AudioClip fireAudio;

        /// <summary>Origin of the trace, and of the tracer drawn for it.</summary>
        public Transform Muzzle => muzzle != null ? muzzle : transform;

        public Vector3 MuzzlePosition => Muzzle.position;

        /// <summary>
        /// Shows one shot on every machine. <paramref name="hasImpact"/> is false when the shot
        /// reached its maximum range without striking anything, which leaves a tracer but no spark.
        /// </summary>
        [SharedRpc(RunOnServer = true, RunOnCallingServer = true, RunOnCallingClient = true)]
        public void ShowShot(Vector3 origin, Vector3 impactPoint, Vector3 impactNormal, bool hasImpact) {
            WeaponFireEffects.Play(
                origin, impactPoint, impactNormal, hasImpact, muzzleFlash, audioSource, fireAudio);
        }

        /// <summary>
        /// Shows a shot traced from this weapon's own muzzle. Deliberately not an overload of
        /// <see cref="ShowShot"/>: the shared-RPC weaver rewrites that method by name, and a second
        /// method sharing it would make the rewrite ambiguous.
        /// </summary>
        public void ShowShotFromMuzzle(RaycastHit hit) {
            ShowShot(MuzzlePosition, hit.point, hit.normal, hit.collider != null);
        }
    }
}
