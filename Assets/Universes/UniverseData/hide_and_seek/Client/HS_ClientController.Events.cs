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
    /// <summary>HUD controller, round points and map events: the points chip and popup, the event chip, title and help.</summary>
    public sealed partial class HS_ClientController {
        void OnPoints(HS_PointsBroadcast message, Channel channel) {
            SetPoints(message.total, true);
            if (!message.highlight) return;
            GameSfx.Play(pointsSound);
            ShowPointsPopup($"+{message.points} {message.reason}");
        }

        void ShowPointsPopup(string text) {
            if (pointsPopup == null) return;
            if (pointsPopupBase == null) pointsPopupBase = pointsPopup.rectTransform.anchoredPosition;
            pointsPopup.text = text;
            pointsPopup.alpha = 1f;
            pointsPopup.rectTransform.anchoredPosition = pointsPopupBase.Value;
            pointsPopup.gameObject.SetActive(true);
            pointsPopupAt = Time.unscaledTime;
        }

        /// <summary>Rises and fades the points popup: it eases up over the first half and fades over the last 40%.</summary>
        void AnimatePointsPopup() {
            if (pointsPopup == null || pointsPopupAt < 0f) return;
            float t = (Time.unscaledTime - pointsPopupAt) / pointsPopupSeconds;
            if (t >= 1f) {
                pointsPopupAt = -1f;
                pointsPopup.gameObject.SetActive(false);
                return;
            }
            float u = 1f - Mathf.Clamp01(t * 2f);
            pointsPopup.rectTransform.anchoredPosition = pointsPopupBase.Value + Vector2.up * (pointsPopupRise * (1f - u * u));
            pointsPopup.alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
        }

        void SetPoints(int total, bool punch) {
            if (total == roundPoints && punch) return;
            roundPoints = total;
            if (pointsLabel != null) pointsLabel.text = total.ToString();
            if (punch && pointsPunch != null) Punch.Scale(pointsPunch, pointsPunchSize);
        }

        void OnEvent(HS_EventBroadcast message, Channel channel) => SetEvent(message.started ? message.kind : HS_RoundEvent.None, message.seconds, true);

        int EventSecondsLeft => activeEvent == HS_RoundEvent.None ? 0 : Mathf.Max(0, Mathf.CeilToInt(eventEndsAt - Time.unscaledTime));

        void SetEvent(HS_RoundEvent kind, int seconds, bool announce) {
            bool starting = kind != HS_RoundEvent.None && kind != activeEvent;
            activeEvent = kind;
            eventEndsAt = Time.unscaledTime + seconds;
            shownEventSeconds = -1;
            if (kind == HS_RoundEvent.None) {
                Show(eventChip, false);
                return;
            }
            eventIcon.sprite = EventIcon(kind);
            Show(eventChip, true);
            RefreshEventChip();
            if (!starting || !announce) return;
            GameSfx.Play(eventSound);
            ShowAlert(EventTitle(kind), Theme.warning, lastHiderTextSeconds);
            // The help toast waits for the title to go (see Update).
            eventToastKind = kind;
            eventToastAt = Time.unscaledTime + lastHiderTextSeconds;
        }

        void RefreshEventChip() {
            if (activeEvent == HS_RoundEvent.None || eventLabel == null) return;
            int left = EventSecondsLeft;
            if (left == shownEventSeconds) return;
            shownEventSeconds = left;
            eventLabel.text = $"{EventTitle(activeEvent)}  <color=#{UITheme.ToRichTextHex(Theme.textSecondary)}>{RoundFormat.Clock(left)}</color>";
        }

        Sprite EventIcon(HS_RoundEvent kind) => kind switch {
            HS_RoundEvent.Blackout => blackoutIcon,
            HS_RoundEvent.Frenzy => frenzyIcon,
            HS_RoundEvent.Shuffle => shuffleIcon,
            _ => infoIcon,
        };

        static string EventTitle(HS_RoundEvent kind) => kind switch {
            HS_RoundEvent.Blackout => "BLACKOUT",
            HS_RoundEvent.Frenzy => "FRENZY",
            HS_RoundEvent.Shuffle => "SHUFFLE",
            _ => "",
        };

        string EventHelp(HS_RoundEvent kind, HS_Role me) {
            string help = BaseEventHelp(kind, me);
            // On its featured day the event pays double for its play: say so.
            return kind == state.featuredEvent ? $"{help} {HS_Daily.Title(kind)}: {HS_Daily.Bonus(kind)}!" : help;
        }

        string BaseEventHelp(HS_RoundEvent kind, HS_Role me) => (kind, Seeks(me)) switch {
            (HS_RoundEvent.Blackout, false) => "The lights are out! Sneak to a better spot.",
            (HS_RoundEvent.Blackout, true) when abilityBar != null && abilityBar.Owns(HS_Ability.Flashlight) => "The lights are out! Your flashlight (3) lights the way.",
            (HS_RoundEvent.Blackout, true) => "The lights are out! Listen for footsteps and taunts.",
            (HS_RoundEvent.Frenzy, false) => "The seekers are faster for a while. Stay put!",
            (HS_RoundEvent.Frenzy, true) => "Frenzy! You run faster for a while. Hunt!",
            (HS_RoundEvent.Shuffle, false) => "Move to another room in time, or the seekers see you!",
            (HS_RoundEvent.Shuffle, true) => "The hiders must move rooms. Watch the doorways!",
            _ => "",
        };
    }
}
