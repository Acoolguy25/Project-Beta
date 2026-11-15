using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>A tower in the bottom shop: portrait, name, price and tags. Lifts and glows when selected.</summary>
    public sealed class TD_ShopCard : MonoBehaviour {
        public RectTransform body;
        public Image outline, portrait, costIcon;
        public TMP_Text costLabel;
        public CanvasGroup group;
        [Min(0f)] public float selectedLift = 10f;
        [Range(0f, 1f)] public float unaffordableAlpha = 0.55f;

        bool selected;
        Color accent, danger, normalCost;

        public void Init(Color accentColor, Color dangerColor, Color costColor) {
            accent = accentColor;
            danger = dangerColor;
            normalCost = costColor;
        }

        public void Set(bool isSelected, bool affordable) {
            selected = isSelected;
            Color edge = accent;
            edge.a = isSelected ? 1f : 0f;
            outline.color = edge;
            costLabel.color = affordable ? normalCost : danger;
            costIcon.color = affordable ? normalCost : danger;
            group.alpha = affordable || isSelected ? 1f : unaffordableAlpha;
        }

        void Update() {
            float y = selected ? selectedLift : 0f;
            Vector2 p = body.anchoredPosition;
            p.y = Mathf.Lerp(p.y, y, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            body.anchoredPosition = p;
        }
    }
}
