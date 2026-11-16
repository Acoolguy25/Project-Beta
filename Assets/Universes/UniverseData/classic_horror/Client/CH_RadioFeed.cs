using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// The radio card. Each line arrives with a burst of static, the speaker on the caption
    /// ("DISPATCH", the witness, the priest...) and the words typed out as if transmitted, then stays
    /// long enough to read before fading.
    /// </summary>
    public sealed class CH_RadioFeed : MonoBehaviour {
        public HudPopup popup;
        public TMP_Text caption;
        public TMP_Text transcript;
        [Tooltip("Caption when a line names no speaker.")]
        [SerializeField] string defaultSpeaker = "Radio";
        public AudioSource staticSource;
        public AudioClip staticClip;
        [SerializeField, Range(0f, 1f)] float staticVolume = 0.35f;
        [Tooltip("Characters revealed per second.")]
        [SerializeField, Min(5f)] float charactersPerSecond = 55f;
        [Tooltip("After typing, a line stays one second per readingSpeed characters, clamped between these.")]
        [SerializeField, Min(1f)] float minimumSeconds = 8f;
        [SerializeField, Min(1f)] float maximumSeconds = 24f;
        [Tooltip("Characters read per second.")]
        [SerializeField, Min(1f)] float readingSpeed = 14f;

        // TMP's "no limit" for maxVisibleCharacters.
        const int AllCharacters = 99999;
        float shownAt, visibleUntil;
        int length;
        bool suppressed;

        public void Show(string line, float minimumHold = 0f) {
            CH_StoryText.Split(line, out string speaker, out string body);
            caption.text = string.IsNullOrEmpty(speaker) ? defaultSpeaker : speaker;
            transcript.text = body;
            // Story lines are plain text, so the string length is the visible character count.
            length = body.Length;
            transcript.maxVisibleCharacters = 0;
            shownAt = Time.unscaledTime;
            float typing = length / charactersPerSecond;
            visibleUntil = shownAt + typing + Mathf.Max(minimumHold, Mathf.Clamp(length / readingSpeed, minimumSeconds, maximumSeconds));
            if (staticSource != null && staticClip != null) staticSource.PlayOneShot(staticClip, staticVolume);
        }

        /// <summary>Hidden while the journal or the ending card has the screen; the line keeps its time.</summary>
        public void SetSuppressed(bool value) => suppressed = value;

        public void Clear() {
            visibleUntil = 0f;
            popup.HideImmediate();
        }

        void Update() {
            bool visible = !suppressed && Time.unscaledTime < visibleUntil;
            popup.SetVisible(visible);
            if (!visible) return;
            int shown = Mathf.FloorToInt((Time.unscaledTime - shownAt) * charactersPerSecond);
            if (shown >= length) shown = AllCharacters;
            if (transcript.maxVisibleCharacters != shown) transcript.maxVisibleCharacters = shown;
        }
    }
}
