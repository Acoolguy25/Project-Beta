using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// Stops drawing a big moving prop while the camera is right up against it, the way the dressing's
    /// shader fades near the camera: a crusher passing just over the player's head otherwise fills half
    /// the screen with a blurry face. Solid or gone, never half: it hides inside <see cref="hideWithin"/>
    /// metres of the camera and shows again past <see cref="showBeyond"/> (the gap keeps it from flickering
    /// at the edge). Only drawing stops; colliders and hazards are untouched. Does nothing without a camera
    /// (a server).
    /// </summary>
    public sealed class OB_NearCameraHide : MonoBehaviour {
        [Tooltip("Root of the renderers to hide (this object when empty).")]
        public Transform visual;
        [Tooltip("Hides once the camera is this close to the visual's bounds (metres).")]
        [Min(0f)] public float hideWithin = 1.8f;
        [Tooltip("Shows again once the camera is this far from the visual's bounds (metres, above hideWithin).")]
        [Min(0f)] public float showBeyond = 2.6f;

        Renderer[] renderers;
        bool hidden;
        Camera cached;

        void Awake() => renderers = (visual != null ? visual : transform).GetComponentsInChildren<Renderer>(true);

        void OnDisable() => SetHidden(false);

        void LateUpdate() {
            if (cached == null || !cached.isActiveAndEnabled) cached = Camera.main;
            if (cached == null || renderers.Length == 0) return;
            Vector3 eye = cached.transform.position;
            // The visual's own renderer comes first and spans it (the face and studs sit on its surface).
            Bounds bounds = renderers[0].bounds;
            float distance = Mathf.Sqrt(bounds.SqrDistance(eye));
            if (!hidden && distance < hideWithin) SetHidden(true);
            else if (hidden && distance > Mathf.Max(showBeyond, hideWithin)) SetHidden(false);
        }

        void SetHidden(bool value) {
            if (hidden == value || renderers == null) return;
            hidden = value;
            foreach (var renderer in renderers)
                if (renderer != null) renderer.forceRenderingOff = value;
        }
    }
}
