using System.ComponentModel;
using UnityEngine;

namespace RyanAssets.UI {
    [RequireComponent(typeof(CanvasGroup))]
    public class CanvasGroupController : MonoBehaviour {
        CanvasGroup canvasGroup;
        [Description("Always Active")]
        public bool alwaysActive;

        float timer, targetTime;
        float startAlpha;
        public float targetAlpha;
        [Tooltip("Scale the content pops in from while fading in from hidden (1 = no pop).")]
        [Range(0.5f, 1f)] public float popFrom = 1f;
        [Tooltip("What pops. Empty = this object. Point it at the window when this object also holds a full-screen scrim.")]
        public Transform popTarget;
        bool popping;
        Transform PopTarget => popTarget != null ? popTarget : transform;
        public bool isVisible => targetAlpha != 0f;
        void Awake() {
            if (canvasGroup != null || gameObject == null)
                return; // already initalized or destroyed
            canvasGroup = GetComponent<CanvasGroup>();
            targetAlpha = canvasGroup.alpha;
            SetAlpha(targetAlpha);
        }
        private void SetAlpha(float alpha) {
            bool newActive = targetAlpha != 0f || alpha != 0f;
            canvasGroup.alpha = alpha;
            canvasGroup.interactable = targetAlpha != 0f;
            canvasGroup.blocksRaycasts = canvasGroup.interactable;
            canvasGroup.gameObject.SetActive(alwaysActive || newActive);
            enabled = targetAlpha != alpha;
        }
        private void UpdateTimer() {
            float t = timer >= targetTime ? 1f : timer / targetTime;
            SetAlpha(Mathf.Lerp(startAlpha, targetAlpha, t));
            if (popping) {
                PopTarget.localScale = Vector3.one * Mathf.LerpUnclamped(popFrom, 1f, EaseOutBack(t));
                if (t >= 1f)
                    popping = false;
            }
        }
        static float EaseOutBack(float t) {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
        public void TweenAlpha(float newAlpha, float duration = 0f) {
            if (canvasGroup == null)
                Awake();
            if (newAlpha == targetAlpha || gameObject == null) // Ignore same set
                return;
            startAlpha = canvasGroup.alpha;
            targetAlpha = newAlpha;
            timer = 0f;
            targetTime = duration;
            // Pop only when appearing from fully hidden; a fade out or a half-way reversal keeps the scale.
            bool pop = popFrom < 1f && duration > 0f && startAlpha <= 0f && newAlpha > 0f;
            if (popping && !pop)
                PopTarget.localScale = Vector3.one;
            popping = pop;
            UpdateTimer();
        }
        public void SetVisible(bool visible, float duration = 0f) {
            TweenAlpha(visible ? 1f : 0f, duration);
        }

        void Update() {
            timer += Time.deltaTime;
            UpdateTimer();
        }
    }
}