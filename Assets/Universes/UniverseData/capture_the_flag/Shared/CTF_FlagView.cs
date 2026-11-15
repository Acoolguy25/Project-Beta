using TMPro;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>Flag visual: the cloth waves, the label says where it is. Placed by the client from server state.</summary>
    public sealed class CTF_FlagView : MonoBehaviour {
        public Transform cloth;
        public TMP_Text label;
        public GameObject homeGlow;
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
            float t = Time.time * 3f;
            cloth.localScale = new Vector3(clothScale.x * (0.92f + 0.08f * Mathf.Sin(t)), clothScale.y, clothScale.z);
            cloth.localRotation = Quaternion.Euler(0f, 8f * Mathf.Sin(t * 0.7f), 0f);
        }
    }
}
