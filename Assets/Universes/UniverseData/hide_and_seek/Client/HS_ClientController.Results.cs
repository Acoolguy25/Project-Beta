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
    /// <summary>HUD controller, results: the survivors' reveal, the results card and the round recap.</summary>
    public sealed partial class HS_ClientController {
        /// <summary>
        /// Fills the results card's title, line and colour from your side's view, and returns whether you won.
        /// A seeker reads "they got away" in the losing colour, a found hider lost, and a seeker whose win does
        /// not count (they joined late, or left and came back) is told so instead of seeing a green card.
        /// </summary>
        bool ShowResultsBanner(HS_Role me) {
            bool hidersWin = state.hidersLeft > 0;
            bool recapNow = recapRound == state.round && recap.played;
            bool mySideWon = me switch { HS_Role.None => hidersWin, HS_Role.Caught => false, _ => hidersWin == (me == HS_Role.Hider) };
            bool winDenied = mySideWon && recapNow && !recap.won && me != HS_Role.None;
            if (winDenied) mySideWon = false;
            bannerTitle.text = hidersWin ? "HIDERS WIN" : "SEEKERS WIN";
            bannerSubtitle.text = hidersWin
                ? Seeks(me) ? $"They got away: {state.hidersLeft} of {state.hidersAtStart} stayed hidden" : $"{state.hidersLeft} of {state.hidersAtStart} stayed hidden"
                : winDenied ? "Everyone was found. You joined late: no win this round" : "Everyone was found";
            Color accent = mySideWon ? Theme.success : winDenied ? Theme.textSecondary : Theme.danger;
            bannerAccent.color = accent;
            bannerIcon.sprite = hidersWin ? hiderIcon : seekerIcon;
            bannerIcon.color = accent;
            return mySideWon;
        }

        void OnReveal(HS_RevealBroadcast message, Channel channel) {
            spots = message.spots ?? System.Array.Empty<HS_HidingSpot>();
            spotsRound = message.round;
            StopBeacons();
            // Names stay up for the whole results phase and draw over the world, so everyone can see
            // where the survivors were from where they stand. Your own spot needs no label.
            float seconds = Mathf.Max(1f, message.seconds);
            for (int i = 0; i < spots.Length; i++) {
                HS_HidingSpot spot = spots[i];
                beacons.Add(GameFx.Start(FxKind.BeaconBeam, spot.position, Theme.success, revealBeaconScale));
                if (spot.clientId != MyId)
                    FloatingText.Callout(spot.position + Vector3.up * (revealLabelHeight + RevealStack(i) * revealLabelStep), spot.name, Theme.success, revealLabelSize, seconds);
            }
            RefreshSpots();
        }

        /// <summary>
        /// How many labelled survivors before <paramref name="index"/> hid close by, so names of survivors who
        /// stood together stack instead of printing over each other.
        /// </summary>
        int RevealStack(int index) {
            int stack = 0;
            for (int j = 0; j < index; j++)
                if (spots[j].clientId != MyId && (spots[j].position - spots[index].position).sqrMagnitude < revealLabelSpacing * revealLabelSpacing) stack++;
            return stack;
        }

        /// <summary>Fills the banner's "who hid where" list from the latest reveal.</summary>
        void RefreshSpots() {
            if (bannerSpots == null) return;
            if (spots.Length == 0 || spotsRound != state.round) { bannerSpots.text = ""; FitBanner(0); return; }
            var text = new StringBuilder();
            string hex = UITheme.ToRichTextHex(Theme.success);
            int shown = Mathf.Min(spots.Length, bannerSpotLines);
            for (int i = 0; i < shown; i++) {
                string area = SpotName(spots[i].position);
                if (i > 0) text.Append('\n');
                text.Append("<color=#").Append(hex).Append('>').Append(spots[i].name).Append("</color> hid in ").Append(area.Length > 0 ? $"the {area}" : "a secret spot");
            }
            if (spots.Length > shown) text.Append($"\n+{spots.Length - shown} more");
            bannerSpots.text = text.ToString();
            FitBanner(shown + (spots.Length > shown ? 1 : 0));
        }

        /// <summary>Where a survivor hid, as precisely as the map knows: a closet by name, else the room.</summary>
        string SpotName(Vector3 position) {
            if (map == null) return "";
            HS_HideDoor door = map.Door(map.DoorAround(position));
            return door != null && !string.IsNullOrEmpty(door.spotName) ? door.spotName : map.AreaAt(position);
        }

        /// <summary>Sizes the results card (and its shadow, a sibling) to the header plus this many list lines.</summary>
        void FitBanner(int lines) {
            if (bannerCard == null) return;
            float height = bannerListTop + lines * bannerLineHeight + bannerBottomMargin;
            float shadowExtra = bannerShadow != null ? bannerShadow.sizeDelta.y - bannerCard.sizeDelta.y : 0f;
            bannerCard.sizeDelta = new Vector2(bannerCard.sizeDelta.x, height);
            if (bannerShadow != null) bannerShadow.sizeDelta = new Vector2(bannerShadow.sizeDelta.x, height + shadowExtra);
        }

        void OnRecap(HS_RoundRecapBroadcast message, Channel channel) {
            recap = message;
            recapRound = message.round;
            RefreshRecap();
            // The recap can land after the results state: re-colour the card from your own result.
            if (hasState && state.phase == HS_Phase.Results && recapRound == state.round) ShowResultsBanner(MyRole);
            if (!message.played) return;
            if (message.newBest || message.winStreak >= 2) GameSfx.Play(recordSound);
            var me = LocalPlayer.Character;
            if (message.newBest && me != null) GameFx.Play(FxKind.Confetti, me.transform.position + Vector3.up * pointsPopupHeight, Theme.warning);
        }

        /// <summary>Fills the results card's MVP line and your own line from the latest recap.</summary>
        void RefreshRecap() {
            if (bannerMvp == null || bannerYou == null) return;
            if (recapRound != state.round) {
                bannerMvp.text = bannerYou.text = "";
                return;
            }
            string gold = UITheme.ToRichTextHex(Theme.warning), muted = UITheme.ToRichTextHex(Theme.textMuted);
            bool mvpIsMe = recap.mvpClientId == MyId;
            bannerMvp.text = recap.mvpClientId < 0 || string.IsNullOrEmpty(recap.mvpName) ? ""
                : $"<color=#{gold}>MVP</color>  {(mvpIsMe ? "You" : recap.mvpName)} <color=#{muted}>with {recap.mvpPoints} pts</color>";
            if (!recap.played) {
                bannerYou.text = "";
                return;
            }
            var text = new StringBuilder();
            text.Append("You: <b>").Append(recap.points).Append(" pts</b>");
            if (recap.newBest) text.Append("  <color=#").Append(gold).Append(">NEW BEST!</color>");
            else text.Append(" <color=#").Append(muted).Append(">(best ").Append(recap.bestPoints).Append(")</color>");
            if (recap.winStreak >= 2) text.Append("  <color=#").Append(gold).Append('>').Append(recap.winStreak).Append(" wins in a row</color>");
            else if (!recap.won && recap.bestWinStreak >= 2) text.Append("  <color=#").Append(muted).Append(">streak ended</color>");
            bannerYou.text = text.ToString();
        }
    }
}
