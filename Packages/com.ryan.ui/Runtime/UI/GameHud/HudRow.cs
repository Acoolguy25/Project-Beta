using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI.GameHud {
    /// <summary>One row of a <see cref="HudRows"/> list: zebra stripe, leading icon and text cells.</summary>
    public sealed class HudRow : MonoBehaviour {
        [Tooltip("Background shown on every other row.")]
        public Image stripe;
        public Image icon;
        public TMP_Text[] cells;

        public void Set(int cell, string text, Color? color = null) {
            if (cells == null || cell < 0 || cell >= cells.Length || cells[cell] == null)
                return;
            cells[cell].text = text;
            if (color.HasValue)
                cells[cell].color = color.Value;
        }

        public void SetIcon(Sprite sprite, Color color) {
            if (icon == null)
                return;
            icon.enabled = sprite != null;
            icon.sprite = sprite;
            icon.color = color;
        }
    }
}
