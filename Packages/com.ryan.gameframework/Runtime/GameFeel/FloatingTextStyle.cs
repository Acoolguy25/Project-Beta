using System;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>How one floating text popup looks and moves. See <see cref="FloatingText"/>.</summary>
    [Serializable]
    public struct FloatingTextStyle {
        [Tooltip("Text colour (alpha fades out over the popup's life).")]
        public Color color;
        [Tooltip("Letter height in metres when seen from the reference distance.")]
        [Min(0.01f)] public float size;
        [Tooltip("Metres the popup rises over its life (along world up).")]
        public float rise;
        [Tooltip("Seconds the popup lives.")]
        [Min(0.05f)] public float duration;
        [Tooltip("Scale the popup starts at before settling to 1 (1 = no pop).")]
        [Range(0.2f, 3f)] public float pop;
        [Tooltip("Random sideways offset (m) so stacked popups do not cover each other.")]
        [Min(0f)] public float scatter;

        public FloatingTextStyle(Color color, float size, float rise, float duration, float pop, float scatter) {
            this.color = color;
            this.size = size;
            this.rise = rise;
            this.duration = duration;
            this.pop = pop;
            this.scatter = scatter;
        }

        /// <summary>This style in another colour.</summary>
        public FloatingTextStyle WithColor(Color newColor) {
            FloatingTextStyle copy = this;
            copy.color = newColor;
            return copy;
        }

        /// <summary>This style living <paramref name="seconds"/> instead of its own duration.</summary>
        public FloatingTextStyle WithDuration(float seconds) {
            FloatingTextStyle copy = this;
            copy.duration = Mathf.Max(0.05f, seconds);
            return copy;
        }

        /// <summary>This style scaled by <paramref name="factor"/>.</summary>
        public FloatingTextStyle Scaled(float factor) {
            FloatingTextStyle copy = this;
            copy.size *= factor;
            return copy;
        }
    }
}
