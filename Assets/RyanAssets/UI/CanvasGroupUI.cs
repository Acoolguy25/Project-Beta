using UnityEngine;

namespace RyanAssets.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public class CanvasGroupController : MonoBehaviour {
        CanvasGroup canvasGroup;
        
        float timer, targetTime;
        float startAlpha, targetAlpha;

        void Awake(){
            canvasGroup = GetComponent<CanvasGroup>();
            targetAlpha = canvasGroup.alpha;
        }
        private void SetAlpha(float alpha){
            canvasGroup.alpha = alpha;
            canvasGroup.interactable = alpha != 0f;
            canvasGroup.blocksRaycasts = canvasGroup.interactable;
        }
        private void UpdateTimer(){
            SetAlpha(Mathf.Lerp(startAlpha, targetAlpha, timer == targetTime? 1f: timer/targetTime));
        }
        public void TweenAlpha(float newAlpha, float duration = 0f){
            if (newAlpha == targetAlpha) // Ignore same set
                return;
            startAlpha = canvasGroup.alpha;
            targetAlpha = newAlpha;
            timer = 0f;
            targetTime = duration;
            UpdateTimer();
        }
        public void SetVisible(bool visible, float duration = 0f){
            TweenAlpha(visible? 1f: 0f, duration);
        }

        void Update(){
            timer += Time.deltaTime;
            UpdateTimer();
        }
    }
}