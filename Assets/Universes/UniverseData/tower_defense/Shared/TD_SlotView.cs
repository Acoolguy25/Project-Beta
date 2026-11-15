using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// A tower pad: a glowing ring that brightens when focused (and takes the tower's colour once
    /// built on), a floating build marker and a ring previewing the selected tower's range.
    /// </summary>
    public sealed class TD_SlotView : MonoBehaviour {
        public LineRenderer glow;
        public Color idleColor = new(0.35f, 0.8f, 1f, 0.55f);
        public Color focusedColor = new(1f, 0.82f, 0.3f, 1f);
        [Tooltip("Floating build marker, hidden once a tower stands on the pad.")]
        public Transform marker;
        public LineRenderer rangeRing;
        [Tooltip("Height of the pad's top surface; towers stand on it.")]
        public float topHeight = 0.6f;

        [HideInInspector] public Color builtColor;
        [HideInInspector] public bool built, focused;

        /// <summary>Recolours the glow ring for the pad's current state.</summary>
        public void Refresh() {
            if (glow == null) return;
            Color c = focused ? focusedColor : built ? builtColor : idleColor;
            glow.startColor = glow.endColor = c;
            glow.widthMultiplier = focused ? 0.35f : 0.22f;
        }
    }
}
