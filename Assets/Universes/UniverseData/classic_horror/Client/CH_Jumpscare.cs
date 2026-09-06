using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>Transient encounter presentation. No input capture or gameplay time changes.</summary>
    public sealed class CH_Jumpscare : MonoBehaviour {
        public CanvasGroup overlay;
        public RectTransform face;
        public AudioSource sting;
        int lastSequence;
        float started = -100, duration;
        public int PlayedCount { get; private set; }
        public void ResetCase() { lastSequence = 0; started = -100; if (overlay != null) overlay.alpha = 0; }
        public void Play(int sequence, byte kind) {
            if (sequence <= lastSequence || overlay == null) return;
            lastSequence = sequence;
            PlayedCount++;
            started = Time.unscaledTime;
            duration = kind == 2 ? 0.85f : kind == 1 ? 0.48f : 0.65f;
            overlay.alpha = 1;
            if (sting != null) { sting.pitch = kind == 1 ? 1.25f : 0.85f; sting.Play(); }
        }
        void Update() {
            if (overlay == null) return;
            float t = (Time.unscaledTime - started) / Mathf.Max(0.01f, duration);
            if (t >= 1) { overlay.alpha = 0; if (sting != null && sting.isPlaying) sting.Stop(); return; }
            // One sudden appearance and a fade, without repeated flashes.
            overlay.alpha = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.55f, 1f, t));
            face.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.35f, Mathf.Sqrt(Mathf.Clamp01(t)));
            face.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 7f) * 5f);
        }
        void OnDisable() { if (overlay != null) overlay.alpha = 0; if (sting != null) sting.Stop(); }
    }
}
