using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI.GameHud {
    /// <summary>
    /// A centred announcement ("VICTORY", "EMERGENCY MEETING"): an emblem icon in an accent-coloured
    /// disc, a title and a subtitle. Built by HudKit.Banner; games call <see cref="Show"/>.
    /// </summary>
    [RequireComponent(typeof(HudPopup))]
    public sealed class HudBanner : MonoBehaviour {
        public Image emblem;
        public Image emblemIcon;
        public Image accentStripe;
        [Tooltip("Optional small line above the title; authored text, not changed by Show.")]
        public TMP_Text kicker;
        public TMP_Text title;
        public TMP_Text subtitle;

        HudPopup popup;

        public HudPopup Popup => popup != null ? popup : popup = GetComponent<HudPopup>();
        public bool Visible => Popup.Visible;

        /// <summary>Shows the banner; it hides itself after <paramref name="seconds"/> (0 = until <see cref="Hide"/>).</summary>
        public void Show(Sprite icon, Color accent, string titleText, string subtitleText = null, float seconds = 4f) {
            Set(icon, accent, titleText, subtitleText);
            Popup.Show(seconds);
        }

        /// <summary>Changes the contents without restarting the animation or timer.</summary>
        public void Set(Sprite icon, Color accent, string titleText, string subtitleText = null) {
            if (emblemIcon != null && icon != null)
                emblemIcon.sprite = icon;
            if (emblem != null)
                emblem.color = accent;
            if (accentStripe != null)
                accentStripe.color = accent;
            if (title != null) {
                title.text = titleText ?? string.Empty;
                title.color = Color.Lerp(accent, Color.white, 0.35f);
            }
            if (subtitle != null) {
                subtitle.text = subtitleText ?? string.Empty;
                subtitle.gameObject.SetActive(!string.IsNullOrEmpty(subtitleText));
            }
        }

        public void Hide() => Popup.Hide();
    }
}
