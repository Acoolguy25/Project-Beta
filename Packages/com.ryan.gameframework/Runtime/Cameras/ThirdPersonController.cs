#if !UNITY_SERVER
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientUI.GameSettings;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RyanAssets.Cameras {
    /// <summary>
    /// Orbiting third-person camera: hold right click to orbit, scroll to zoom, and the camera pulls in
    /// in front of walls.
    /// <para>
    /// With <see cref="alignToGravity"/> on, the orbit is built around a "gravity frame" that follows the
    /// character's <see cref="CharacterGravity"/>: when the character walks onto a wall or ceiling the
    /// frame turns over with them, so "up" on screen stays the character's up and orbiting still circles
    /// around them. With default gravity the frame is the world frame, so nothing changes.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(ZoomComponent))]
    [DefaultExecutionOrder(-100)] // the gravity frame must move before the Cinemachine brain reads it
    public class ThirdPersonController : ICamera {
        [Header("Wall Collision")]
        [Tooltip("Distance kept between the camera and a wall behind it.")]
        [SerializeField, Min(0f)] private float forceScrollOffset = 1f;
        [Tooltip("Seconds to pull in when a wall comes between the camera and the character.")]
        [SerializeField, Min(0f)] private float forceScrollSmoothTime = 0.05f;

        [Header("Orbit Input")]
        public InputAction rightClick;

        [Header("Gravity")]
        [Tooltip("Turn the orbit with the character's gravity so walls and ceilings can be walked on comfortably.")]
        [SerializeField] private bool alignToGravity = true;
        [Tooltip("How fast the camera's up follows a new gravity direction (degrees per second).")]
        [SerializeField, Min(1f)] private float gravityAlignSpeed = 240f;

        private CinemachineCamera cinemachineCamera;
        private CinemachineInputAxisController cinemachineInputAxisController;
        private CinemachineOrbitalFollow orbitalFollow;
        private ZoomComponent zoomComponent;
        private bool initialized;
        private bool isRotating;
        private Vector2 cursorPosition;
        private LayerMask wallLayerMask;

        private Transform followTarget;      // the character's camera anchor
        private CharacterGravity targetGravity;
        private Transform gravityFrame;      // what Cinemachine actually follows when aligning to gravity
        private CinemachineBrain brain;
        private Transform previousWorldUpOverride;

        private Transform OrbitTarget => alignToGravity ? gravityFrame : followTarget;

        private void Initialize() {
            if (initialized)
                return;
            initialized = true;
            // Characters never pull the camera in, including the player's own limbs while ragdolled.
            wallLayerMask = ~LayerMask.GetMask("Character", "LocalCharacter", "UI");
            orbitalFollow = GetComponent<CinemachineOrbitalFollow>();
            cinemachineInputAxisController = GetComponent<CinemachineInputAxisController>();
            cinemachineCamera = GetComponent<CinemachineCamera>();
            zoomComponent = GetComponent<ZoomComponent>();

            if (alignToGravity) {
                // The orbit is measured in the gravity frame's own axes, which is exactly world space
                // until gravity changes direction.
                gravityFrame = new GameObject($"{name} Gravity Frame").transform;
                gravityFrame.SetParent(transform.parent, false);
                var tracker = orbitalFollow.TrackerSettings;
                tracker.BindingMode = BindingMode.LockToTarget;
                orbitalFollow.TrackerSettings = tracker;
            }
        }

        private void Start() {
            Initialize();
            zoomComponent.SetZoom(orbitalFollow.Radius, true);
            UpdateCameraZoom(true);
        }

        private void OnEnable() {
            Initialize();
            rightClick.Enable();
            rightClick.performed += OnRightClick;
            rightClick.canceled += OnRightClickRelease;
            if (alignToGravity) {
                // Aim and roll use the brain's world up; while this mode is live, that is the frame's up.
                brain = CinemachineCore.FindPotentialTargetBrain(cinemachineCamera);
                if (brain != null) {
                    previousWorldUpOverride = brain.WorldUpOverride;
                    brain.WorldUpOverride = gravityFrame;
                }
                SnapGravityFrame();
            }
        }

        private void OnDisable() {
            rightClick.performed -= OnRightClick;
            rightClick.canceled -= OnRightClickRelease;
            OnRightClickRelease(new InputAction.CallbackContext());
            rightClick.Disable();
            if (brain != null && brain.WorldUpOverride == gravityFrame)
                brain.WorldUpOverride = previousWorldUpOverride;
            brain = null;
        }

        private void OnDestroy() {
            if (gravityFrame != null)
                Destroy(gravityFrame.gameObject);
        }

        public override void SetTarget(GameCharacter target) {
            Initialize();
            followTarget = target != null ? target.CharacterCamera : null;
            targetGravity = target != null ? target.GetComponent<CharacterGravity>() : null;
            cinemachineCamera.Follow = cinemachineCamera.LookAt = followTarget != null ? OrbitTarget : null;
            SnapGravityFrame();
        }

        private void LateUpdate() {
            UpdateGravityFrame(Time.deltaTime);
            UpdateCameraZoom();
            if (isRotating && Mouse.current != null)
                Mouse.current.WarpCursorPosition(cursorPosition);
        }

        // ------------------------------------------------------------------ gravity frame
        private Vector3 TargetUp => targetGravity != null ? targetGravity.Up : Vector3.up;

        private void SnapGravityFrame() {
            if (!alignToGravity || gravityFrame == null)
                return;
            if (followTarget != null)
                gravityFrame.position = followTarget.position;
            gravityFrame.rotation = CharacterGravity.TurnToward(Vector3.up, TargetUp, transform.forward);
        }

        /// <summary>
        /// Follows the anchor's position and turns the frame's up toward gravity's up, carrying the
        /// frame's other axes along (no spin about up), so the orbit angle the player chose is kept.
        /// </summary>
        private void UpdateGravityFrame(float deltaTime) {
            if (!alignToGravity || gravityFrame == null || followTarget == null)
                return;
            gravityFrame.position = followTarget.position;

            // Upside-down flips roll about the view direction, so the camera stays behind the character
            // and forward on screen stays forward.
            Quaternion turn = CharacterGravity.TurnToward(gravityFrame.up, TargetUp, transform.forward, gravityAlignSpeed * deltaTime);
            gravityFrame.rotation = turn * gravityFrame.rotation;
        }

        // ------------------------------------------------------------------ orbit input
        private void ToggleRightClick(bool newValue) {
            isRotating = newValue;
            if (cinemachineInputAxisController) {
                cinemachineInputAxisController.enabled = newValue;
                foreach (var controller in cinemachineInputAxisController.Controllers) {
                    switch (controller.Name) {
                        case "Look Orbit X":
                            controller.Input.Gain = GameSettingsClient.GetSettingValue<int>("TurnSensitivity") / 100f
                                * GameSettingsClient.GetSettingValue<int>("HorizontalTurnSensitivity") / 900f;
                            break;
                        case "Look Orbit Y":
                            controller.Input.Gain = GameSettingsClient.GetSettingValue<int>("TurnSensitivity") / 100f
                                * -GameSettingsClient.GetSettingValue<int>("VerticalTurnSensitivity") / 900f;
                            break;
                    }
                }
            }

            Cursor.lockState = newValue ? CursorLockMode.Confined : CursorLockMode.None;
            Cursor.visible = !newValue;
            if (Mouse.current != null)
                cursorPosition = Mouse.current.position.value;
        }

        private void OnRightClick(InputAction.CallbackContext context) {
            if (!InputService.GetActionMapActive(RyanAssetsActionMap.Character))
                return;
            ToggleRightClick(true);
        }

        private void OnRightClickRelease(InputAction.CallbackContext context) => ToggleRightClick(false);

        // ------------------------------------------------------------------ zoom
        private void UpdateCameraZoom(bool immediate = false) {
            if (cinemachineCamera.Follow == null
                || !InputService.GetActionMapActive(RyanAssetsActionMap.Character))
                return;

            Vector3 origin = cinemachineCamera.Follow.position;
            Vector3 back = transform.TransformDirection(Vector3.back);
            float wallCeiling = zoomComponent.MaxZoom;
            // Trigger volumes (gravity fields, checkpoints) are not walls.
            if (Physics.Raycast(origin, back, out RaycastHit hit, zoomComponent.MaxZoom + forceScrollOffset, wallLayerMask, QueryTriggerInteraction.Ignore)) {
                wallCeiling = Mathf.Max(zoomComponent.MinZoom, hit.distance - forceScrollOffset);
                Debug.DrawRay(origin, back * hit.distance, Color.white);
            }

            float smoothTime = wallCeiling < zoomComponent.DesiredZoom
                ? forceScrollSmoothTime
                : zoomComponent.ZoomSmoothTime;
            orbitalFollow.Radius = zoomComponent.UpdateZoom(orbitalFollow.Radius, wallCeiling, smoothTime, immediate);
        }
    }
}
#endif
