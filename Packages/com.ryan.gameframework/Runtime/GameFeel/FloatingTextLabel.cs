using TMPro;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>The pooled popup prefab's root: one world-space TextMeshPro. Animated by <see cref="FloatingText"/>.</summary>
    [DisallowMultipleComponent]
    public sealed class FloatingTextLabel : MonoBehaviour {
        [SerializeField] TMP_Text text;

        public TMP_Text Text => text;

        void Reset() => text = GetComponentInChildren<TMP_Text>();
    }
}
