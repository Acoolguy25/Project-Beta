using UnityEngine;

namespace RyanAssets.Rounds {
    /// <summary>Turns world-space labels (prices, names, scores) to face the main camera. Presentation only.</summary>
    public sealed class Billboard : MonoBehaviour {
        [Tooltip("Only turn around the vertical axis, so signs stay upright.")]
        public bool yawOnly = true;
        Camera cached;

        void LateUpdate() {
            if (cached == null || !cached.isActiveAndEnabled) cached = Camera.main;
            if (cached == null) return;
            Vector3 away = transform.position - cached.transform.position;
            if (yawOnly) away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
