using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>One line of the tag feed: an icon and a rich-text message.</summary>
    public sealed class HS_FeedRow : MonoBehaviour {
        public Image icon;
        public TMP_Text label;

        public void Set(string text, Sprite sprite) {
            label.text = text;
            if (sprite != null) icon.sprite = sprite;
        }
    }
}
