#if !UNITY_SERVER
using RyanAssets.Client.ClientUI.GameSettings;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [Min(0f)] public float ZoomSmoothTime = 0.12f;

        private float zoomVelocity;

        public float DesiredZoom { get; private set; }

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

            SetZoom(DesiredZoom + scrollWheel.ReadValue<float>() * sensitivity);

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
