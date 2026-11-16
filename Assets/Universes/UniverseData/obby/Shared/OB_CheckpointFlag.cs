using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// Raises a checkpoint pad's flag up its pole once the local player has reached it, with a little
    /// overshoot, and keeps it lowered until then. Presentation only; the server decides checkpoints.
    /// Lives in the shared assembly because the pad prefab is also built on the server, where it idles.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OB_CheckpointFlag : MonoBehaviour {
        [Tooltip("The flag cloth that slides along the pole.")]
        public Transform flag;
        [Tooltip("Local height of the flag's centre while the checkpoint is not reached.")]
        public float loweredHeight = 1f;
        [Tooltip("Local height of the flag's centre once reached (the top of the pole).")]
        public float raisedHeight = 3f;
        [Tooltip("Seconds the raise takes.")]
        [Min(0.01f)] public float raiseSeconds = 0.9f;
        [Tooltip("Overshoot of the raise (0 = none), as with an ease-out-back curve.")]
        [Range(0f, 3f)] public float overshoot = 1.4f;

        bool raised;
        float from, to, start = -1f;

        public bool Raised => raised;

        /// <summary>Raises or lowers the flag, animated or at once.</summary>
        public void SetRaised(bool value, bool animate) {
            if (flag == null || (raised == value && start < 0f)) {
                raised = value;
                return;
            }
            raised = value;
            from = flag.localPosition.y;
            to = value ? raisedHeight : loweredHeight;
            if (animate && Application.isPlaying) {
                start = Time.time;
            } else {
                start = -1f;
                SetHeight(to);
            }
        }

#if !UNITY_SERVER
        void Update() {
            if (start < 0f || flag == null) return;
            float t = Mathf.Clamp01((Time.time - start) / raiseSeconds);
            float c3 = overshoot + 1f;
            float eased = 1f + c3 * Mathf.Pow(t - 1f, 3f) + overshoot * Mathf.Pow(t - 1f, 2f);
            SetHeight(Mathf.LerpUnclamped(from, to, eased));
            if (t >= 1f) start = -1f;
        }
#endif

        void SetHeight(float y) {
            Vector3 position = flag.localPosition;
            position.y = y;
            flag.localPosition = position;
        }
    }
}
