using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI {
    /// <summary>
    /// Sizes a scroll view to its content, up to <see cref="maxHeight"/>, so a panel around it
    /// (with a ContentSizeFitter) grows with a short list and scrolls a long one.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(LayoutElement))]
    public class ScrollHeightFitter : MonoBehaviour {
        [SerializeField] RectTransform content;
        [SerializeField, Min(0f)] float minHeight = 0f;
        [SerializeField, Min(0f)] float maxHeight = 360f;

        LayoutElement element;

        void LateUpdate() {
            if (content == null)
                return;
            if (element == null)
                element = GetComponent<LayoutElement>();
            float height = Mathf.Clamp(content.rect.height, minHeight, maxHeight);
            if (Mathf.Abs(element.preferredHeight - height) < 0.5f)
                return;
            element.preferredHeight = height;
            element.minHeight = height;
        }
    }
}
