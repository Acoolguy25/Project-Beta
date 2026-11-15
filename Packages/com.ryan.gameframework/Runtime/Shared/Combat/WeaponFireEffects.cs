#if !UNITY_SERVER
using RyanAssets.Client.ClientAudio;
using RyanAssets.Clients.ClientEffects;
#endif
using UnityEngine;

namespace RyanAssets.Shared.Combat {
    /// <summary>
    /// What a fired shot looks and sounds like on a client: muzzle flash, tracer, impact, report.
    /// <para>
    /// Lifted out of <c>ToolGunShared.VisualizeBulletLocally</c> so a turret's shot is drawn by the
    /// same code as a handheld gun's rather than by a second, slightly different effect. The whole
    /// body is compiled out of a dedicated-server build, which has no renderers or audio listener;
    /// callers can therefore invoke it unconditionally from shared code.
    /// </para>
    /// </summary>
    public static class WeaponFireEffects {
        /// <summary>
        /// Plays one shot's presentation. <paramref name="impactPoint"/> is where the tracer ends,
        /// and <paramref name="hasImpact"/> is false for a shot that reached maximum range without
        /// striking anything, which should leave no impact spark.
        /// </summary>
        public static void Play(
            Vector3 muzzlePosition,
            Vector3 impactPoint,
            Vector3 impactNormal,
            bool hasImpact,
            ParticleSystem muzzleFlash = null,
            AudioSource audioSource = null,
            AudioClip fireAudio = null) {
#if !UNITY_SERVER
            if (muzzleFlash != null)
                muzzleFlash.Play();

            GunVisualEffects.VisualizeBullet(muzzlePosition, impactPoint, impactNormal, hasImpact);

            if (fireAudio != null && audioSource != null && audioSource.isActiveAndEnabled)
                MusicService.CreateOneShot(audioSource, fireAudio);
            else if (fireAudio != null)
                MusicService.CreateOneShot(fireAudio, muzzlePosition);
#endif
        }

        /// <summary>Convenience overload for callers that still hold the raw trace result.</summary>
        public static void Play(
            Vector3 muzzlePosition,
            RaycastHit hit,
            ParticleSystem muzzleFlash = null,
            AudioSource audioSource = null,
            AudioClip fireAudio = null) {
            Play(muzzlePosition, hit.point, hit.normal, hit.collider != null,
                muzzleFlash, audioSource, fireAudio);
        }
    }
}
