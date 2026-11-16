using UnityEngine;

namespace RyanAssets.UI {
    /// <summary>
    /// Keeps a drop shadow (a sibling behind <see cref="target"/>) the size of its panel plus the
    /// blur margin, so the shadow is right for panels that size themselves to their content.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public class UIShadowFollower : MonoBehaviour {
        [SerializeField] RectTransform target;
        [Tooltip("How far the shadow reaches past the panel on each side.")]
        [SerializeField, Min(0f)] float blur = 28f;
        [Tooltip("How far the shadow sits below the panel.")]
        [SerializeField] float offsetY = 8f;

        RectTransform rect;

        void LateUpdate() {
            if (target == null)
                return;
            if (rect == null)
                rect = (RectTransform)transform;
            // A shadow may also be a child of its panel (HudKit cards, so it fades and hides with them):
            // then it is placed on the panel's own pivot, not at the panel's position in its parent.
            bool child = rect.parent == target;
            Vector2 size = target.rect.size + Vector2.one * (blur * 2f);
            Vector2 position = (child ? Vector2.zero : target.anchoredPosition) + new Vector2(0f, -offsetY);
            Vector2 anchor = child ? target.pivot : target.anchorMin == target.anchorMax ? target.anchorMin : new Vector2(0.5f, 0.5f);
            if (rect.sizeDelta == size && rect.anchoredPosition == position && rect.anchorMin == anchor)
                return;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = target.pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }
    }
}
