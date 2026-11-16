using System;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Full-screen feedback on one overlay canvas (created on first use, under the HUD): flashes,
    /// a damage vignette pulse, a sustained low-health pulse, speed lines and a big centre text with
    /// a "3-2-1-GO" countdown (with beeps). Colours and timings are on the ScreenFxOverlay prefab.
    /// <code>
    /// ScreenFx.Flash(Color.white, 0.2f);
    /// ScreenFx.DamagePulse();                         // on every hit taken
    /// ScreenFx.SetLowHealth(health / (float)maxHealth); // every change; pulses at 30% and below
    /// ScreenFx.SetSpeedLines(boosting ? 1f : 0f);
    /// ScreenFx.Countdown(3, "GO!", () => raceStarted = true);
    /// ScreenFx.ShowText("ROUND 2", Color.yellow);
    /// </code>
    /// </summary>
    public static class ScreenFx {
        /// <summary>A full-screen flash: <paramref name="color"/>'s alpha at the peak, fading over <paramref name="seconds"/>.</summary>
        public static void Flash(Color color, float seconds = 0.25f) => Overlay()?.Flash(color, seconds);

        /// <summary>A red vignette pulse; <paramref name="strength"/> 0..1 (bigger hits, stronger pulses).</summary>
        public static void DamagePulse(float strength = 1f) => Overlay()?.DamagePulse(strength);

        /// <summary>Health as 0..1. At or below the overlay's threshold a red vignette beats, faster as it drops; 1 stops it.</summary>
        public static void SetLowHealth(float health01) {
            // Healthy players never need the overlay created.
            if (health01 >= 1f && ExistingOverlay() == null)
                return;
            Overlay()?.SetLowHealth(health01);
        }

        /// <summary>Speed lines at <paramref name="intensity"/> 0..1 (0 fades them out).</summary>
        public static void SetSpeedLines(float intensity) {
            if (intensity <= 0f && ExistingOverlay() == null)
                return;
            Overlay()?.SetSpeedLines(intensity);
        }

        /// <summary>
        /// Big centre "3", "2", "1", then <paramref name="goText"/>, one per second with the
        /// CountdownBeep and Go cues. <paramref name="onGo"/> runs when the go text appears.
        /// Starting a new countdown or text cancels the running one (its onGo never runs).
        /// </summary>
        public static void Countdown(int from = 3, string goText = "GO!", Action onGo = null) =>
            Overlay()?.Countdown(from, goText, onGo);

        /// <summary>Big centre text for <paramref name="seconds"/> ("ROUND 2", "SUDDEN DEATH").</summary>
        public static void ShowText(string text, Color color, float seconds = 1.6f) => Overlay()?.ShowText(text, color, seconds);

        /// <summary>Stops a running countdown or centre text.</summary>
        public static void CancelCountdown() {
            ScreenFxOverlay overlay = ExistingOverlay();
            if (overlay != null)
                overlay.CancelCountdown();
        }

        static ScreenFxOverlay ExistingOverlay() {
            GameFeelRunner runner = GameFeelRunner.Existing;
            return runner != null ? runner.ExistingOverlay : null;
        }

        static ScreenFxOverlay Overlay() {
            GameFeelRunner runner = GameFeelRunner.Get();
            return runner != null ? runner.Overlay : null;
        }
    }
}
