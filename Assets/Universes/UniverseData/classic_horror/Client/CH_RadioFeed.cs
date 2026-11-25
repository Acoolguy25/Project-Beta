using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine.UI;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// The subtitle card. Each spoken line shows its speaker on the caption, in that speaker's colour, and the words
    /// typed out as they are said; a radio line arrives with a burst of static. It stays as long as the line is spoken
    /// (and long enough to read) before fading.
    /// </summary>
    public sealed class CH_RadioFeed : MonoBehaviour {
        public HudPopup popup;
        public TMP_Text caption;
        public TMP_Text transcript;
        [Tooltip("The glyph in the card's emblem, swapped for each speaker.")]
        public Image emblem;
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
        float shownAt, visibleUntil, typingRate = 55f;
        int length;
        bool suppressed;

        /// <summary>
        /// Shows <paramref name="body"/> said by <paramref name="speaker"/>, under <paramref name="icon"/> (kept when null).
        /// The words are typed over <paramref name="spokenSeconds"/> when the line is voiced, or at reading pace when it is not.
        /// </summary>
        public void Show(string speaker, string body, Color captionColor, Sprite icon, float spokenSeconds = 0f, bool radioStatic = false) {
            caption.text = string.IsNullOrEmpty(speaker) ? defaultSpeaker : speaker;
            caption.color = captionColor;
            if (emblem != null && icon != null) emblem.sprite = icon;
            transcript.text = body;
            // Story lines are plain text, so the string length is the visible character count.
            length = body.Length;
            transcript.maxVisibleCharacters = 0;
            shownAt = Time.unscaledTime;
            // A voiced line is typed as it is spoken, finishing a little before the voice does.
            typingRate = spokenSeconds > 0.5f ? Mathf.Max(charactersPerSecond * 0.35f, length / (spokenSeconds * 0.8f)) : charactersPerSecond;
            float typing = length / typingRate;
            float read = Mathf.Clamp(length / readingSpeed, minimumSeconds, maximumSeconds);
            visibleUntil = shownAt + Mathf.Max(typing + read * 0.5f, spokenSeconds + 1.5f);
            if (radioStatic && staticSource != null && staticClip != null) staticSource.PlayOneShot(staticClip, staticVolume);
        }

        /// <summary>Hidden while the ending card has the screen; the line keeps its time.</summary>
        public void SetSuppressed(bool value) => suppressed = value;

        public void Clear() {
            visibleUntil = 0f;
            popup.HideImmediate();
        }

        void Update() {
            bool visible = !suppressed && Time.unscaledTime < visibleUntil;
            popup.SetVisible(visible);
            if (!visible) return;
            int shown = Mathf.FloorToInt((Time.unscaledTime - shownAt) * typingRate);
            if (shown >= length) shown = AllCharacters;
            if (transcript.maxVisibleCharacters != shown) transcript.maxVisibleCharacters = shown;
        }
    }
}
