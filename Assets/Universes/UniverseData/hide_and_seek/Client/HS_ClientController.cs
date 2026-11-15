using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientUI.NameTag;
using RyanAssets.Input;
using RyanAssets.Rounds;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// Shows your role, the blindfold while seekers wait, ping markers and the tag prompt. Seekers
    /// press E (or click) to tag; the server decides whether it counts. Name tags are only shown for
    /// your own side, so hiders can't be found by their floating names.
    /// </summary>
    public sealed class HS_ClientController : MonoBehaviour {
        public TMP_Text roleLabel, timerLabel, headlineLabel, promptLabel, toastLabel, feedLabel, blindfoldLabel;
        public GameObject blindfold;
        public AudioSource sfx;
        public AudioClip tagClip, pingClip, startClip;

        HS_Map map;
        ClientManager manager;
        HS_StateBroadcast state;
        bool hasState;
        float stateAt, toastUntil, nextRequest, feedUntil;
        readonly Dictionary<int, HS_Role> roles = new();
        readonly List<(GameObject marker, float until)> pings = new();
        HS_Phase lastPhase;
        int savedMask = ~0;

        void Awake() {
            map = GetComponentInParent<HS_Map>();
            roleLabel.text = "HIDE AND SEEK";
            timerLabel.text = headlineLabel.text = promptLabel.text = toastLabel.text = feedLabel.text = "";
            blindfold.SetActive(false);
        }

        void OnEnable() {
            ToolControls.interactPressed += TryTag;
            ToolControls.primaryPressed += TryTag;
            NameTagManager.CharacterTagFilter = ShowTag;
        }

        void OnDisable() {
            ToolControls.interactPressed -= TryTag;
            ToolControls.primaryPressed -= TryTag;
            if (NameTagManager.CharacterTagFilter == ShowTag) NameTagManager.CharacterTagFilter = null;
            if (blindfold != null) SetBlind(false);
        }

        void OnDestroy() {
            if (manager == null) return;
            manager.UnregisterBroadcast<HS_StateBroadcast>(OnState);
            manager.UnregisterBroadcast<HS_TagBroadcast>(OnTag);
            manager.UnregisterBroadcast<HS_PingBroadcast>(OnPing);
            manager.UnregisterBroadcast<HS_ResultBroadcast>(OnResult);
        }

        static bool Seeks(HS_Role role) => role is HS_Role.Seeker or HS_Role.Caught;
        int MyId => manager != null && manager.Connection != null ? manager.Connection.ClientId : -1;
        HS_Role MyRole => roles.TryGetValue(MyId, out var r) ? r : HS_Role.None;
        HS_Role RoleOf(int clientId) => roles.TryGetValue(clientId, out var r) ? r : HS_Role.None;

        bool ShowTag(GameCharacter character) {
            if (!hasState || state.phase is not (HS_Phase.Hiding or HS_Phase.Seeking)) return true;
            HS_Role other = RoleOf(character.OwnerId), me = MyRole;
            return Seeks(other) == Seeks(me) || me == HS_Role.None;
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<HS_StateBroadcast>(OnState);
                manager.RegisterBroadcast<HS_TagBroadcast>(OnTag);
                manager.RegisterBroadcast<HS_PingBroadcast>(OnPing);
                manager.RegisterBroadcast<HS_ResultBroadcast>(OnResult);
            }
            if (manager != null && manager.Started && !hasState && Time.unscaledTime > nextRequest) {
                nextRequest = Time.unscaledTime + 2f;
                manager.Broadcast(new HS_StateRequest());
            }
            if (toastLabel.text.Length > 0 && Time.unscaledTime > toastUntil) toastLabel.text = "";
            if (feedLabel.text.Length > 0 && Time.unscaledTime > feedUntil) feedLabel.text = "";
            for (int i = pings.Count - 1; i >= 0; i--) {
                if (Time.unscaledTime < pings[i].until) continue;
                if (pings[i].marker != null) Destroy(pings[i].marker);
                pings.RemoveAt(i);
            }
            RefreshTimer();
            RefreshPrompt();
            ShowPanel(timerLabel);
            ShowPanel(promptLabel);
        }

        /// <summary>Hides a label's backing panel while the label is empty.</summary>
        static void ShowPanel(TMP_Text label) {
            var panel = label.transform.parent.gameObject;
            bool show = label.text.Length > 0;
            if (panel.activeSelf != show) panel.SetActive(show);
        }

        /// <summary>Seekers see nothing at all while hiders hide: the camera stops drawing the world.</summary>
        void SetBlind(bool blind) {
            if (blindfold.activeSelf != blind) blindfold.SetActive(blind);
            var cam = Camera.main;
            if (cam == null) return;
            if (blind && cam.cullingMask != 0) { savedMask = cam.cullingMask; cam.cullingMask = 0; } else if (!blind && cam.cullingMask == 0) cam.cullingMask = savedMask;
        }

        // ---------------------------------------------------------------- input
        void TryTag() {
            if (manager == null || !hasState || state.phase != HS_Phase.Seeking || !Seeks(MyRole)) return;
            manager.Broadcast(new HS_TagRequest { round = state.round });
        }

        LocalCharacter NearestHider(out float distance) {
            distance = float.MaxValue;
            var me = LocalPlayer.Character;
            if (me == null) return null;
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

        void RefreshPrompt() {
            if (!hasState || state.phase != HS_Phase.Seeking || !Seeks(MyRole)) { promptLabel.text = ""; return; }
            var hider = NearestHider(out float d);
            promptLabel.text = hider != null && d <= HS_Rules.TagRange ? $"[E] or click: TAG {hider.DisplayName}" : "";
        }

        // ---------------------------------------------------------------- messages
        void OnState(HS_StateBroadcast message, Channel channel) {
            state = message;
            hasState = true;
            stateAt = Time.unscaledTime;
            roles.Clear();
            if (message.roles != null) foreach (var r in message.roles) roles[r.clientId] = r.role;
            headlineLabel.text = message.headline;
            HS_Role me = MyRole;
            roleLabel.text = message.phase switch {
                HS_Phase.Hiding or HS_Phase.Seeking or HS_Phase.Results => me switch {
                    HS_Role.Hider => "<color=#7CFF8A>YOU ARE HIDING</color>",
                    HS_Role.Seeker => "<color=#FF6B5E>YOU ARE SEEKING</color>",
                    HS_Role.Caught => "<color=#FFB25E>FOUND! Now help seek</color>",
                    _ => "SPECTATING"
                },
                _ => $"HIDE AND SEEK   Round {message.round + 1}"
            };
            bool blind = message.phase == HS_Phase.Hiding && Seeks(me);
            SetBlind(blind);
            if (blind) blindfoldLabel.text = $"Count to {message.secondsLeft}...\nThe hiders are hiding.";
            if (message.phase != lastPhase) {
                if (message.phase == HS_Phase.Hiding) {
                    Toast(me == HS_Role.Hider ? "Run and hide! Seekers are released soon." : "You're a seeker. Wait for the countdown.", startClip, 4f);
                    ClearPings();
                } else if (message.phase == HS_Phase.Seeking) {
                    Toast(Seeks(me) ? "GO! Find them. Press E or click to tag." : "The seekers are coming. Stay hidden!", startClip, 3f);
                }
                lastPhase = message.phase;
            }
        }

        void OnTag(HS_TagBroadcast message, Channel channel) {
            roles[message.hiderId] = HS_Role.Caught;
            feedLabel.text = $"{message.seekerName} found {message.hiderName}!  {message.hidersLeft} left";
            feedUntil = Time.unscaledTime + 4f;
            if (message.hiderId == MyId) Toast("You were found! You're a seeker now.", tagClip, 3f);
            else if (message.seekerId == MyId) Toast($"Tagged {message.hiderName}!", tagClip, 2f);
            else if (sfx != null && tagClip != null) sfx.PlayOneShot(tagClip, 0.5f);
        }

        void OnPing(HS_PingBroadcast message, Channel channel) {
            ClearPings();
            if (map == null || map.pingPrefab == null || message.positions == null) return;
            foreach (var p in message.positions)
                pings.Add((Instantiate(map.pingPrefab, p, Quaternion.identity, map.transform), Time.unscaledTime + 8f));
            Toast("PING! Hiders were spotted near the red beacons.", pingClip, 3f);
        }

        void OnResult(HS_ResultBroadcast message, Channel channel) => Toast(message.message, message.accepted ? pingClip : null, 2.5f);

        void ClearPings() {
            foreach (var p in pings) if (p.marker != null) Destroy(p.marker);
            pings.Clear();
        }

        void RefreshTimer() {
            if (!hasState) { timerLabel.text = ""; return; }
            int left = Mathf.Max(0, state.secondsLeft - (state.phase == HS_Phase.Seeking ? (int)(Time.unscaledTime - stateAt) : 0));
            timerLabel.text = state.phase switch {
                HS_Phase.Hiding => $"Hide  {RoundFormat.Clock(left)}",
                HS_Phase.Seeking => $"{RoundFormat.Clock(left)}   Hiders {state.hidersLeft}/{state.hidersAtStart}",
                _ => ""
            };
        }

        void Toast(string message, AudioClip clip, float seconds) {
            toastLabel.text = message;
            toastUntil = Time.unscaledTime + seconds;
            if (sfx != null && clip != null) sfx.PlayOneShot(clip);
        }
    }
}
