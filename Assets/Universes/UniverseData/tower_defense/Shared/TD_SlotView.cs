using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>A slot pad: highlight when focused, hide when built on.</summary>
    public sealed class TD_SlotView : MonoBehaviour {
        public Renderer pad;
        public Material idle, focused;
        public TMP_Text label;
    }
}
