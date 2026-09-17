#if !UNITY_SERVER
using RyanAssets.Client.ClientUI.GameSettings;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RyanAssets.Cameras
{
    public class ZoomComponent : MonoBehaviour
    {
        [Header("Zoom Limits")]
        [Min(0f)] public float MinZoom = 1f;
        [Min(0f)] public float MaxZoom = 20f;
        [Min(0f)] public float InitialZoom = 10f;

        [Header("Zoom Input")]
        public InputAction scrollWheel = new(
            "Zoom",
            InputActionType.Value,
            "<Mouse>/scroll/y",
            processors: "scale(factor=-0.01)");
        [SerializeField] private bool useGameSettingsSensitivity = true;
        [SerializeField, Min(0f)] private float zoomSensitivity = 1f;
        [SerializeField, Min(0f), Tooltip("Base zoom units per second while Q (out) or E (in) is held. Keyboard zoom is 5x the configured zoom sensitivity.")]
        private float keyboardZoomSpeed = 12f;
        [Min(0f)] public float ZoomSmoothTime = 0.12f;

        private float zoomVelocity;

        public float DesiredZoom { get; private set; }
        private static readonly List<RaycastResult> RaycastResults = new();

        private bool IsPointerOverScrollRect() {
            if (EventSystem.current == null || Mouse.current == null)
                return false;

            PointerEventData eventData = new(EventSystem.current) {
                position = Mouse.current.position.ReadValue()
            };

            RaycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, RaycastResults);

            foreach (RaycastResult result in RaycastResults) {
                if (result.gameObject.GetComponentInParent<ScrollRect>() != null)
                    return true;
            }

            return false;
        }

        private void Awake()
        {
            ValidateLimits();
            DesiredZoom = Mathf.Clamp(InitialZoom, MinZoom, MaxZoom);
        }

        private void OnEnable()
        {
            scrollWheel.Enable();
        }

        private void OnDisable()
        {
            scrollWheel.Disable();
        }

        private void OnValidate()
        {
            ValidateLimits();
        }

        public void SetZoom(float zoom, bool resetVelocity = false)
        {
            DesiredZoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            if (resetVelocity)
                zoomVelocity = 0f;
        }

        public float UpdateZoom(
            float currentZoom,
            float maximumAllowedZoom = float.PositiveInfinity,
            float smoothTime = -1f,
            bool immediate = false)
        {
            float sensitivity = zoomSensitivity;
            if (useGameSettingsSensitivity)
                sensitivity *= GameSettingsClient.GetSettingValue<int>("ZoomSensitivity") / 100f;

            if (!IsPointerOverScrollRect())
            {
                float keyboardZoom = 0f;
                if (Keyboard.current != null)
                {
                    if (Keyboard.current.eKey.isPressed)
                        keyboardZoom -= 1f;
                    if (Keyboard.current.qKey.isPressed)
                        keyboardZoom += 1f;
                }

                SetZoom(DesiredZoom + scrollWheel.ReadValue<float>() * sensitivity +
                    keyboardZoom * keyboardZoomSpeed * sensitivity * 5f * Time.unscaledDeltaTime);
            }

            float targetZoom = Mathf.Min(DesiredZoom, maximumAllowedZoom);
            targetZoom = Mathf.Clamp(targetZoom, MinZoom, MaxZoom);
            if (immediate)
            {
                zoomVelocity = 0f;
                return targetZoom;
            }

            float appliedSmoothTime = smoothTime >= 0f ? smoothTime : ZoomSmoothTime;
            return Mathf.SmoothDamp(currentZoom, targetZoom, ref zoomVelocity, appliedSmoothTime);
        }

        private void ValidateLimits()
        {
            MinZoom = Mathf.Max(0f, MinZoom);
            MaxZoom = Mathf.Max(MinZoom, MaxZoom);
            InitialZoom = Mathf.Clamp(InitialZoom, MinZoom, MaxZoom);
        }
    }
}
#endif
