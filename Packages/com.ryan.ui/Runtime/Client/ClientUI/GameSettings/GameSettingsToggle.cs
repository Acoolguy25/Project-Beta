using System;
using RyanAssets.UI;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.GameSettings {
    /// <summary>An on/off switch: a pill track with a knob that slides across. Colours come from the active <see cref="UITheme"/>.</summary>
    [DisallowMultipleComponent]
    public class GameSettingsToggle : MonoBehaviour {
        [SerializeField] Button button;
        [SerializeField] Image track;
        [SerializeField] RectTransform knob;
        [SerializeField] bool value;
        [Tooltip("Distance between the knob's centre and the track's end.")]
        [SerializeField] float knobInset = 13f;
        [SerializeField, Min(0f)] float slideDuration = 0.12f;

        public event Action<bool> onValueChanged;

        float knobPosition; // 0 = off, 1 = on

        public bool Value {
            get => value;
            set => SetValue(value);
        }

        void Reset() {
            button = GetComponent<Button>();
            track = GetComponent<Image>();
            if (transform.childCount > 0)
                knob = transform.GetChild(0) as RectTransform;
        }

        void Awake() {
            knobPosition = value ? 1f : 0f;
            RefreshVisuals();
        }

        void OnEnable() {
            if (button != null)
                button.onClick.AddListener(Switch);
            knobPosition = value ? 1f : 0f;
            RefreshVisuals();
        }

        void OnDisable() {
            if (button != null)
                button.onClick.RemoveListener(Switch);
        }

        void Update() {
            float target = value ? 1f : 0f;
            if (Mathf.Approximately(knobPosition, target))
                return;
            knobPosition = slideDuration <= 0f ? target : Mathf.MoveTowards(knobPosition, target, Time.unscaledDeltaTime / slideDuration);
            RefreshVisuals();
        }

        public void Switch() => SetValue(!value);

        public void SetValue(bool newValue) => SetValue(newValue, true);

        public void SetValue(bool newValue, bool notify) {
            if (value == newValue)
                return;
            value = newValue;
            if (!isActiveAndEnabled)
                knobPosition = value ? 1f : 0f;
            RefreshVisuals();
            if (notify)
                onValueChanged?.Invoke(value);
        }

        void RefreshVisuals() {
            UITheme theme = UITheme.Active;
            float eased = knobPosition * knobPosition * (3f - 2f * knobPosition);
            if (track != null)
                track.color = Color.Lerp(theme.surfaceHover, theme.accent, eased);
            if (knob == null)
                return;
            knob.anchorMin = knob.anchorMax = new Vector2(eased, 0.5f);
            knob.anchoredPosition = new Vector2(Mathf.Lerp(knobInset, -knobInset, eased), 0f);
        }
    }
}
