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
    /// <summary>HUD controller, round moments: tags, the last hider, pings, server answers and taunts.</summary>
    public sealed partial class HS_ClientController {
        void OnTag(HS_TagBroadcast message, Channel channel) {
            roles[message.hiderId] = HS_Role.Caught;
            AddFeed($"<color=#{UITheme.ToRichTextHex(Theme.danger)}>{message.seekerName}</color> found <color=#{UITheme.ToRichTextHex(Theme.success)}>{message.hiderName}</color>", tagIcon);
            bool tagged = message.hiderId == MyId, tagger = message.seekerId == MyId;
            if (tagged) Toast("You were found! You're a seeker now.", caughtIcon, null, ToastLong);
            else if (tagger) Toast($"Tagged {message.hiderName}!", tagIcon, null, ToastShort);
            if (tagged) RefreshRoleCard(MyRole);
            PlayTagMoment(message.hiderId, tagged, tagger);
        }

        /// <summary>Sting, a poof of smoke where the hider was, a flash and a slow shake for whoever was involved.</summary>
        void PlayTagMoment(int hiderId, bool tagged, bool tagger) {
            LocalCharacter hider = CharacterLookup.ByClientId(hiderId);
            if (tagged || tagger) {
                GameSfx.Play(tagSting);
                ScreenFx.Flash(tagged ? taggedFlash : taggerFlash, flashSeconds);
                CameraShake.Kick(tagged ? tagShake : tagShake * taggerShakeFactor, tagShakeSeconds);
            }
            if (hider == null) return;
            Vector3 feet = hider.transform.position;
            if (!tagged && !tagger) {
                GameSfx.PlayAt(tagSting, feet);
                CameraShake.Explosion(feet, tagShakeRadius, tagShake * bystanderShakeFactor, tagShakeSeconds);
            }
            // Layered puffs from feet to head swallow the robot (one stretched puff showed hard edges).
            for (int i = 0; i < poofPasses; i++) GameFx.Poof(feet, poofHeight, poofTint, poofScale);
            HitFlash.On(hider, Color.white);
        }

        void AnnounceLastHider(HS_Role me) {
            if (state.phase != HS_Phase.Seeking) return;
            GameSfx.Play(lastHiderSting);
            GameSfx.Play(lastHiderVoice);
            ShowAlert(me == HS_Role.Hider ? "YOU'RE THE LAST HIDER!" : "LAST HIDER!", Theme.danger, lastHiderTextSeconds);
            string name = roles.Where(r => r.Value == HS_Role.Hider).Select(r => CharacterLookup.ByClientId(r.Key)).FirstOrDefault(c => c != null)?.DisplayName;
            AddFeed(name != null ? $"Last hider: <color=#{UITheme.ToRichTextHex(Theme.success)}>{name}</color>" : "One hider left", hiderIcon);
        }

        void OnPing(HS_PingBroadcast message, Channel channel) {
            ClearPings();
            if (map == null || map.pingPrefab == null || message.positions == null) return;
            foreach (var p in message.positions)
                pings.Add((Instantiate(map.pingPrefab, p, Quaternion.identity, map.transform), Time.unscaledTime + pingSeconds));
            Toast("PING! Hiders were spotted near the red beacons.", pingIcon, pingSound, ToastLong);
        }

        void OnResult(HS_ResultBroadcast message, Channel channel) {
            // Ability messages (a refusal, a radar warning) go on the line above the bar, clear of the view.
            if (message.ability != HS_Ability.None && abilityBar != null && abilityBar.Visible) {
                abilityBar.Say(message.message, ToastMedium);
                if (message.accepted) GameSfx.Play(pingSound);
                return;
            }
            Toast(message.message, message.accepted ? pingIcon : infoIcon, message.accepted ? pingSound : null, ToastMedium);
        }

        void OnTaunt(HS_TauntBroadcast message, Channel channel) {
            if (message.clientId != MyId) {
                GameSfx.PlayAt(tauntSound, message.position);
                // Seekers (and other hiders) also see where it came from, if they are looking that way.
                GameFx.Play(FxKind.ShockwaveRing, message.position, Theme.warning, tauntRingScale);
                FloatingText.Callout(message.position + Vector3.up * tauntMarkHeight, "!", Theme.warning, 1f, tauntMarkSeconds);
                return;
            }
            tauntsUsed = message.used;
            tauntPendingUntil = 0f;
            if (message.correction) return;
            GameSfx.Play(tauntSound);
            lastTauntAt = Time.unscaledTime;
            // The XP shows on the HUD popup by the points chip (a label over your own head is out of view in first person).
            ShowPointsPopup(message.brave ? $"+{message.xp} Brave taunt" : $"+{message.xp} Taunt");
            if (message.brave) Toast("Brave taunt! A seeker is close.", tauntIcon, null, ToastMedium); // the popup carries the XP
        }
    }
}
