using TMPro;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>Flag visual: the cloth waves, the label says where it is. Placed by the client from server state.</summary>
    public sealed class CTF_FlagView : MonoBehaviour {
        public Transform cloth;
        public TMP_Text label;
        public GameObject homeGlow;
        [Header("Cloth wave")]
        [Min(0f)] public float waveSpeed = 3f;
        [Range(0f, 0.5f)] public float waveStretch = 0.08f;
        [Tooltip("Degrees the cloth swings either way.")]
        [Min(0f)] public float waveSwing = 8f;
        [Tooltip("Swing speed relative to the stretch.")]
        [Min(0f)] public float swingRate = 0.7f;
        Vector3 clothScale;

        void Awake() {
            if (cloth != null) clothScale = cloth.localScale;
        }

        public void Show(string text, bool home) {
            if (label != null && label.text != text) label.text = text;
            if (homeGlow != null && homeGlow.activeSelf != home) homeGlow.SetActive(home);
        }

        void Update() {
            if (cloth == null) return;
            float t = Time.time * waveSpeed;
            cloth.localScale = new Vector3(clothScale.x * (1f - waveStretch + waveStretch * Mathf.Sin(t)), clothScale.y, clothScale.z);
            cloth.localRotation = Quaternion.Euler(0f, waveSwing * Mathf.Sin(t * swingRate), 0f);
        }
    }
}
