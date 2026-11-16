using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using RyanAssets.Platformer;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// How speed and impacts feel for the local player in Sky Obby: the camera widens its field of view
    /// and speed lines streak in when they move fast (springs, boost pads, long drops), with an instant
    /// kick when a pad launches them, and a whoosh plays as they drop past the point of no return.
    /// Hard-landing camera shake comes from the kit's character feel; respawn teleports are reported
    /// to it so arriving on a checkpoint never shakes. Also turns on post-processing for the main camera while the obby is shown, since the
    /// shared camera renders without it and the obby's grade lives in a Volume.
    /// Everything is restored when this component is disabled.
    /// </summary>
    public sealed class OB_PlayerFeel : MonoBehaviour {
        [Header("Post-processing")]
        [Tooltip("Turn on post-processing for the main camera while the obby is shown (its Volume does nothing otherwise).")]
        public bool enablePostProcessing = true;

        [Header("Speed")]
        [Tooltip("Speeds (m/s) where the speed effects start and reach full strength.")]
        public Vector2 speedRange = new(17f, 32f);
        [Tooltip("Extra field of view (degrees) at full speed.")]
        [Range(0f, 30f)] public float fovKick = 10f;
        [Tooltip("Speed line intensity (0..1) at full speed.")]
        [Range(0f, 1f)] public float speedLines = 0.75f;
        [Tooltip("Seconds the effects take to build up.")]
        [Min(0.01f)] public float attackSeconds = 0.12f;
        [Tooltip("Seconds the effects take to fade after slowing down.")]
        [Min(0.01f)] public float releaseSeconds = 0.5f;

        [Tooltip("Speed effect strength (0..1) a launch pad kicks in at once, before the speed itself builds up.")]
        [Range(0f, 1f)] public float launchKick = 0.6f;

        [Header("Falling off")]
        public OB_Cue fallSound = new(SfxCue.Whoosh, 0.9f, 0.7f);
        [Tooltip("The whoosh plays this many metres above the height where the server sends a falling player back.")]
        [Min(0f)] public float fallWarningHeight = 7f;
        [Tooltip("Smallest downward speed (m/s) that counts as falling off.")]
        [Min(0f)] public float fallWarningSpeed = 8f;

        /// <summary>World height below which the server returns the local player to their checkpoint.</summary>
        public float KillHeight { get; set; } = float.NegativeInfinity;

        CharacterMovement player;
        bool fallWarned;
        float intensity, shownLines;

        Camera mainCamera;
        UniversalAdditionalCameraData cameraData;
        bool forcedPostProcessing;
        CinemachineBrain brain;
        CinemachineCamera lensCamera;
        float baseFieldOfView;
        bool cameraKicked;

        void OnEnable() {
            LocalCharacter.Teleported += OnTeleported;
            LaunchPad.Launched += OnLaunched;
        }

        void OnDisable() {
            LocalCharacter.Teleported -= OnTeleported;
            LaunchPad.Launched -= OnLaunched;
            ReleaseCamera();
            intensity = 0f;
            if (shownLines > 0f) ScreenFx.SetSpeedLines(0f);
            shownLines = 0f;
        }

        void Update() {
            TrackPlayer();
            UpdateSpeed();
        }

        void LateUpdate() {
            BindCamera();
            ApplyFieldOfView();
        }

        // ---------------------------------------------------------------- local player
        void TrackPlayer() {
            var local = CharacterMovement.Local;
            if (local != player) {
                player = local;
                fallWarned = false;
            }
            if (player == null || player.Body == null) return;
            if (player.Grounded) {
                fallWarned = false;
            } else if (!fallWarned && player.Body.linearVelocity.y < -fallWarningSpeed && player.Body.position.y < KillHeight + fallWarningHeight) {
                fallWarned = true;
                fallSound.Play();
            }
        }

        /// <summary>A respawn is not a landing: the kit must not shake the camera when the player arrives.</summary>
        void OnTeleported(LocalCharacter character) {
            if (character == null || LocalPlayer.Character != character) return;
            fallWarned = false;
            CharacterFeel.NotifyTeleported(character);
        }

        void OnLaunched(LaunchPad pad, CharacterMovement movement) {
            if (movement == CharacterMovement.Local) intensity = Mathf.Max(intensity, launchKick);
        }

        void UpdateSpeed() {
            float speed = player != null && player.Body != null ? player.Body.linearVelocity.magnitude : 0f;
            float target = Mathf.InverseLerp(speedRange.x, speedRange.y, speed);
            float seconds = target > intensity ? attackSeconds : releaseSeconds;
            intensity = Mathf.MoveTowards(intensity, target, Time.deltaTime / seconds);
            float lines = intensity * speedLines;
            if (Mathf.Approximately(lines, shownLines)) return;
            shownLines = lines;
            ScreenFx.SetSpeedLines(lines);
        }

        // ---------------------------------------------------------------- camera
        /// <summary>
        /// Follows the main camera and whatever sets its field of view: the live Cinemachine camera when a brain
        /// outputs to the main camera, otherwise the camera itself. Restores whatever it leaves.
        /// </summary>
        void BindCamera() {
            Camera main = Camera.main;
            if (main != mainCamera) {
                ReleaseCamera();
                mainCamera = main;
                cameraData = main != null ? main.GetUniversalAdditionalCameraData() : null;
                brain = main != null ? main.GetComponent<CinemachineBrain>() : null;
                if (main != null) baseFieldOfView = main.fieldOfView;
            }
            // Re-asserted every frame: the kit's mood blender turns post-processing back off when a mood it forced on ends.
            if (enablePostProcessing && cameraData != null && !cameraData.renderPostProcessing) {
                cameraData.renderPostProcessing = true;
                forcedPostProcessing = true;
            }
            var live = brain != null ? brain.ActiveVirtualCamera as CinemachineCamera : null;
            if (live != lensCamera) {
                RestoreLens();
                lensCamera = live;
                if (lensCamera != null) baseFieldOfView = lensCamera.Lens.FieldOfView;
            }
        }

        void ApplyFieldOfView() {
            float fov = baseFieldOfView + fovKick * intensity;
            if (lensCamera != null) {
                lensCamera.Lens.FieldOfView = fov;
            } else if (mainCamera != null) {
                // Only touch the camera while kicking, so anything else that sets its field of view keeps working.
                if (intensity <= 0f && !cameraKicked) {
                    baseFieldOfView = mainCamera.fieldOfView;
                    return;
                }
                cameraKicked = intensity > 0f;
                mainCamera.fieldOfView = fov;
            }
        }

        void RestoreLens() {
            if (lensCamera != null) lensCamera.Lens.FieldOfView = baseFieldOfView;
            else if (mainCamera != null && cameraKicked) mainCamera.fieldOfView = baseFieldOfView;
            lensCamera = null;
            cameraKicked = false;
        }

        void ReleaseCamera() {
            RestoreLens();
            if (forcedPostProcessing && cameraData != null) cameraData.renderPostProcessing = false;
            forcedPostProcessing = false;
            cameraData = null;
            mainCamera = null;
            brain = null;
        }
    }
}
