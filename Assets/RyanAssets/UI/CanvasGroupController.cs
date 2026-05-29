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

        void Awake() {
            if (canvasGroup != null)
                return; // already initalized
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
            SetAlpha(Mathf.Lerp(startAlpha, targetAlpha, timer == targetTime ? 1f : timer / targetTime));
        }
        public void TweenAlpha(float newAlpha, float duration = 0f) {
            if (newAlpha == targetAlpha) // Ignore same set
                return;
            startAlpha = canvasGroup.alpha;
            targetAlpha = newAlpha;
            timer = 0f;
            targetTime = duration;
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