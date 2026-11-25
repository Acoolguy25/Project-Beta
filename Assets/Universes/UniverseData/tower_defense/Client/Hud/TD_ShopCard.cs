using TMPro;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// A tower in the bottom shop: portrait, name, price and tags. Lifts and glows when selected. A
    /// tower the player has not unlocked yet shows a level badge ("LV 3") over a darkened portrait,
    /// so the next unlock is always in sight.
    /// </summary>
    public sealed class TD_ShopCard : MonoBehaviour, IPointerClickHandler {
        /// <summary>Raised with <see cref="index"/> when the card is clicked (it arms or drops the tower).</summary>
        public event Action<int> Clicked;
        [Tooltip("Position in the shop (the tower kind).")]
        public int index;
        public RectTransform body;
        public Image outline, portrait, costIcon;
        public TMP_Text costLabel;
        public CanvasGroup group;
        [Min(0f)] public float selectedLift = 10f;
        [Tooltip("An unaffordable card stays opaque (it read as see-through over a pad): its portrait dims to this and its price turns red.")]
        public Color unaffordablePortrait = new(0.55f, 0.55f, 0.58f, 1f);
        [Tooltip("How fast the selected card lifts (1/s).")]
        [Min(0.1f)] public float liftSharpness = 14f;
        [Tooltip("Shown over the card while the tower is locked.")]
        public GameObject lockGroup;
        public TMP_Text lockLabel;
        [Tooltip("Hidden while locked (the price is replaced by the level badge).")]
        public GameObject costGroup;
        public Color lockedPortrait = new(0.25f, 0.27f, 0.32f, 1f);

        public void OnPointerClick(PointerEventData eventData) {
            if (eventData.button == PointerEventData.InputButton.Left) Clicked?.Invoke(index);
        }

        bool selected;
        Color accent, danger, normalCost;

        public void Init(Color accentColor, Color dangerColor, Color costColor) {
            accent = accentColor;
            danger = dangerColor;
            normalCost = costColor;
        }

        /// <param name="unlockLevel">The level that unlocks this tower, or 0 when the player owns it.</param>
        public void Set(bool isSelected, bool affordable, int unlockLevel = 0) {
            bool locked = unlockLevel > 0;
            if (lockGroup != null) lockGroup.SetActive(locked);
            if (lockLabel != null && locked) lockLabel.text = $"LV {unlockLevel}";
            if (costGroup != null) costGroup.SetActive(!locked);
            if (portrait != null) portrait.color = locked ? lockedPortrait : affordable || isSelected ? Color.white : unaffordablePortrait;
            group.alpha = 1f;
            if (locked) {
                selected = isSelected;
                Color lockedEdge = accent;
                lockedEdge.a = isSelected ? 0.6f : 0f;
                outline.color = lockedEdge;
                return;
            }
            selected = isSelected;
            Color edge = accent;
            edge.a = isSelected ? 1f : 0f;
            outline.color = edge;
            costLabel.color = affordable ? normalCost : danger;
            costIcon.color = affordable ? normalCost : danger;
        }

        void Update() {
            float y = selected ? selectedLift : 0f;
            Vector2 p = body.anchoredPosition;
            p.y = Mathf.Lerp(p.y, y, 1f - Mathf.Exp(-liftSharpness * Time.unscaledDeltaTime));
            body.anchoredPosition = p;
        }
    }
}
