#if !UNITY_SERVER
using RyanAssets.Client.ClientUI.GameSettings;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RyanAssets.Cameras
{
    [RequireComponent(typeof(ZoomComponent))]
    public class ThirdPersonController : ICamera
    {
        [Header("Wall Collision")]
        [SerializeField, Min(0f)] private float forceScrollOffset = 1f;
        [SerializeField, Min(0f)] private float forceScrollSmoothTime = 0.05f;

        [Header("Orbit Input")]
        public InputAction rightClick;

        private CinemachineCamera cinemachineCamera;
        private CinemachineInputAxisController cinemachineInputAxisController;
        private CinemachineOrbitalFollow orbitalFollow;
        private ZoomComponent zoomComponent;
        private bool isRotating;
        private Vector2 cursorPosition;
        private LayerMask wallLayerMask;

        private void Start()
        {
            wallLayerMask = ~LayerMask.GetMask("Character", "UI");
            orbitalFollow = GetComponent<CinemachineOrbitalFollow>();
            cinemachineInputAxisController = GetComponent<CinemachineInputAxisController>();
            cinemachineCamera = GetComponent<CinemachineCamera>();
            zoomComponent = GetComponent<ZoomComponent>();

            zoomComponent.SetZoom(orbitalFollow.Radius, true);
            UpdateCameraZoom(true);
        }

        private void OnEnable()
        {
            rightClick.Enable();
            rightClick.performed += OnRightClick;
            rightClick.canceled += OnRightClickRelease;
        }

        private void OnDisable()
        {
            rightClick.performed -= OnRightClick;
            rightClick.canceled -= OnRightClickRelease;
            OnRightClickRelease(new InputAction.CallbackContext());
            rightClick.Disable();
        }

        private void ToggleRightClick(bool newValue)
        {
            isRotating = newValue;
            if (cinemachineInputAxisController)
            {
                cinemachineInputAxisController.enabled = newValue;
                foreach (var controller in cinemachineInputAxisController.Controllers)
                {
                    switch (controller.Name)
                    {
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

        private void OnRightClick(InputAction.CallbackContext context)
        {
            if (!InputService.GetActionMapActive(RyanAssetsActionMap.Character))
                return;

            ToggleRightClick(true);
        }

        private void OnRightClickRelease(InputAction.CallbackContext context)
        {
            ToggleRightClick(false);
        }

        private void LateUpdate()
        {
            UpdateCameraZoom();
            if (isRotating && Mouse.current != null)
                Mouse.current.WarpCursorPosition(cursorPosition);
        }

        private void UpdateCameraZoom(bool immediate = false)
        {
            if (cinemachineCamera.Follow == null
                || !InputService.GetActionMapActive(RyanAssetsActionMap.Character))
                return;

            float wallCeiling = zoomComponent.MaxZoom;
            if (Physics.Raycast(
                    cinemachineCamera.Follow.position,
                    transform.TransformDirection(Vector3.back),
                    out RaycastHit hit,
                    zoomComponent.MaxZoom + forceScrollOffset,
                    wallLayerMask))
            {
                wallCeiling = Mathf.Max(zoomComponent.MinZoom, hit.distance - forceScrollOffset);
                Debug.DrawRay(
                    cinemachineCamera.Follow.position,
                    transform.TransformDirection(Vector3.back) * hit.distance,
                    Color.white);
            }

            float smoothTime = wallCeiling < zoomComponent.DesiredZoom
                ? forceScrollSmoothTime
                : zoomComponent.ZoomSmoothTime;
            orbitalFollow.Radius = zoomComponent.UpdateZoom(orbitalFollow.Radius, wallCeiling, smoothTime, immediate);
        }
    }
}
#endif
