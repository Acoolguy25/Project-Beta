#if !UNITY_SERVER
using RyanAssets.Characters.Client;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace RyanAssets.Cameras
{
    [RequireComponent(typeof(ZoomComponent))]
    public class TwoDimController : ICamera
    {
        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 10f;

        [Header("View")]
        [SerializeField] private Vector3 initialFocusPoint = Vector3.zero;
        [SerializeField] private Vector3 viewEulerAngles = new(70f, 0f, 0f);
        [SerializeField, Min(1f)] private float cameraDistance = 60f;

        [Header("Grid")]
        [SerializeField] private bool snapToGrid;
        [SerializeField, Min(0.0001f)] private float gridSize = 1f;
        [SerializeField] private Vector2 gridOrigin;

        [Header("Bounds (World X/Z)")]
        [SerializeField] private Vector2 minimumBounds = new(-50f, -50f);
        [SerializeField] private Vector2 maximumBounds = new(50f, 50f);

        private ZoomComponent zoomComponent;
        private Camera controlledCamera;
        private Vector3 unsnappedFocusPoint;
        private Vector3 requestedFocusPoint;
        private float currentZoom;
        private bool hasRequestedFocus;

        private void Start()
        {
            PrepareCamera();
            ApplyCameraTransform(true);
        }

        public override void EnableCamera(Transform oldCamera, GameCameraType oldCameraType)
        {
            PrepareCamera();
            ApplyCameraTransform(true);
        }

        public void SetFocusPoint(Vector3 focusPoint)
        {
            requestedFocusPoint = ClampToBounds(focusPoint);
            hasRequestedFocus = true;
            unsnappedFocusPoint = requestedFocusPoint;
            ApplyCameraTransform(true);
        }

        private void Update()
        {
            CharacterControls movementInput = InputService.characterControls;
            if (movementInput == null)
                return;

            Vector2 input = CharacterMovement.GetAdaptedMoveVector(movementInput);
            float inputMagnitude = movementInput.analogMovement ? Mathf.Clamp01(input.magnitude) : 1f;
            input = input.sqrMagnitude > 0f ? input.normalized * inputMagnitude : Vector2.zero;

            Vector3 screenRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            Vector3 screenUp = Vector3.ProjectOnPlane(transform.up, Vector3.up).normalized;
            if (screenRight.sqrMagnitude < Mathf.Epsilon)
                screenRight = Vector3.right;
            if (screenUp.sqrMagnitude < Mathf.Epsilon)
                screenUp = Vector3.forward;

            unsnappedFocusPoint += (screenRight * input.x + screenUp * input.y) * moveSpeed * Time.deltaTime;
            unsnappedFocusPoint = ClampToBounds(unsnappedFocusPoint);
        }

        private void LateUpdate()
        {
            ApplyCameraTransform(false);
        }

        private void OnValidate()
        {
            gridSize = Mathf.Max(0.0001f, gridSize);
            cameraDistance = Mathf.Max(1f, cameraDistance);
            maximumBounds = Vector2.Max(minimumBounds, maximumBounds);
        }

        private void PrepareCamera()
        {
            zoomComponent ??= GetComponent<ZoomComponent>();
            controlledCamera ??= GetComponent<Camera>();
            transform.rotation = Quaternion.Euler(viewEulerAngles);
            unsnappedFocusPoint = hasRequestedFocus
                ? requestedFocusPoint
                : ClampToBounds(initialFocusPoint);
            currentZoom = zoomComponent.DesiredZoom > 0f
                ? zoomComponent.DesiredZoom
                : zoomComponent.InitialZoom;

            if (controlledCamera != null)
                controlledCamera.orthographic = true;
        }

        private void ApplyCameraTransform(bool immediate)
        {
            currentZoom = zoomComponent.UpdateZoom(currentZoom, zoomComponent.MaxZoom, -1f, immediate);

            Vector3 focusPoint = snapToGrid ? SnapToGrid(unsnappedFocusPoint) : unsnappedFocusPoint;
            if (controlledCamera != null && controlledCamera.orthographic)
                controlledCamera.orthographicSize = currentZoom;
            transform.position = focusPoint - transform.forward * cameraDistance;
        }

        private Vector3 ClampToBounds(Vector3 point)
        {
            point.x = Mathf.Clamp(point.x, minimumBounds.x, maximumBounds.x);
            point.z = Mathf.Clamp(point.z, minimumBounds.y, maximumBounds.y);
            return point;
        }

        private Vector3 SnapToGrid(Vector3 point)
        {
            point.x = gridOrigin.x + Mathf.Round((point.x - gridOrigin.x) / gridSize) * gridSize;
            point.z = gridOrigin.y + Mathf.Round((point.z - gridOrigin.y) / gridSize) * gridSize;
            return ClampToBounds(point);
        }
    }
}
#endif
