using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
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
    /// shown on this client, the local player's and everyone else's, gets an
    /// impact puff (from <see cref="WeaponFireEffects.ShotShown"/>; the flash, tracer and model are each
    /// gun's own, <see cref="CTF_GunLooks"/>). Every new body starts with the
    /// pistol in hand (selected through the character, like pressing 1), and the pistol is posed low
    /// right in first person (the shared <see cref="FirstPersonToolView"/> only poses tools for a locked
    /// first-person camera; here the player may switch between first and third person).
    /// <para>Guns: every Flag Wars gun is the pistol tool re-tuned. The server sends the gun this life
    /// wears in <see cref="CTF_LoadoutBroadcast.liveStats"/>; this client fires the tool, so the firing
    /// pattern (rate, rounds per pull, spread, range) is applied to the own pistol here, and recoil is
    /// scaled per gun so a marksman shot kicks and a burst chatters.</para>
    /// </summary>
    public sealed class CTF_GunFeel : MonoBehaviour {
        [Header("Pistol (own shots)")]
        [Range(0f, 1f)] public float recoilShake = 0.18f;
        [Min(0f)] public float recoilSeconds = 0.12f;
        [Tooltip("Extra field of view, in degrees, kicked out on each shot.")]
        [Min(0f)] public float recoilFovDegrees = 1.5f;
        [Min(0f)] public float recoilFovSeconds = 0.18f;
        [Tooltip("Recoil (shake and FOV kick) per gun, in CTF_Weapon order: Sidearm, Burst Rifle (per round), Scattergun, Marksman.")]
        public float[] recoilScale = { 1f, 0.45f, 1.6f, 1.9f };

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
        [Tooltip("Each gun's model, shot sound, tracer and muzzle flash (and its first-person pose).")]
        public CTF_GunLooks looks;
        public Color impactTint = new(1f, 0.9f, 0.7f);
        [Min(0.05f)] public float impactSparkScale = 0.5f;
        [Min(0.05f)] public float impactDustScale = 0.7f;

        [Header("Dagger (own swings)")]
        [Range(0f, 1f)] public float swingShake = 0.08f;
        [Min(0f)] public float swingShakeSeconds = 0.1f;
        [Range(0f, 1f)] public float swingVolume = 0.6f;

        ClientManager manager;
        CTF_LoadoutBroadcast live;
        bool hasLive;
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

        void OnDestroy() {
            if (manager != null) manager.UnregisterBroadcast<CTF_LoadoutBroadcast>(OnLoadout);
        }

        /// <summary>The gun this life wears (valid once <see cref="HasLiveStats"/>).</summary>
        public CTF_GunStats LiveStats => live.liveStats;
        public CTF_Weapon LiveWeapon => live.liveWeapon;
        public bool HasLiveStats => hasLive;

        void OnLoadout(CTF_LoadoutBroadcast message, Channel channel) {
            bool newGun = !hasLive || message.liveWeapon != live.liveWeapon || message.livePerk != live.livePerk;
            live = message;
            hasLive = true;
            // A changed gun comes with a full clip of its own size; the same gun keeps the rounds left.
            if (ownPistol is ToolGunShared gun) CTF_GunSetup.ApplyFiringPattern(gun, message.liveStats, refill: newGun);
        }

        void OnToolCreated(ToolBaseShared tool) {
            if (tool is not ToolGunShared || !tool.IsOwner) return;
            ownPistol = tool;
            posed = false;
            // A fresh body's pistol: wear the gun the server last named (a newer broadcast re-applies it).
            if (hasLive) CTF_GunSetup.ApplyFiringPattern((ToolGunShared)tool, live.liveStats, refill: true);
            if (!equipPistolOnSpawn) return;
            pendingEquip = tool;
            equipAt = Time.time + equipDelay;
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<CTF_LoadoutBroadcast>(OnLoadout);
            }
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
            // A long gun sits closer and smaller than the pistol, so its barrel does not fill the view.
            var look = looks != null ? looks.OwnLook : null;
            float scale = look != null ? look.firstPersonScale : firstPersonScale;
            Vector3 offset = look != null ? look.firstPersonOffset : firstPersonOffset;
            Vector3 parentScale = root.parent.lossyScale;
            root.localScale = new Vector3(scale / Mathf.Max(0.001f, parentScale.x), scale / Mathf.Max(0.001f, parentScale.y), scale / Mathf.Max(0.001f, parentScale.z));
            root.SetPositionAndRotation(cam.transform.TransformPoint(offset), cam.transform.rotation * Quaternion.Euler(firstPersonAngles));
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
                case ToolGunShared: {
                    float scale = RecoilScale();
                    CameraShake.Kick(Mathf.Clamp01(recoilShake * scale), recoilSeconds);
                    CameraFov.Kick(recoilFovDegrees * scale, recoilFovSeconds);
                    break;
                }
                case ToolMeleeShared:
                    CameraShake.Kick(swingShake, swingShakeSeconds);
                    GameSfx.Play(SfxCue.Whoosh, swingVolume);
                    break;
            }
        }

        float RecoilScale() {
            int i = hasLive ? (int)live.liveWeapon : 0;
            return recoilScale != null && i >= 0 && i < recoilScale.Length ? Mathf.Max(0f, recoilScale[i]) : 1f;
        }

        /// <summary>The impact (the muzzle flash and tracer are the gun's own, see <see cref="CTF_GunLooks"/>).</summary>
        void OnShotShown(Vector3 muzzle, Vector3 impact, Vector3 normal, bool hasImpact) {
            if (!hasImpact) return;
            GameFx.Play(FxKind.Sparks, impact, normal, impactTint, impactSparkScale);
            GameFx.Play(FxKind.DustPuff, impact, normal, impactTint, impactDustScale);
        }
    }
}
