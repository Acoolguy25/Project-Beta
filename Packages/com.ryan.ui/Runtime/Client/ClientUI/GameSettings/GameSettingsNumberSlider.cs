using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.GameSettings {
    /// <summary>A slider paired with a small number box; editing either updates the other.</summary>
    [DisallowMultipleComponent]
    public class GameSettingsNumberSlider : MonoBehaviour {
        [SerializeField] public Slider slider;
        [SerializeField] TMP_InputField numberInput;
        [SerializeField] string numberFormat = "0.##";
        [SerializeField] bool wholeNumbers;

        bool updating;

        public float Value {
            get => slider != null ? slider.value : 0f;
            set => SetValue(value);
        }

        void Reset() {
            slider = GetComponentInChildren<Slider>(true);
            numberInput = GetComponentInChildren<TMP_InputField>(true);
        }

        void Awake() {
            ApplyWholeNumberMode();
            RefreshInput(Value);
        }

        void OnEnable() {
            if (slider != null)
                slider.onValueChanged.AddListener(HandleSliderChanged);
            if (numberInput != null)
                numberInput.onEndEdit.AddListener(HandleInputSubmitted);
            RefreshInput(Value);
        }

        void OnDisable() {
            if (slider != null)
                slider.onValueChanged.RemoveListener(HandleSliderChanged);
            if (numberInput != null)
                numberInput.onEndEdit.RemoveListener(HandleInputSubmitted);
        }

        public void SetRange(float minValue, float maxValue, bool useWholeNumbers = false) {
            wholeNumbers = useWholeNumbers;
            if (slider == null)
                return;
            slider.wholeNumbers = wholeNumbers;
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            SetValue(slider.value);
            ApplyWholeNumberMode();
        }

        /// <summary>Sets the value without raising the slider's change event.</summary>
        public void SetValue(float value) {
            if (slider == null) {
                RefreshInput(value);
                return;
            }
            float clamped = Mathf.Clamp(value, slider.minValue, slider.maxValue);
            if (wholeNumbers || slider.wholeNumbers)
                clamped = Mathf.Round(clamped);

            updating = true;
            slider.SetValueWithoutNotify(clamped);
            RefreshInput(clamped);
            updating = false;
        }

        void HandleSliderChanged(float value) {
            if (!updating)
                RefreshInput(value);
        }

        // Typed values go through the slider so listeners hear about them like a drag.
        void HandleInputSubmitted(string text) {
            if (updating || slider == null)
                return;
            if (!TryParseNumber(text, out float value)) {
                RefreshInput(Value);
                return;
            }
            float clamped = Mathf.Clamp(value, slider.minValue, slider.maxValue);
            if (wholeNumbers)
                clamped = Mathf.Round(clamped);
            slider.value = clamped;
            RefreshInput(clamped);
        }

        void RefreshInput(float value) {
            if (numberInput == null)
                return;
            string format = string.IsNullOrWhiteSpace(numberFormat) ? "0.##" : numberFormat;
            numberInput.SetTextWithoutNotify(value.ToString(format, CultureInfo.InvariantCulture));
        }

        void ApplyWholeNumberMode() {
            if (slider != null)
                slider.wholeNumbers = wholeNumbers;
            if (numberInput != null)
                numberInput.contentType = wholeNumbers ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.DecimalNumber;
        }

        static bool TryParseNumber(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}
