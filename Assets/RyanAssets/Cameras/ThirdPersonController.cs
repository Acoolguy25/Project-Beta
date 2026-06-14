#if !UNITY_SERVER
using UnityEngine;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine.InputSystem;
using RyanAssets.Client.ClientUI.GameSettings;

namespace RyanAssets.Cameras
{
    public class ThirdPersonController : MonoBehaviour
    {
        [Header("Scrollwheel Settings")]
        public float MinZoom = 1f;
        public float MaxZoom = 20.0f;
        // public float ZoomMultiplier = 300.0f;
        public float ZoomPercentage = 0.3f;
        [Header("Force Scroll Settings")]
        public float ForceScrollOffset = 0.5f;
        public float ForceScrollPercentage = 0.1f;

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
            if (cinemachineCamera.Follow == null) return;
            float zoomDelta = scrollWheel.ReadValue<float>() * GameSettingsClient.GetSettingValue<int>("ZoomSensitivity") / 100f;//zoomMultiplierSensitivity.value;
            if (zoomDelta != 0)
            {
                newScroll += zoomDelta;
                newScroll = Mathf.Clamp(newScroll, MinZoom, MaxZoom);
            }
            RaycastHit hit;
            if (Physics.Raycast(cinemachineCamera.Follow.position, 
                transform.TransformDirection(Vector3.back), 
                out hit, 
                MaxZoom, layerMask)){
                forceScroll = Mathf.Floor(Mathf.Clamp(newScroll, 0, hit.distance - ForceScrollOffset));
                //Debug.Log("Hit: " + hit.collider.name + " at distance: " + hit.distance);
                orbitalFollow.Radius = Mathf.Lerp(orbitalFollow.Radius, forceScroll, started? 1: ForceScrollPercentage);
                Debug.DrawRay(cinemachineCamera.Follow.position, transform.TransformDirection(Vector3.back) * 1000, Color.white);
            }
            else
            {
                orbitalFollow.Radius = Mathf.Lerp(orbitalFollow.Radius, newScroll, started ? 1 : ZoomPercentage);
                //Debug.Log("Did not Hit");
            }
        }
    }
}
#endif