using RyanAssets.Cameras;
using RyanAssets.Characters.Client;
using RyanAssets.GameFeel;
using RyanAssets.Shared.Combat;
using RyanAssets.Tools.Client;
using RyanAssets.Tools.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// Weapon feel layered on the shared tools. The local player's own pistol shot gets camera recoil,
    /// a small FOV kick and a weapon punch (from <see cref="ToolBaseClient.Activated"/>, which fires
    /// only where the tool is controlled); a dagger swing gets a whoosh and a small shake. Every shot
    /// shown on this client, the local player's and everyone else's, gets a muzzle spark burst and an
    /// impact puff (from <see cref="WeaponFireEffects.ShotShown"/>). Every new body starts with the
    /// pistol in hand (selected through the character, like pressing 1), and the pistol is posed low
    /// right in first person (the shared <see cref="FirstPersonToolView"/> only poses tools for a locked
    /// first-person camera; here the player may switch between first and third person).
    /// </summary>
    public sealed class CTF_GunFeel : MonoBehaviour {
        [Header("Pistol (own shots)")]
        [Range(0f, 1f)] public float recoilShake = 0.18f;
        [Min(0f)] public float recoilSeconds = 0.12f;
        [Tooltip("Extra field of view, in degrees, kicked out on each shot.")]
        [Min(0f)] public float recoilFovDegrees = 1.5f;
        [Min(0f)] public float recoilFovSeconds = 0.18f;

        [Header("Loadout")]
        [Tooltip("Equip the pistol on every spawn, so nobody starts a fight unarmed.")]
        public bool equipPistolOnSpawn = true;
        [Tooltip("Seconds after the pistol appears before it is equipped, so the toolbar has its slot and highlights it.")]
        [Min(0f)] public float equipDelay = 0.5f;

        [Header("First-person pistol")]
        [Tooltip("Pose the pistol low right while the first-person camera is active (the robot's hand would hold it, huge, in the middle of the view).")]
        public bool firstPersonPose = true;
        [Tooltip("Where the pistol sits, in camera space (metres: right, up, forward).")]
        public Vector3 firstPersonOffset = new(0.3f, -0.28f, 0.55f);
        public Vector3 firstPersonAngles = new(0f, -4f, 0f);
        [Tooltip("World scale of the pistol in first person.")]
        [Min(0.01f)] public float firstPersonScale = 0.7f;

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

        ToolBaseShared pendingEquip, ownPistol;
        float equipAt;
        Vector3 pistolPosition, pistolScale;
        Quaternion pistolRotation;
        bool posed;

        void OnEnable() {
            ToolBaseClient.Activated += OnActivated;
            WeaponFireEffects.ShotShown += OnShotShown;
            ToolBaseShared.createStaticEvent += OnToolCreated;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDisable() {
            ToolBaseClient.Activated -= OnActivated;
            WeaponFireEffects.ShotShown -= OnShotShown;
            ToolBaseShared.createStaticEvent -= OnToolCreated;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Unpose();
            pendingEquip = ownPistol = null;
        }

        void OnToolCreated(ToolBaseShared tool) {
            if (tool is not ToolGunShared || !tool.IsOwner) return;
            ownPistol = tool;
            posed = false;
            if (!equipPistolOnSpawn) return;
            pendingEquip = tool;
            equipAt = Time.time + equipDelay;
        }

        void Update() {
            if (pendingEquip == null || Time.time < equipAt) return;
            var tool = pendingEquip;
            pendingEquip = null;
            var character = LocalPlayer.Character;
            // Only a fresh body with nothing in hand: never override a weapon the player already picked.
            if (character != null && !character.IsDead && character.ActiveTool.Value == null
                && tool.connectedCharacter != null && tool.connectedCharacter.gameObject == character.gameObject)
                character.SwitchTool(tool);
        }

        /// <summary>Right before the main camera renders (after it has moved), pose the pistol in front of it.</summary>
        void OnBeginCamera(ScriptableRenderContext context, Camera cam) {
            if (ownPistol == null || cam != Camera.main) return;
            bool firstPerson = firstPersonPose && ownPistol.equipped
                && CameraController.Instance != null && CameraController.Instance.activeCamera is FirstPersonController;
            if (!firstPerson) {
                Unpose();
                return;
            }
            Transform root = ownPistol.weaponRoot.transform;
            if (!posed) {
                pistolPosition = root.localPosition;
                pistolRotation = root.localRotation;
                pistolScale = root.localScale;
                posed = true;
            }
            Vector3 parentScale = root.parent.lossyScale;
            root.localScale = new Vector3(firstPersonScale / Mathf.Max(0.001f, parentScale.x), firstPersonScale / Mathf.Max(0.001f, parentScale.y), firstPersonScale / Mathf.Max(0.001f, parentScale.z));
            root.SetPositionAndRotation(cam.transform.TransformPoint(firstPersonOffset), cam.transform.rotation * Quaternion.Euler(firstPersonAngles));
        }

        void Unpose() {
            if (!posed || ownPistol == null) return;
            Transform root = ownPistol.weaponRoot.transform;
            root.SetLocalPositionAndRotation(pistolPosition, pistolRotation);
            root.localScale = pistolScale;
            posed = false;
        }

        void OnActivated(ToolBaseClient tool, Vector3 target) {
            ToolBaseShared shared = tool.SharedTool;
            if (shared == null || !shared.IsOwner) return;
            switch (shared) {
                case ToolGunShared:
                    CameraShake.Kick(recoilShake, recoilSeconds);
                    CameraFov.Kick(recoilFovDegrees, recoilFovSeconds);
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
