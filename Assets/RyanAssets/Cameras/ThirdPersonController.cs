#if !UNITY_SERVER
using RyanAssets.Client.ClientUI.GameSettings;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RyanAssets.Cameras
{
    public class ThirdPersonController : ICamera {
        [Header("Scrollwheel Settings")]
        public float MinZoom = 1f;
        public float MaxZoom = 20.0f;
        // public float ZoomMultiplier = 300.0f;
        public float ZoomPercentage = 0.3f;
        [Header("Force Scroll Settings")]
        public float ForceScrollOffset = 0.5f;
        public float ForceScrollPercentage = 0.1f;
        private float _radiusVelocity = 0f;

        // Replace ForceScrollPercentage / ZoomPercentage with smooth times (seconds)
        // Tune these — 0.05f is snappy, 0.15f is buttery
        [SerializeField] private float ForceScrollSmoothTime = 0.05f;  // fast push-in near walls
        [SerializeField] private float ZoomSmoothTime = 0.12f;          // gentle ease for normal zoom

        private CinemachineCamera cinemachineCamera;
        private CinemachineInputAxisController cinemachineInputAxisController;
        private CinemachineOrbitalFollow orbitalFollow;
        private float newScroll;
        private float forceScroll;
        public InputAction rightClick;
        public InputAction scrollWheel;
        private bool isRotating = false;
        private Vector2 cursor_pos;

        private LayerMask layerMask;

        void Start()
        {
            forceScroll = MaxZoom;
            layerMask = ~LayerMask.GetMask("Character", "UI");
            orbitalFollow = GetComponent<CinemachineOrbitalFollow>();
            cinemachineInputAxisController = GetComponent<CinemachineInputAxisController>();
            newScroll = orbitalFollow.Radius;
            cinemachineCamera = GetComponent<CinemachineCamera>();
            UpdateCameraZoom(true);
        }
        void OnEnable()
        {
            rightClick.Enable();
            rightClick.performed += OnRightClick;
            rightClick.canceled += OnRightClickRelease;
            scrollWheel.Enable();
        }

        void OnDisable()
        {
            rightClick.performed -= OnRightClick;
            rightClick.canceled -= OnRightClickRelease;
            OnRightClickRelease(new InputAction.CallbackContext());
            rightClick.Disable();
            scrollWheel.Disable();
        }
        void ToggleRightClick(bool newVal) {
            isRotating = newVal;
            if (cinemachineInputAxisController){
                cinemachineInputAxisController.enabled = newVal;
                foreach (var c in cinemachineInputAxisController.Controllers){
                    switch (c.Name)
                    {
                        case "Look Orbit X":
                            c.Input.Gain = GameSettingsClient.GetSettingValue<int>("TurnSensitivity") / 100f * GameSettingsClient.GetSettingValue<int>("HorizontalTurnSensitivity") / 900f;
                            break;

                        case "Look Orbit Y":
                            c.Input.Gain = GameSettingsClient.GetSettingValue<int>("TurnSensitivity")  / 100f * -1f * GameSettingsClient.GetSettingValue<int>("VerticalTurnSensitivity") / 900f; // Invert Y direction
                            break;

                        //case "Orbit Scale":
                        //    c.Input.Gain = GameSettingsClient.GetSettingValue<int>("TurnSensitivity") / 500f;
                        //    break;
                    }
                }
            }
            Cursor.lockState = newVal ? CursorLockMode.Confined : CursorLockMode.None;
            Cursor.visible = !newVal;
            //Mouse.current.position.value;
            cursor_pos = Mouse.current.position.value;
        }
        void OnRightClick(InputAction.CallbackContext context)
        {
            if (!InputService.GetActionMapActive(RyanAssetsActionMap.Character))
                return;
            ToggleRightClick(true);
        }
        void OnRightClickRelease(InputAction.CallbackContext context)
        {
            ToggleRightClick(false);
        }
        void LateUpdate()
        {
            UpdateCameraZoom();
            if (isRotating)
                SetCameraPos();
        }
        void SetCameraPos() {
            Mouse.current.WarpCursorPosition(cursor_pos);
        }
        void UpdateCameraZoom(bool started = false) {
            if (cinemachineCamera.Follow == null || !InputService.GetActionMapActive(RyanAssetsActionMap.Character)) return;

            // --- Input ---
            float zoomDelta = scrollWheel.ReadValue<float>() * GameSettingsClient.GetSettingValue<int>("ZoomSensitivity") / 100f;
            if (zoomDelta != 0) {
                newScroll += zoomDelta;
                newScroll = Mathf.Clamp(newScroll, MinZoom, MaxZoom);
            }

            // --- Wall detection ---
            // Determine the hard ceiling this frame from geometry
            float wallCeiling = MaxZoom;
            RaycastHit hit;
            if (Physics.Raycast(
                    cinemachineCamera.Follow.position,
                    transform.TransformDirection(Vector3.back),
                    out hit,
                    MaxZoom + ForceScrollOffset,   // cast a little further so offset doesn't blind us
                    layerMask)) {
                // Subtract offset from the raw hit distance to keep the camera clear of the surface
                wallCeiling = Mathf.Max(MinZoom, hit.distance - ForceScrollOffset);
                Debug.DrawRay(cinemachineCamera.Follow.position,
                    transform.TransformDirection(Vector3.back) * hit.distance, Color.white);
            }

            // --- Target radius ---
            // Player's desired zoom, hard-clamped by the wall — never let the target exceed geometry
            float targetRadius = Mathf.Min(newScroll, wallCeiling);

            // --- Smooth ---
            // Use SmoothDamp instead of framerate-dependent Lerp percentage
            if (started) {
                orbitalFollow.Radius = targetRadius;
                _radiusVelocity = 0f;
            } else {
                // Push in fast when a wall forces it, ease out gently when the wall clears
                bool wallForcing = wallCeiling < newScroll;
                float smoothTime = wallForcing ? ForceScrollSmoothTime : ZoomSmoothTime;
                orbitalFollow.Radius = Mathf.SmoothDamp(
                    orbitalFollow.Radius, targetRadius, ref _radiusVelocity, smoothTime);
            }
        }
    }
}
#endif