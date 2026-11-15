using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>A buy button's presentation: label plus an affordable / too-expensive / not-yours look.</summary>
    public sealed class TY_ButtonView : MonoBehaviour {
        public TMP_Text label;
        public Renderer pad;
        public Material affordable, expensive, locked;

        public void Show(string name, long price, int state) {
            label.text = price > 0 ? $"{name}\n<color=#FFE066>${RyanAssets.Rounds.RoundFormat.Money(price)}</color>" : name;
            pad.sharedMaterial = state switch { 0 => affordable, 1 => expensive, _ => locked };
        }
    }
}
