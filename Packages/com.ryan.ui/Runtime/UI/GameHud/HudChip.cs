using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI.GameHud {
    /// <summary>A small pill with an icon and a value ("12", "02:30", "Red team"), built by HudKit.</summary>
    [DisallowMultipleComponent]
    public sealed class HudChip : MonoBehaviour {
        public Image background;
        public Image icon;
        public TMP_Text label;

        public string Text {
            get => label != null ? label.text : string.Empty;
            set { if (label != null && label.text != value) label.text = value; }
        }

        public void SetIcon(Sprite sprite) {
            if (icon != null && sprite != null)
                icon.sprite = sprite;
        }

        /// <summary>Colours the icon (and, when <paramref name="text"/> is set, the label too).</summary>
        public void SetTint(Color iconColor, Color? text = null) {
            if (icon != null)
                icon.color = iconColor;
            if (label != null && text.HasValue)
                label.color = text.Value;
        }

        public void SetVisible(bool visible) {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }
    }
}
