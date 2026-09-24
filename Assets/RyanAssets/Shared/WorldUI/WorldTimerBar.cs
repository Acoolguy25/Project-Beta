using RyanAssets.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Shared.WorldUI {
    /// <summary>
    /// A world-space countdown and progress bar drawn above an object, shared by every game.
    /// <para>
    /// The bar is driven by a replicated deadline rather than by a locally ticked timer: a caller
    /// hands it the server clock reading the countdown ends at and how long it runs for, and the bar
    /// derives the remaining time itself from <see cref="NetworkHelper.ServerTime"/> every frame.
    /// Every machine therefore reads the same two replicated numbers and draws the same bar, without
    /// the owner having to push a progress value over the wire each frame.
    /// </para>
    /// <para>
    /// Everything it draws - backing, fill, label, and the owner-colour accent - is authored on the
    /// prefab and wired in the Inspector. This component only writes values into those references.
    /// </para>
    /// </summary>
    public sealed class WorldTimerBar : MonoBehaviour {
        [Header("Authored References")]
        [Tooltip("Filled Image drawn from empty to full as the countdown runs.")]
        [SerializeField] Image fill;
        [Tooltip("Optional. Countdown text, e.g. \"Building - 12s\".")]
        [SerializeField] TextMeshProUGUI label;
        [Tooltip("Optional. Strip tinted with the owner's colour so the bar reads as theirs.")]
        [SerializeField] Image accent;
        [Tooltip("Everything that should disappear together when the bar is not running.")]
        [SerializeField] GameObject content;

        [Header("Behaviour")]
        [Tooltip("Turn the bar to face the active camera each frame.")]
        [SerializeField] bool billboard = true;
        [Tooltip("Hide the bar once the countdown reaches zero.")]
        [SerializeField] bool hideWhenComplete = true;
        [Tooltip("Text placed before the countdown, e.g. \"Building\". Leave empty for the time alone.")]
        [SerializeField] string labelPrefix = string.Empty;

        float completionServerTime;
        float duration;
        bool running;

        /// <summary>0 while the countdown is fresh, 1 the moment it elapses.</summary>
        public float Progress =>
            duration <= 0f ? (running ? 0f : 1f) : Mathf.Clamp01(1f - SecondsRemaining / duration);

        public float SecondsRemaining =>
            running ? Mathf.Max(0f, completionServerTime - NetworkHelper.ServerTime) : 0f;

        public bool IsRunning => running;

        void Awake() {
            // Authored visible so the bar can be seen and adjusted in the prefab, but a live one
            // stays hidden until something actually starts a countdown on it.
            ApplyVisible(false);
        }

        /// <summary>
        /// Points the bar at a replicated deadline. <paramref name="completionServerTime"/> is a
        /// <see cref="NetworkHelper.ServerTime"/> reading, so callers pass the same SyncVar value
        /// every client already holds.
        /// </summary>
        public void StartCountdown(float completionServerTime, float duration) {
            this.completionServerTime = completionServerTime;
            this.duration = Mathf.Max(0f, duration);
            running = true;
            ApplyVisible(true);
            Refresh();
        }

        /// <summary>Stops and hides the bar. Called when the work finishes or its owner dies.</summary>
        public void Stop() {
            running = false;
            ApplyVisible(false);
        }

        /// <summary>Tints the accent strip, normally with the owning player's colour.</summary>
        public void SetAccentColor(Color color) {
            if (accent != null)
                accent.color = color;
        }

        public void SetLabelPrefix(string prefix) {
            labelPrefix = prefix;
            if (running)
                Refresh();
        }

        // LateUpdate so the billboard is applied after anything that moved the bar's parent this
        // frame, which keeps it from lagging a frame behind a structure that is still rising.
        void LateUpdate() {
            if (!running)
                return;

            Refresh();

            if (hideWhenComplete && SecondsRemaining <= 0f)
                Stop();
        }

        void Refresh() {
            float remaining = SecondsRemaining;

            if (fill != null)
                fill.fillAmount = Progress;

            if (label != null) {
                label.text = string.IsNullOrEmpty(labelPrefix)
                    ? FormatCountdown(remaining)
                    : $"{labelPrefix} {FormatCountdown(remaining)}";
            }

            if (!billboard)
                return;

            // Camera.main is null for a frame during camera switches, and on a headless build there
            // is no camera at all. Holding the previous facing for that frame is correct.
            Camera camera = Camera.main;
            if (camera != null)
                transform.rotation = camera.transform.rotation;
        }

        void ApplyVisible(bool visible) {
            if (content != null)
                content.SetActive(visible);
            else
                gameObject.SetActive(visible);
        }

        /// <summary>
        /// Shared countdown formatting, so a build timer, a production queue, and a round clock all
        /// read the same way rather than each game rounding and abbreviating differently.
        /// </summary>
        public static string FormatCountdown(float secondsRemaining) {
            if (secondsRemaining <= 0f)
                return "0s";
            int total = Mathf.CeilToInt(secondsRemaining);
            return total >= 60 ? $"{total / 60}m {total % 60}s" : $"{total}s";
        }
    }
}
