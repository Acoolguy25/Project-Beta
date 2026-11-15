using UnityEngine;

namespace RyanAssets.UI.GameHud {
    /// <summary>
    /// Shows and hides a HUD element with a short fade and a small "pop" in scale, optionally hiding
    /// itself again after a few seconds. Use it for banners, cards and prompts that come and go, so
    /// they never just blink in and out.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    [DisallowMultipleComponent]
    public sealed class HudPopup : MonoBehaviour {
        [Tooltip("Seconds the fade in or out takes.")]
        [Min(0f)] public float fadeDuration = 0.18f;
        [Tooltip("Scale the element starts from when it appears (1 = no pop).")]
        [Range(0.5f, 1.2f)] public float popFrom = 0.92f;
        [Tooltip("Deactivate the GameObject once fully hidden, so hidden layouts cost nothing.")]
        public bool deactivateWhenHidden = true;

        CanvasGroup group;
        float target, hideAt = float.PositiveInfinity;
        bool initialised;

        /// <summary>True while shown or fading in.</summary>
        public bool Visible => target > 0f;

        void Init() {
            if (initialised)
                return;
            initialised = true;
            group = GetComponent<CanvasGroup>();
            target = gameObject.activeSelf ? group.alpha : 0f;
        }

        void Awake() => Init();

        /// <summary>Shows the element; with <paramref name="seconds"/> &gt; 0 it hides itself after that long.</summary>
        public void Show(float seconds = 0f) {
            Init();
            hideAt = seconds > 0f ? Time.unscaledTime + seconds : float.PositiveInfinity;
            if (target > 0f && gameObject.activeSelf)
                return;
            target = 1f;
            gameObject.SetActive(true);
            enabled = true;
            if (group.alpha <= 0.01f)
                transform.localScale = Vector3.one * popFrom;
        }

        public void Hide() {
            Init();
            hideAt = float.PositiveInfinity;
            if (target <= 0f)
                return;
            target = 0f;
            enabled = true;
        }

        /// <summary>Shows or hides; cheap to call every frame.</summary>
        public void SetVisible(bool visible) {
            if (visible) Show(); else Hide();
        }

        /// <summary>Jumps straight to hidden, with no fade.</summary>
        public void HideImmediate() {
            Init();
            target = 0f;
            hideAt = float.PositiveInfinity;
            group.alpha = 0f;
            transform.localScale = Vector3.one;
            if (deactivateWhenHidden)
                gameObject.SetActive(false);
        }

        void Update() {
            if (Time.unscaledTime >= hideAt)
                Hide();
            float step = fadeDuration <= 0f ? 1f : Time.unscaledDeltaTime / fadeDuration;
            group.alpha = Mathf.MoveTowards(group.alpha, target, step);
            group.blocksRaycasts = group.interactable = target > 0f;
            // Ease the scale with the fade, overshooting a touch on the way in.
            float t = group.alpha;
            float scale = target > 0f
                ? Mathf.LerpUnclamped(popFrom, 1f, EaseOutBack(t))
                : Mathf.Lerp(1f, 0.97f, 1f - t);
            transform.localScale = Vector3.one * scale;
            if (Mathf.Approximately(group.alpha, target) && float.IsPositiveInfinity(hideAt)) {
                transform.localScale = Vector3.one;
                if (target <= 0f && deactivateWhenHidden)
                    gameObject.SetActive(false);
                enabled = false;
            }
        }

        static float EaseOutBack(float t) {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
