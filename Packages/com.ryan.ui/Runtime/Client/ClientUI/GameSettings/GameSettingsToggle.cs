using System;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.GameSettings {
    [DisallowMultipleComponent]
    public class GameSettingsToggle: MonoBehaviour {
        [SerializeField] Button button;
        [SerializeField] Image backgroundImage;
        [SerializeField] RectTransform knob;
        [SerializeField] bool value;
        [SerializeField] Color onColor = new(0.08f, 0.62f, 0.28f, 1f);
        [SerializeField] Color offColor = new(0.32f, 0.32f, 0.36f, 1f);
        [SerializeField] float knobInset = 18f;

        public event Action<bool> onValueChanged;

        public bool Value {
            get => value;
            set => SetValue(value);
        }

        void Reset() {
            button = GetComponent<Button>();
            backgroundImage = GetComponent<Image>();

            if (transform.childCount > 0) {
                knob = transform.GetChild(0) as RectTransform;
            }
        }

        void Awake() {
            RefreshVisuals();
        }

        void OnEnable() {
            if (button != null) {
                button.onClick.AddListener(Switch);
            }

            RefreshVisuals();
        }

        void OnDisable() {
            if (button != null) {
                button.onClick.RemoveListener(Switch);
            }
        }

        public void Switch() {
            SetValue(!value);
        }

        public void SetValue(bool newValue) {
            SetValue(newValue, true);
        }

        public void SetValue(bool newValue, bool notify) {
            if (value == newValue) {
                RefreshVisuals();
                return;
            }

            value = newValue;
            RefreshVisuals();

            if (notify) {
                onValueChanged?.Invoke(value);
            }
        }

        void RefreshVisuals() {
            if (backgroundImage != null) {
                backgroundImage.color = value ? onColor : offColor;
            }

            if (knob == null) {
                return;
            }

            knob.anchorMin = new Vector2(value ? 1f : 0f, 0.5f);
            knob.anchorMax = new Vector2(value ? 1f : 0f, 0.5f);
            knob.anchoredPosition = new Vector2(value ? -knobInset : knobInset, 0f);
        }
    }
}
