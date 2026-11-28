using System.Collections.Generic;
using System.Linq;
using System.Text;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientUI.NameTag;
using RyanAssets.GameFeel;
using RyanAssets.Input;
using RyanAssets.Retention.Client;
using RyanAssets.Rounds;
using RyanAssets.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>HUD controller, feedback widgets: the tag feed, ping markers, the round timer, the alert and the toast.</summary>
    public sealed partial class HS_ClientController {
        void ClearFeed() {
            while (feed.Count > 0) {
                var row = feed.Dequeue().row;
                if (row != null) Destroy(row.gameObject);
            }
        }

        void StopBeacons() {
            foreach (var beacon in beacons) beacon.Stop();
            beacons.Clear();
        }

        void AddFeed(string text, Sprite icon) {
            var row = Instantiate(feedTemplate, feedList);
            row.gameObject.SetActive(true);
            row.Set(text, icon);
            row.transform.SetAsFirstSibling();
            feed.Enqueue((row, Time.unscaledTime + feedSeconds));
        }

        void ClearPings() {
            foreach (var p in pings) if (p.marker != null) Destroy(p.marker);
            pings.Clear();
        }

        void RefreshTimer() {
            bool timed = state.phase is HS_Phase.Intermission or HS_Phase.Hiding or HS_Phase.Seeking or HS_Phase.Results;
            int left = Mathf.Max(0, state.secondsLeft - Mathf.FloorToInt(Time.unscaledTime - stateAt));
            int clock = timed ? left : -2;
            if (clock != shownClock) {
                shownClock = clock;
                timerLabel.text = timed ? RoundFormat.Clock(left) : "--:--";
            }
            timerFill.fillAmount = timed && state.phaseSeconds > 0 ? Mathf.Clamp01((left - (Time.unscaledTime - stateAt) % 1f) / state.phaseSeconds) : 0f;
            if (state.phase == HS_Phase.Hiding) TickCountdown(left);
            else lastTickSecond = -1;
        }

        /// <summary>Blind seekers hear every second tick by; everyone hears the last few seconds beep.</summary>
        void TickCountdown(int left) {
            if (left == lastTickSecond || left <= 0) return;
            bool first = lastTickSecond < 0;
            lastTickSecond = left;
            if (blindfold.activeSelf) blindCountdown.text = left.ToString();
            if (first) return;
            if (left <= countdownBeepsFrom) GameSfx.Play(SfxCue.CountdownBeep);
            else if (blindfold.activeSelf) GameSfx.Play(blindTick);
        }

        void ShowAlert(string message, Color color, float seconds) {
            if (alert == null) return;
            alertLabel.text = message;
            alertLabel.color = color;
            alertAt = Time.unscaledTime;
            alertUntil = alertAt + seconds;
            Show(alert, true, 0.08f);
        }

        /// <summary>Pops the alert in (overshoot to full size) and fades it when its time is up.</summary>
        void RefreshAlert() {
            if (alertAt < 0f) return;
            float age = Time.unscaledTime - alertAt;
            float t = Mathf.Clamp01(age / alertPopSeconds);
            alertLabel.transform.localScale = Vector3.one * Mathf.LerpUnclamped(alertPopScale, 1f, 1f - (1f - t) * (1f - t));
            if (Time.unscaledTime > alertUntil) {
                Show(alert, false);
                alertAt = -1f;
            }
        }

        /// <summary>
        /// Sizes the toast's text for <paramref name="message"/>: one line at the authored size when it fits in
        /// <see cref="toastTextMaxWidth"/> (so the icon stays next to it), otherwise two lines at a smaller size.
        /// </summary>
        void FitToastText(string message) {
            if (toastFontSize <= 0f) toastFontSize = toastLabel.fontSize;
            if (toastLayout == null && !toastLabel.TryGetComponent(out toastLayout)) toastLayout = toastLabel.gameObject.AddComponent<LayoutElement>();
            toastLabel.enableAutoSizing = false;
            toastLabel.fontSize = toastFontSize;
            float width = toastLabel.GetPreferredValues(message, float.PositiveInfinity, float.PositiveInfinity).x;
            bool wrap = width > toastTextMaxWidth;
            toastLabel.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            if (wrap) toastLabel.fontSize = Mathf.Min(toastFontSize, toastWrappedFontSize);
            toastLayout.preferredWidth = wrap ? toastTextMaxWidth : width;
        }

        void Toast(string message, Sprite icon, SfxSound sound, float seconds) {
            FitToastText(message);
            toastLabel.text = message;
            toastIcon.sprite = icon != null ? icon : infoIcon;
            toastUntil = Time.unscaledTime + seconds;
            Show(toast, true);
            if (sound != null) GameSfx.Play(sound);
        }
    }
}
