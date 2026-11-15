using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI.GameHud {
    /// <summary>A rounded progress bar: the fill's right edge follows <see cref="Value"/> (0..1), eased.</summary>
    [DisallowMultipleComponent]
    public sealed class HudBar : MonoBehaviour {
        public RectTransform fill;
        public Image fillImage;
        [Tooltip("How quickly the fill catches up with a new value (0 = instantly).")]
        [Min(0f)] public float smoothing = 10f;

        float shown = -1f, value;

        public float Value {
            get => value;
            set { this.value = Mathf.Clamp01(value); if (shown < 0f || smoothing <= 0f) Apply(this.value); }
        }

        public void SetColor(Color color) {
            if (fillImage != null)
                fillImage.color = color;
        }

        void Update() {
            if (Mathf.Approximately(shown, value))
                return;
            Apply(Mathf.MoveTowards(shown, value, Mathf.Max(0.01f, Mathf.Abs(value - shown)) * smoothing * Time.unscaledDeltaTime));
        }

        void Apply(float v) {
            shown = v;
            if (fill != null) {
                fill.anchorMax = new Vector2(v, 1f);
                fill.gameObject.SetActive(v > 0.001f);
            }
        }
    }
}
