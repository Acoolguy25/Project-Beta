using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tycoon.Client {
    /// <summary>One perk in the rebirth perk shop: icon, effect, level pips and an upgrade button.</summary>
    public sealed class TY_PerkRow : MonoBehaviour {
        public TY_PerkId perk;
        public TMP_Text title, effect, cost;
        [Tooltip("One image per level, filled up to the current level.")]
        public Image[] pips;
        public Button upgrade;
        public TMP_Text upgradeLabel;
        [Tooltip("Dims the upgrade button while it can't be pressed.")]
        public CanvasGroup upgradeGroup;
        public Color pipOn = new(1f, 0.79f, 0.24f), pipOff = new(1f, 1f, 1f, 0.12f);

        /// <summary>Raised when the player presses Upgrade.</summary>
        public event Action<TY_PerkId> Upgrade;

        void Awake() => upgrade.onClick.AddListener(OnClick);
        void OnDestroy() => upgrade.onClick.RemoveListener(OnClick);
        void OnClick() => Upgrade?.Invoke(perk);

        public void Show(int level, int tokens) {
            var data = TY_Perks.Get(perk);
            title.text = data.name;
            bool maxed = level >= data.maxLevel;
            effect.text = level > 0 ? data.Effect(level) : $"Next: {data.Effect(1)}";
            for (int i = 0; i < pips.Length; i++) {
                pips[i].gameObject.SetActive(i < data.maxLevel);
                pips[i].color = i < level ? pipOn : pipOff;
            }
            int price = data.Cost(level);
            cost.text = maxed ? "" : $"{price}";
            upgradeLabel.text = maxed ? "MAX" : "Upgrade";
            upgrade.interactable = !maxed && tokens >= price;
            upgradeGroup.alpha = upgrade.interactable ? 1f : 0.4f;
        }
    }
}
