using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Retention.Client {
    /// <summary>
    /// Theme icons by name, so unlocks the server names ("bolt", "crossed_swords") show their icon.
    /// The HUD authoring fills it with every icon of the shared theme; a game can add its own sprites
    /// on its HUD instance (same name = replaced).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RetentionIcons : MonoBehaviour {
        [Tooltip("Icon names, index-aligned with sprites.")]
        [SerializeField] string[] names = new string[0];
        [SerializeField] Sprite[] sprites = new Sprite[0];
        [Tooltip("Shown for unknown or empty names.")]
        [SerializeField] Sprite fallback;

        Dictionary<string, Sprite> byName;

        public Sprite Fallback => fallback;

        /// <summary>The icon called <paramref name="iconName"/>, or <see cref="Fallback"/>.</summary>
        public Sprite Get(string iconName) {
            if (byName == null) Build();
            return !string.IsNullOrEmpty(iconName) && byName.TryGetValue(iconName, out Sprite sprite) && sprite != null ? sprite : fallback;
        }

        /// <summary>Replaces the table (editor authoring).</summary>
        public void Set(string[] iconNames, Sprite[] iconSprites, Sprite fallbackSprite) {
            names = iconNames ?? new string[0];
            sprites = iconSprites ?? new Sprite[0];
            fallback = fallbackSprite;
            byName = null;
        }

        void Build() {
            int count = Mathf.Min(names?.Length ?? 0, sprites?.Length ?? 0);
            byName = new Dictionary<string, Sprite>(count);
            for (int i = 0; i < count; i++)
                if (!string.IsNullOrEmpty(names[i])) byName[names[i]] = sprites[i];
        }
    }
}
