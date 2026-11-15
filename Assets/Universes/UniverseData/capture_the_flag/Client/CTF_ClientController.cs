using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Rounds;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>Shows the score, both flags and flag events from server state. Decides nothing.</summary>
    public sealed class CTF_ClientController : MonoBehaviour {
        public TMP_Text redScoreLabel, blueScoreLabel, timerLabel, teamLabel, toastLabel, objectiveLabel, bannerLabel;
        public Image teamStripe;
        public AudioSource sfx;
        public AudioClip takenClip, capturedClip, returnedClip;

        CTF_Map map;
        ClientManager manager;
        CTF_StateBroadcast state;
        bool hasState, hasTeam;
        CTF_Team myTeam;
        float toastUntil, bannerUntil, nextRequest, stateAt;
        readonly CTF_FlagView[] views = new CTF_FlagView[2];
        readonly Vector3[] shown = new Vector3[2];

        void Awake() {
            map = GetComponentInParent<CTF_Map>();
            redScoreLabel.text = blueScoreLabel.text = "0";
            timerLabel.text = toastLabel.text = objectiveLabel.text = bannerLabel.text = "";
            teamLabel.text = "FLAG WARS";
        }

        void Start() {
            for (int i = 0; i < 2; i++) {
                if (map.flagPrefabs[i] == null) continue;
                views[i] = Instantiate(map.flagPrefabs[i], map.flagStands[i].position, Quaternion.identity, map.transform).GetComponent<CTF_FlagView>();
                shown[i] = map.flagStands[i].position;
            }
        }

        void OnDestroy() {
            if (manager == null) return;
            manager.UnregisterBroadcast<CTF_StateBroadcast>(OnState);
            manager.UnregisterBroadcast<CTF_TeamBroadcast>(OnTeam);
            manager.UnregisterBroadcast<CTF_EventBroadcast>(OnEvent);
        }

        int MyId => manager != null && manager.Connection != null ? manager.Connection.ClientId : -1;

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<CTF_StateBroadcast>(OnState);
                manager.RegisterBroadcast<CTF_TeamBroadcast>(OnTeam);
                manager.RegisterBroadcast<CTF_EventBroadcast>(OnEvent);
            }
            if (manager != null && manager.Started && (!hasState || !hasTeam) && Time.unscaledTime > nextRequest) {
                nextRequest = Time.unscaledTime + 2f;
                manager.Broadcast(new CTF_StateRequest());
            }
            if (toastLabel.text.Length > 0 && Time.unscaledTime > toastUntil) toastLabel.text = "";
            if (bannerLabel.text.Length > 0 && Time.unscaledTime > bannerUntil) bannerLabel.text = "";
            var objectivePanel = objectiveLabel.transform.parent.gameObject;
            if (objectivePanel.activeSelf != objectiveLabel.text.Length > 0) objectivePanel.SetActive(objectiveLabel.text.Length > 0);
            if (hasState) {
                PlaceFlag(0, state.redFlag);
                PlaceFlag(1, state.blueFlag);
                RefreshObjective();
                int left = state.phase == CTF_Phase.Playing ? Mathf.Max(0, state.secondsLeft - (int)(Time.unscaledTime - stateAt)) : state.secondsLeft;
                timerLabel.text = state.phase switch {
                    CTF_Phase.Playing => RoundFormat.Clock(left),
                    CTF_Phase.Countdown => $"Starts in {left}",
                    CTF_Phase.Results => "Match over",
                    _ => "Waiting"
                };
            }
        }

        void PlaceFlag(int i, CTF_FlagSnap snap) {
            var view = views[i];
            if (view == null) return;
            Vector3 target = snap.position;
            if (snap.state == CTF_FlagState.Carried) {
                var carrier = CharacterLookup.ByClientId(snap.carrierId);
                if (carrier != null) target = carrier.transform.position + carrier.transform.up * 2.1f - carrier.transform.forward * 0.4f;
            }
            float follow = snap.state == CTF_FlagState.Carried ? 30f : 12f;
            shown[i] = (shown[i] - target).sqrMagnitude > 400f ? target : Vector3.Lerp(shown[i], target, 1f - Mathf.Exp(-follow * Time.deltaTime));
            view.transform.position = shown[i];
            view.transform.localScale = Vector3.one * (snap.state == CTF_FlagState.Carried ? 0.55f : 1f);
            var team = (CTF_Team)i;
            view.Show(snap.state switch {
                CTF_FlagState.Carried => "",
                CTF_FlagState.Dropped => $"<color={CTF_Rules.Hex(team)}>{CTF_Rules.Name(team)} FLAG</color>\n<size=70%>dropped</size>",
                _ => $"<color={CTF_Rules.Hex(team)}>{CTF_Rules.Name(team)} FLAG</color>"
            }, snap.state == CTF_FlagState.Home);
        }

        void RefreshObjective() {
            if (!hasTeam || state.phase != CTF_Phase.Playing) { objectiveLabel.text = state.phase == CTF_Phase.Countdown ? "Get ready! Pistol (1) and dagger (2)." : ""; return; }
            var enemy = CTF_Rules.Other(myTeam);
            var mine = myTeam == CTF_Team.Red ? state.redFlag : state.blueFlag;
            var theirs = enemy == CTF_Team.Red ? state.redFlag : state.blueFlag;
            if (theirs.state == CTF_FlagState.Carried && theirs.carrierId == MyId)
                objectiveLabel.text = mine.state == CTF_FlagState.Home ? "You have the flag! Run it back to your base!" : "You have the flag! Your flag must be home to score.";
            else if (mine.state == CTF_FlagState.Carried) objectiveLabel.text = "Your flag was taken! Stop the carrier!";
            else if (mine.state == CTF_FlagState.Dropped) objectiveLabel.text = "Your flag is on the ground. Touch it to return it!";
            else if (theirs.state == CTF_FlagState.Carried) objectiveLabel.text = "A teammate has their flag. Cover them!";
            else objectiveLabel.text = $"Grab the <color={CTF_Rules.Hex(enemy)}>{CTF_Rules.Name(enemy)}</color> flag from their base!";
            if (LocalPlayer.Character != null && map != null) {
                bool carrying = theirs.state == CTF_FlagState.Carried && theirs.carrierId == MyId;
                float d = Vector3.Distance(LocalPlayer.Character.transform.position, carrying ? map.Stand(myTeam) : theirs.position);
                objectiveLabel.text += $"  <size=75%>({Mathf.RoundToInt(d)} m)</size>";
            }
        }

        void OnState(CTF_StateBroadcast message, Channel channel) {
            state = message;
            hasState = true;
            stateAt = Time.unscaledTime;
            redScoreLabel.text = message.redScore.ToString();
            blueScoreLabel.text = message.blueScore.ToString();
        }

        void OnTeam(CTF_TeamBroadcast message, Channel channel) {
            myTeam = message.team;
            hasTeam = true;
            teamLabel.text = $"YOU ARE <color={CTF_Rules.Hex(myTeam)}>{CTF_Rules.Name(myTeam)}</color>";
            if (teamStripe != null) teamStripe.color = CTF_Rules.Color(myTeam);
        }

        void OnEvent(CTF_EventBroadcast message, Channel channel) {
            string flag = $"<color={CTF_Rules.Hex(message.flag)}>{CTF_Rules.Name(message.flag)}</color> flag";
            bool ours = hasTeam && message.flag == myTeam;
            string who = message.playerId == MyId ? "You" : message.playerName;
            switch (message.kind) {
                case CTF_EventKind.Taken:
                    Toast($"{who} took the {flag}!", takenClip);
                    break;
                case CTF_EventKind.Dropped:
                    Toast($"{who} dropped the {flag}.", null);
                    break;
                case CTF_EventKind.Returned:
                    Toast(string.IsNullOrEmpty(message.playerName) ? $"The {flag} went home." : $"{who} returned the {flag}.", returnedClip);
                    break;
                case CTF_EventKind.Captured:
                    Banner(ours ? $"{who} CAPTURED YOUR FLAG" : $"{who.ToUpperInvariant()} CAPTURED THE {flag}!", capturedClip);
                    break;
                case CTF_EventKind.Won:
                    Banner(hasTeam && message.flag == myTeam ? "VICTORY!" : $"<color={CTF_Rules.Hex(message.flag)}>{CTF_Rules.Name(message.flag)}</color> WINS", capturedClip, 8f);
                    break;
            }
        }

        void Toast(string text, AudioClip clip) {
            toastLabel.text = text;
            toastUntil = Time.unscaledTime + 3f;
            if (sfx != null && clip != null) sfx.PlayOneShot(clip);
        }

        void Banner(string text, AudioClip clip, float seconds = 3.5f) {
            bannerLabel.text = text;
            bannerUntil = Time.unscaledTime + seconds;
            if (sfx != null && clip != null) sfx.PlayOneShot(clip);
        }
    }
}
