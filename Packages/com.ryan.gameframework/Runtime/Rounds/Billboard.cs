using UnityEngine;

namespace RyanAssets.Rounds {
    /// <summary>
    /// Turns world-space labels (prices, names, scores) to face the main camera, and can cap how
    /// large they get on screen. Presentation only.
    /// </summary>
    public sealed class Billboard : MonoBehaviour {
        public enum Facing {
            /// <summary>Look at the camera position (the original behaviour; skews near screen edges).</summary>
            LookAtCamera,
            /// <summary>Align with the camera plane, so every label reads flat whatever its place on screen.</summary>
            CameraPlane,
        }

        [Tooltip("LookAtCamera: point at the camera position. CameraPlane: align with the camera's view plane (no skew near the screen edges).")]
        public Facing facing = Facing.LookAtCamera;
        [Tooltip("Only turn around the vertical axis, so signs stay upright.")]
        public bool yawOnly = true;
        [Tooltip("Largest share of the screen height the label may cover (0 = no cap). Scales the object down at close range, never up.")]
        [Range(0f, 1f)] public float maxScreenHeight;
        [Tooltip("World height of the label at its authored scale; used by the screen-height cap.")]
        [Min(0.01f)] public float referenceHeight = 1f;

        Camera cached;
        Vector3 baseScale;
        bool scaleCaptured;

        void OnEnable() {
            if (!scaleCaptured) {
                baseScale = transform.localScale;
                scaleCaptured = true;
            }
        }

        void LateUpdate() {
            if (cached == null || !cached.isActiveAndEnabled) cached = Camera.main;
            if (cached == null) return;
            Transform cam = cached.transform;
            Vector3 away = facing == Facing.CameraPlane ? cam.forward : transform.position - cam.position;
            if (yawOnly) away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            if (maxScreenHeight > 0f) CapScale(cam);
        }

        void CapScale(Transform cam) {
            float depth = Vector3.Dot(transform.position - cam.position, cam.forward);
            if (depth <= 0.01f) return;
            float viewHeight = cached.orthographic
                ? cached.orthographicSize * 2f
                : 2f * depth * Mathf.Tan(cached.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float parentScale = transform.parent != null ? Mathf.Abs(transform.parent.lossyScale.y) : 1f;
            float height = referenceHeight * Mathf.Abs(baseScale.y) * parentScale;
            float limit = maxScreenHeight * viewHeight;
            float factor = height > limit && height > 0f ? limit / height : 1f;
            transform.localScale = baseScale * factor;
        }
    }
}
