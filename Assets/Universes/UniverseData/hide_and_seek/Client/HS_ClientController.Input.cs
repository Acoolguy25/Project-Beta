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
    /// <summary>HUD controller, input: tagging, taunting, the prompts and the "where am I" chip.</summary>
    public sealed partial class HS_ClientController {
        void TryTag() {
            if (manager == null || !hasState || state.phase != HS_Phase.Seeking || !Seeks(MyRole)) return;
            manager.Broadcast(new HS_TagRequest { round = state.round });
        }

        /// <summary>Hiders: ask to taunt. The server checks the cooldown and allowance again and answers with <see cref="HS_TauntBroadcast"/>.</summary>
        void TryTaunt() {
            if (manager == null || !hasState || !CanTauntNow()) return;
            manager.Broadcast(new HS_TauntRequest { round = state.round });
            // Until the server confirms, wait a moment before allowing another request.
            tauntPendingUntil = Time.unscaledTime + TauntRetrySeconds;
        }

        float SinceLastTaunt => Time.unscaledTime - lastTauntAt;

        int TauntAllowance => abilityBar != null ? abilityBar.TauntAllowance : HS_Rules.TauntsPerRound;

        bool CanTauntNow() => Time.unscaledTime >= tauntPendingUntil && HS_Rules.CanTaunt(state.phase, MyRole, SinceLastTaunt, tauntsUsed, TauntAllowance);

        LocalCharacter NearestHider() {
            var me = LocalPlayer.Character;
            if (me == null) return null;
            float distance = float.MaxValue;
            LocalCharacter best = null;
            foreach (var pair in roles) {
                if (pair.Value != HS_Role.Hider) continue;
                var hider = CharacterLookup.ByClientId(pair.Key);
                if (hider == null) continue;
                float d = Vector3.Distance(me.transform.position, hider.transform.position);
                if (d < distance) { distance = d; best = hider; }
            }
            return best;
        }

        /// <summary>
        /// The E prompt: a seeker's nearest hider in reach comes first ("Tag Bramble"), then the nearest door you
        /// may work ("Shut yourself in", "Search the Coat Closet").
        /// </summary>
        void RefreshPrompt() {
            bool seeking = state.phase == HS_Phase.Seeking && Seeks(MyRole);
            if (Time.unscaledTime >= nextHiderScan) {
                // The lookups walk every character and door, so do them a few times a second, not every frame.
                nextHiderScan = Time.unscaledTime + HiderScanSeconds;
                promptHider = seeking ? NearestHider() : null;
                promptDoor = DoorInReach();
            }
            var me = LocalPlayer.Character;
            bool hiderInReach = seeking && promptHider != null && me != null && Vector3.Distance(me.transform.position, promptHider.transform.position) <= HS_Rules.TagRange;
            promptTarget = hiderInReach ? PromptTarget.Hider : promptDoor >= 0 ? PromptTarget.Door : PromptTarget.None;
            switch (promptTarget) {
                case PromptTarget.Hider:
                    // Name the hider only once they are in reach, so a fading prompt never shows someone far away.
                    if (promptHider != labelledHider) {
                        labelledHider = promptHider;
                        tagLabel.text = $"Tag <b>{promptHider.DisplayName}</b>";
                    }
                    Show(tagPrompt, true);
                    Show(doorPrompt, false);
                    break;
                case PromptTarget.Door:
                    Show(tagPrompt, false);
                    ShowDoorPrompt(promptDoor);
                    break;
                default:
                    Show(tagPrompt, false);
                    Show(doorPrompt, false);
                    break;
            }
        }

        void RefreshTauntPrompt() {
            bool show = state.phase == HS_Phase.Seeking && MyRole == HS_Role.Hider;
            Show(tauntPrompt, show);
            if (!show) return;
            int left = TauntAllowance - tauntsUsed;
            float charge = Mathf.Clamp01(SinceLastTaunt / HS_Rules.TauntCooldown);
            float fill = left <= 0 ? 0f : charge;
            if (tauntFill.fillAmount != fill) tauntFill.fillAmount = fill;
            // 0 = none left, -1 = recharging, otherwise the count: the label only changes with this.
            int key = left <= 0 ? 0 : charge < 1f ? -1 : left;
            if (key == shownTaunt) return;
            shownTaunt = key;
            tauntLabel.text = key == 0 ? "No taunts left" : key < 0 ? "Taunt recharging" : $"Taunt <size=75%>({left} left)</size>";
        }

        void RefreshLocation() {
            var me = LocalPlayer.Character;
            string area = me != null && map != null ? map.AreaAt(me.transform.position) : "";
            bool show = area.Length > 0 && state.phase != HS_Phase.Results && !blindfold.activeSelf;
            if (show && area != shownArea) locationLabel.text = shownArea = area;
            Show(locationChip, show);
        }
    }
}
