using RyanAssets.GameFeel;
using RyanAssets.Shared.Combat;
using RyanAssets.Tools.Client;
using RyanAssets.Tools.Shared;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// Weapon feel layered on the shared tools. The local player's own pistol shot gets camera recoil,
    /// a small FOV kick and a weapon punch (from <see cref="ToolBaseClient.Activated"/>, which fires
    /// only where the tool is controlled); a dagger swing gets a whoosh and a small shake. Every shot
    /// shown on this client, the local player's and everyone else's, gets a muzzle spark burst and an
    /// impact puff (from <see cref="WeaponFireEffects.ShotShown"/>).
    /// </summary>
    public sealed class CTF_GunFeel : MonoBehaviour {
        [Header("Pistol (own shots)")]
        [Range(0f, 1f)] public float recoilShake = 0.18f;
        [Min(0f)] public float recoilSeconds = 0.12f;
        [Tooltip("Extra field of view, in degrees, kicked out on each shot.")]
        [Min(0f)] public float recoilFovDegrees = 1.5f;
        [Min(0f)] public float recoilFovSeconds = 0.18f;
        [Tooltip("Scale punch on the gun model per shot.")]
        [Min(0f)] public float weaponKick = 0.18f;
        [Min(0f)] public float weaponKickSeconds = 0.15f;

        [Header("Every shot (local and remote)")]
        public Color muzzleTint = new(1f, 0.82f, 0.45f);
        [Min(0.05f)] public float muzzleSparkScale = 0.6f;
        [Tooltip("No muzzle sparks closer than this to the camera (your own first-person shot): they would streak across the screen.")]
        [Min(0f)] public float muzzleSparkMinDistance = 3f;
        public Color impactTint = new(1f, 0.9f, 0.7f);
        [Min(0.05f)] public float impactSparkScale = 0.5f;
        [Min(0.05f)] public float impactDustScale = 0.7f;

        [Header("Dagger (own swings)")]
        [Range(0f, 1f)] public float swingShake = 0.08f;
        [Min(0f)] public float swingShakeSeconds = 0.1f;
        [Range(0f, 1f)] public float swingVolume = 0.6f;

        void OnEnable() {
            ToolBaseClient.Activated += OnActivated;
            WeaponFireEffects.ShotShown += OnShotShown;
        }

        void OnDisable() {
            ToolBaseClient.Activated -= OnActivated;
            WeaponFireEffects.ShotShown -= OnShotShown;
        }

        void OnActivated(ToolBaseClient tool, Vector3 target) {
            ToolBaseShared shared = tool.SharedTool;
            if (shared == null || !shared.IsOwner) return;
            switch (shared) {
                case ToolGunShared gun:
                    CameraShake.Kick(recoilShake, recoilSeconds);
                    CameraFov.Kick(recoilFovDegrees, recoilFovSeconds);
                    Punch.Scale(gun.weaponRoot.transform, weaponKick, weaponKickSeconds);
                    break;
                case ToolMeleeShared:
                    CameraShake.Kick(swingShake, swingShakeSeconds);
                    GameSfx.Play(SfxCue.Whoosh, swingVolume);
                    break;
            }
        }

        void OnShotShown(Vector3 muzzle, Vector3 impact, Vector3 normal, bool hasImpact) {
            Vector3 aim = impact - muzzle;
            Camera cam = Camera.main;
            bool nearCamera = cam != null && (muzzle - cam.transform.position).sqrMagnitude < muzzleSparkMinDistance * muzzleSparkMinDistance;
            if (!nearCamera)
                GameFx.Play(FxKind.Sparks, muzzle, aim.sqrMagnitude > 0f ? aim.normalized : Vector3.up, muzzleTint, muzzleSparkScale);
            if (!hasImpact) return;
            GameFx.Play(FxKind.Sparks, impact, normal, impactTint, impactSparkScale);
            GameFx.Play(FxKind.DustPuff, impact, normal, impactTint, impactDustScale);
        }
    }
}
