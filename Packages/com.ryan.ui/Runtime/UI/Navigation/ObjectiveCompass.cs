using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RyanAssets.UI.Navigation {
    /// <summary>
    /// Displays the closest world-space objective to the player camera.
    /// Supplying an empty list disables the compass display.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveCompass : MonoBehaviour {
        [Header("Presentation")]
        [SerializeField] GameObject displayRoot;
        [SerializeField] RectTransform directionPointer;
        [SerializeField] TMP_Text readout;
        [SerializeField] string readoutPrefix = "OBJECTIVE";

        [Header("Tracking")]
        [Tooltip("Defaults to Camera.main when not assigned.")]
        [SerializeField] Camera playerCamera;

        readonly List<Vector3> objectives = new();
        bool displayAllowed = true;

        public IReadOnlyList<Vector3> Objectives => objectives;
        public bool HasObjective { get; private set; }
        public Vector3 CurrentObjective { get; private set; }
        public float CurrentDistance { get; private set; }

        public void Configure(GameObject root, RectTransform pointer, TMP_Text label, string prefix = "OBJECTIVE") {
            displayRoot = root;
            directionPointer = pointer;
            readout = label;
            readoutPrefix = prefix;
            Refresh();
        }

        public void SetPlayerCamera(Camera camera) {
            playerCamera = camera;
            Refresh();
        }

        public void SetObjectives(IReadOnlyList<Vector3> positions) {
            objectives.Clear();

            if (positions != null) {
                for (int i = 0; i < positions.Count; i++) {
                    Vector3 position = positions[i];

                    if (IsFinite(position))
                        objectives.Add(position);
                }
            }

            Refresh();
        }

        public void ClearObjectives() {
            objectives.Clear();
            Refresh();
        }

        public void SetDisplayAllowed(bool allowed) {
            if (displayAllowed == allowed)
                return;

            displayAllowed = allowed;
            Refresh();
        }

        void LateUpdate() => Refresh();

        public void Refresh() {
            Camera camera = playerCamera != null ? playerCamera : Camera.main;
            Transform cameraTransform = camera != null ? camera.transform : null;

            Vector3 closest = default;
            float distance = 0f;

            HasObjective = displayAllowed && cameraTransform != null
                && TryGetClosest(cameraTransform.position, objectives, out closest, out distance);

            SetDisplayVisible(HasObjective);

            if (!HasObjective) {
                CurrentObjective = default;
                CurrentDistance = 0f;
                return;
            }

            CurrentObjective = closest;
            CurrentDistance = distance;

            Vector3 direction = Vector3.ProjectOnPlane(
                closest - cameraTransform.position,
                Vector3.up
            );

            Vector3 heading = Vector3.ProjectOnPlane(
                cameraTransform.forward,
                Vector3.up
            );

            if (heading.sqrMagnitude < 0.0001f)
                heading = Vector3.forward;

            if (direction.sqrMagnitude < 0.0001f)
                direction = heading;

            float relativeBearing = Vector3.SignedAngle(
                heading,
                direction,
                Vector3.up
            );

            if (directionPointer != null)
                directionPointer.localRotation = Quaternion.Euler(0f, 0f, -relativeBearing);

            if (readout != null) {
                string relativeDirection = RelativeDirection(relativeBearing);
                string prefix = string.IsNullOrWhiteSpace(readoutPrefix)
                    ? string.Empty
                    : readoutPrefix.Trim() + "   ";

                readout.text = $"{prefix}{relativeDirection}   {Mathf.CeilToInt(distance)} m";
            }
        }

        public static bool TryGetClosest(
            Vector3 origin,
            IReadOnlyList<Vector3> positions,
            out Vector3 closest,
            out float distance) {
            closest = default;
            distance = 0f;

            if (positions == null || positions.Count == 0)
                return false;

            float bestDistanceSquared = float.PositiveInfinity;

            for (int i = 0; i < positions.Count; i++) {
                Vector3 candidate = positions[i];

                if (!IsFinite(candidate))
                    continue;

                float candidateDistanceSquared = (candidate - origin).sqrMagnitude;

                if (candidateDistanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = candidateDistanceSquared;
                closest = candidate;
            }

            if (float.IsPositiveInfinity(bestDistanceSquared))
                return false;

            distance = Mathf.Sqrt(bestDistanceSquared);
            return true;
        }

        static string RelativeDirection(float bearing) {
            float absoluteBearing = Mathf.Abs(bearing);

            if (absoluteBearing < 45f)
                return "FORWARD";

            if (absoluteBearing > 135f)
                return "BEHIND";

            return bearing > 0f ? "RIGHT" : "LEFT";
        }

        void SetDisplayVisible(bool visible) {
            if (displayRoot != null &&
                displayRoot != gameObject &&
                displayRoot.activeSelf != visible) {
                displayRoot.SetActive(visible);
            }
        }

        static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}