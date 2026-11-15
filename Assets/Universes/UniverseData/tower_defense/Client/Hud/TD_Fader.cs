using UnityEngine;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>Fades and slightly scales a CanvasGroup in and out; never blocks clicks (the HUD is display-only).</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class TD_Fader : MonoBehaviour {
        [Min(0.01f)] public float duration = 0.18f;
        [Tooltip("Scale the panel starts from when it appears.")]
        [Range(0.5f, 1f)] public float popScale = 0.94f;

        CanvasGroup group;
        float target;

        public bool Visible => target > 0.5f;

        void Awake() {
            group = GetComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;
            target = group.alpha;
        }

        public void Set(bool visible, bool instant = false) {
            if (group == null) Awake();
            float wanted = visible ? 1f : 0f;
            if (visible && !Visible) transform.localScale = Vector3.one * popScale;
            target = wanted;
            if (instant) {
                group.alpha = wanted;
                transform.localScale = Vector3.one;
            }
        }

        void Update() {
            float step = Time.unscaledDeltaTime / duration;
            group.alpha = Mathf.MoveTowards(group.alpha, target, step);
            transform.localScale = Vector3.MoveTowards(transform.localScale, Vector3.one, step * (1f - popScale));
        }
    }
}
