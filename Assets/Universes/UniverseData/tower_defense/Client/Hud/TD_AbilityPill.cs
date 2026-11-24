using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// One ability button (Airstrike, EMP) on the right edge: key, name and its state (ready, waves to
    /// recharge, or the level that unlocks it). Display only; the keys are read by the controller.
    /// </summary>
    [Serializable]
    public sealed class TD_AbilityPill {
        public CanvasGroup group;
        public RectTransform punch;
        public Image back, icon;
        public TMP_Text keyLabel, nameLabel, stateLabel;
    }
}
