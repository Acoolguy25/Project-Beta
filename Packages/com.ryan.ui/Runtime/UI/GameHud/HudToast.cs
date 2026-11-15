using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI.GameHud {
    /// <summary>One row of a <see cref="HudToastFeed"/>: accent edge, icon and text.</summary>
    [RequireComponent(typeof(HudPopup))]
    public sealed class HudToast : MonoBehaviour {
        public Image accent;
        public Image icon;
        public TMP_Text label;

        HudPopup popup;
        public HudPopup Popup => popup != null ? popup : popup = GetComponent<HudPopup>();

        public void Set(Sprite sprite, Color color, string text) {
            if (icon != null) {
                if (sprite != null) icon.sprite = sprite;
                icon.color = color;
            }
            if (accent != null)
                accent.color = color;
            if (label != null)
                label.text = text;
        }
    }
}
