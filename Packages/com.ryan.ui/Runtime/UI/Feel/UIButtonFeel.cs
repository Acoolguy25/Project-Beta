using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RyanAssets.UI.Feel {
    /// <summary>
    /// Makes a button feel pressable: a small grow on hover, a shrink while held, and the theme's
    /// hover and click sounds (<see cref="UISound"/>). Added to every shared themed button by
    /// <c>UIKit.Clickable</c>; scales and timings come from <see cref="UITheme"/> unless overridden here.
    /// Scaling never moves layout, so it is safe inside layout groups.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIButtonFeel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler {
        [Tooltip("The button whose interactable state gates the feedback. Empty = the Selectable on this object.")]
        [SerializeField] Selectable selectable;
        [Tooltip("What scales. Empty = this object.")]
        [SerializeField] Transform scaleTarget;
        [SerializeField] bool playSounds = true;
        [Tooltip("Overrides the theme's hover sound when set.")]
        [SerializeField] AudioClip hoverSound;
        [Tooltip("Overrides the theme's click sound when set.")]
        [SerializeField] AudioClip clickSound;

        bool hovered, pressed, animating;
        Vector3 baseScale = Vector3.one;
        bool hasBase;

        Transform Target => scaleTarget != null ? scaleTarget : transform;
        bool Interactable => selectable == null || selectable.IsInteractable();

        void Awake() {
            // Stays enabled: the EventSystem skips disabled behaviours, so it would never hear the pointer.
            if (selectable == null)
                selectable = GetComponent<Selectable>();
        }

        void CaptureBase() {
            if (hasBase)
                return;
            baseScale = Target.localScale;
            hasBase = true;
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (!Interactable)
                return;
            hovered = true;
            Animate();
            if (playSounds) {
                if (hoverSound != null) UISound.Play(hoverSound, UITheme.Active.buttonHoverVolume);
                else UISound.Hover();
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            hovered = false;
            pressed = false;
            Animate();
        }

        public void OnPointerDown(PointerEventData eventData) {
            if (eventData.button != PointerEventData.InputButton.Left || !Interactable)
                return;
            pressed = true;
            Animate();
        }

        public void OnPointerUp(PointerEventData eventData) {
            pressed = false;
            Animate();
        }

        public void OnPointerClick(PointerEventData eventData) {
            if (eventData.button != PointerEventData.InputButton.Left || !Interactable || !playSounds)
                return;
            if (clickSound != null) UISound.Play(clickSound, UITheme.Active.buttonClickVolume);
            else UISound.Click();
        }

        void Animate() {
            CaptureBase();
            animating = true;
        }

        float TargetScale() {
            UITheme theme = UITheme.Active;
            if (pressed)
                return theme.buttonPressScale;
            return hovered && Interactable ? theme.buttonHoverScale : 1f;
        }

        void Update() {
            if (!animating)
                return;
            UITheme theme = UITheme.Active;
            Transform target = Target;
            float current = baseScale.x != 0f ? target.localScale.x / baseScale.x : 1f;
            float goal = TargetScale();
            float step = Time.unscaledDeltaTime / theme.buttonScaleSeconds * Mathf.Max(0.01f, Mathf.Abs(theme.buttonHoverScale - theme.buttonPressScale));
            float next = Mathf.MoveTowards(current, goal, step);
            target.localScale = baseScale * next;
            if (Mathf.Approximately(next, goal) && goal == 1f)
                animating = false;
        }

        void OnDisable() {
            // Hidden mid-press (a panel closing under the pointer): never leave a button shrunk.
            if (hasBase && Target != null)
                Target.localScale = baseScale;
            hovered = pressed = animating = false;
        }
    }
}
