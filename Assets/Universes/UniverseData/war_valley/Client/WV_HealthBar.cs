using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// One small overhead health bar, with a name line that only appears when the commander hovers
    /// or selects what it belongs to. Authored as a template inside the HUD prefab and pooled by
    /// <see cref="WV_HealthBarLayer"/>, which positions it over its soldier every frame.
    /// </summary>
    public sealed class WV_HealthBar : MonoBehaviour {
        [Tooltip("The filled image showing remaining health (Image.Type Filled, horizontal).")]
        [SerializeField] Image fill;
        [Tooltip("The name line above the bar. Hidden unless the soldier is hovered or selected.")]
        [SerializeField] TextMeshProUGUI nameLabel;
        [Tooltip("Fades the whole bar with distance.")]
        [SerializeField] CanvasGroup group;

        RectTransform rect;
        string shownName;

        public RectTransform Rect => rect != null ? rect : rect = (RectTransform)transform;

        /// <summary>Sets the fill to <paramref name="fraction"/> of the bar, in <paramref name="color"/>.</summary>
        public void SetHealth(float fraction, Color color) {
            if (fill == null)
                return;
            fill.fillAmount = Mathf.Clamp01(fraction);
            fill.color = color;
        }

        /// <summary>Shows <paramref name="displayName"/> over the bar, or hides the name when null.</summary>
        public void SetName(string displayName, Color color) {
            if (nameLabel == null)
                return;
            bool show = !string.IsNullOrEmpty(displayName);
            if (nameLabel.gameObject.activeSelf != show)
                nameLabel.gameObject.SetActive(show);
            if (!show)
                return;
            // Names change rarely; skipping the assignment avoids a mesh rebuild every frame.
            if (!ReferenceEquals(shownName, displayName)) {
                shownName = displayName;
                nameLabel.text = displayName;
            }
            nameLabel.color = color;
        }

        /// <summary>Places the bar at a canvas position, scaled and faded for distance.</summary>
        public void Place(Vector2 canvasPosition, float scale, float alpha) {
            RectTransform target = Rect;
            target.anchoredPosition = canvasPosition;
            target.localScale = new Vector3(scale, scale, 1f);
            if (group != null)
                group.alpha = alpha;
        }

        public void SetVisible(bool visible) {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }
    }
}
