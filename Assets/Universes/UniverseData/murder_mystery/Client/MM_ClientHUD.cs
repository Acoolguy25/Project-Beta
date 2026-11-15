using System.Collections.Generic;
using System.Text;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Input;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Universes.UniverseData.murder_mystery.Client {
    /// <summary>
    /// Murder mystery HUD: role card, body report prompt and markers, meeting notice, ejection banner and the
    /// end-of-round reveal. Presentation only; the server decides everything. Lives on the MM_HUD prefab
    /// (built by MM_Authoring) in the murder_mystery_start scene.
    /// </summary>
    public sealed class MM_ClientHUD : MonoBehaviour {
        static readonly Color Muted = new(0.7f, 0.72f, 0.75f);
        static readonly Color Alarm = new(0.95f, 0.3f, 0.25f);

        public GameObject roleCard, roleBadge, reportPanel, meetingPanel, ejectionPanel, resultsPanel;
        public TMP_Text roleCardTitle, roleCardGoal, roleBadgeText, reportText, meetingText, ejectionTitle, ejectionBody;
        public TMP_Text resultsTitle, resultsSummary, resultsBody, resultsFooter;
        public Image roleCardStripe, ejectionStripe;
        [Tooltip("Shown on the floor where an unreported body lies near the local player.")]
        public GameObject bodyMarkerPrefab;
        public AudioSource sfx;
        public AudioClip meetingClip, ejectionClip;

        ClientManager manager;
        MM_StateBroadcast state;
        bool hasState;
        MM_MeetingBroadcast meeting;
        bool hasMeeting;
        MM_EjectionBroadcast ejection;
        float ejectionUntil;
        MM_ResultsBroadcast results;
        float resultsUntil;
        int revealShownRound = -1;
        float revealUntil;
        readonly List<GameObject> markers = new();

        void Awake() {
            roleCard.SetActive(false);
            roleBadge.SetActive(false);
            reportPanel.SetActive(false);
            meetingPanel.SetActive(false);
            ejectionPanel.SetActive(false);
            resultsPanel.SetActive(false);
        }

        void OnEnable() => ToolControls.interactPressed += TryReport;

        void OnDisable() => ToolControls.interactPressed -= TryReport;

        void OnDestroy() {
            if (manager != null) {
                manager.UnregisterBroadcast<MM_StateBroadcast>(OnState);
                manager.UnregisterBroadcast<MM_MeetingBroadcast>(OnMeeting);
                manager.UnregisterBroadcast<MM_EjectionBroadcast>(OnEjection);
                manager.UnregisterBroadcast<MM_ResultsBroadcast>(OnResults);
            }
            foreach (GameObject marker in markers)
                if (marker != null) Destroy(marker);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<MM_StateBroadcast>(OnState);
                manager.RegisterBroadcast<MM_MeetingBroadcast>(OnMeeting);
                manager.RegisterBroadcast<MM_EjectionBroadcast>(OnEjection);
                manager.RegisterBroadcast<MM_ResultsBroadcast>(OnResults);
            }
            Refresh();
        }

        // ---------- network ----------

        void OnState(MM_StateBroadcast next, Channel channel) {
            state = next;
            hasState = true;
            if (next.classic && next.phase == MM_Phase.RoleReveal && revealShownRound != next.round && next.role != MM_Role.None) {
                revealShownRound = next.round;
                revealUntil = Time.unscaledTime + Mathf.Max(3, next.secondsLeft);
            }
            if (next.phase is not (MM_Phase.Meeting or MM_Phase.Voting))
                hasMeeting = false;
            UpdateMarkers();
        }

        void OnMeeting(MM_MeetingBroadcast next, Channel channel) {
            meeting = next;
            hasMeeting = true;
            revealUntil = 0;
            if (sfx != null && meetingClip != null)
                sfx.PlayOneShot(meetingClip);
        }

        void OnEjection(MM_EjectionBroadcast next, Channel channel) {
            ejection = next;
            ejectionUntil = Time.unscaledTime + 6f;
            hasMeeting = false;
            if (sfx != null && ejectionClip != null && next.ejected)
                sfx.PlayOneShot(ejectionClip);
        }

        void OnResults(MM_ResultsBroadcast next, Channel channel) {
            bool fresh = results.round != next.round || resultsUntil < Time.unscaledTime;
            results = next;
            resultsUntil = Time.unscaledTime + next.secondsLeft + 0.5f;
            if (fresh)
                FillResults();
            resultsFooter.text = $"Next round in {next.secondsLeft}s";
        }

        void TryReport() {
            if (manager == null || !manager.Started || !hasState || state.reportableBody < 0 || state.phase != MM_Phase.Hunt)
                return;
            if (state.meetingsLeft <= 0 || state.reportCooldown > 0)
                return;
            manager.Broadcast(new MM_ReportRequest { bodyId = state.reportableBody });
        }

        // ---------- presentation ----------

        void Refresh() {
            bool classic = hasState && state.classic;
            float now = Time.unscaledTime;

            bool showReveal = classic && now < revealUntil && state.phase == MM_Phase.RoleReveal;
            roleCard.SetActive(showReveal);
            if (showReveal) {
                Color color = MM_Text.RoleColor(state.role);
                roleCardTitle.text = state.role == MM_Role.Spectator ? "YOU ARE SPECTATING" : $"YOU ARE {(state.role == MM_Role.Innocent ? "" : "THE ")}{MM_Text.RoleName(state.role).ToUpperInvariant()}";
                roleCardTitle.color = color;
                roleCardStripe.color = color;
                roleCardGoal.text = MM_Text.RoleGoal(state.role);
            }

            bool showBadge = classic && state.role != MM_Role.None && state.phase != MM_Phase.Results;
            roleBadge.SetActive(showBadge);
            if (showBadge) {
                string role = MM_Text.RoleName(state.role).ToUpperInvariant();
                string color = ColorUtility.ToHtmlStringRGB(MM_Text.RoleColor(state.role));
                string life = state.alive ? "" : "   <color=#B0B0B0>(eliminated)</color>";
                roleBadgeText.text = $"<size=70%><color=#B8BCC2>YOUR ROLE</color></size>\n<color=#{color}><b>{role}</b></color>{life}\n<size=70%><color=#B8BCC2>Meetings left: {state.meetingsLeft}</color></size>";
            }

            string report = null;
            if (classic && state.alive && state.phase == MM_Phase.Hunt && state.reportableBody >= 0) {
                if (state.meetingsLeft <= 0)
                    report = $"Body of {state.reportableBodyName}  -  no meetings left this round";
                else if (state.reportCooldown > 0)
                    report = $"Body of {state.reportableBodyName}  -  you can report in {state.reportCooldown}s";
                else
                    report = $"Press <b>E</b> to report the body of <b>{state.reportableBodyName}</b>";
            }
            reportPanel.SetActive(report != null);
            if (report != null)
                reportText.text = report;

            // During the vote the shared vote window shows the same details, so the notice steps aside.
            bool showMeeting = classic && hasMeeting && state.phase == MM_Phase.Meeting;
            meetingPanel.SetActive(showMeeting);
            if (showMeeting) {
                string suspects = meeting.suspects == null || meeting.suspects.Length == 0 ? "nobody" : string.Join(", ", meeting.suspects);
                string action = $"Discuss in chat. Voting in {state.secondsLeft}s.";
                meetingText.text = $"<color=#F2584A><b>EMERGENCY MEETING</b></color>\n<b>{meeting.reporterName}</b> found the body of <b>{meeting.victimName}</b>.\nSeen nearby: {suspects}\n<size=80%><color=#B8BCC2>{action}</color></size>";
            }

            bool showEjection = now < ejectionUntil && !(results.round == ejection.round && now < resultsUntil);
            ejectionPanel.SetActive(showEjection);
            if (showEjection) {
                bool murderer = ejection.ejected && ejection.role == MM_Role.Murderer;
                ejectionTitle.text = !ejection.ejected ? "NO ONE WAS EJECTED" : murderer ? "MURDERER CAUGHT" : "WRONG PERSON";
                Color color = !ejection.ejected ? Muted : murderer ? MM_Text.RoleColor(MM_Role.Innocent) : Alarm;
                ejectionTitle.color = color;
                ejectionStripe.color = color;
                ejectionBody.text = ejection.headline;
            }

            resultsPanel.SetActive(now < resultsUntil);
        }

        void FillResults() {
            resultsTitle.text = $"{results.winner.ToUpperInvariant()} WIN";
            resultsTitle.color = results.winner.StartsWith("Murder") ? Alarm : MM_Text.RoleColor(MM_Role.Innocent);
            resultsSummary.text = results.summary;
            StringBuilder sb = new();
            sb.Append("<color=#B8BCC2><size=80%>NAME<pos=36%>ROLE<pos=58%>FATE<pos=82%>KILLS</size></color>\n");
            foreach (MM_ResultEntry entry in results.entries ?? System.Array.Empty<MM_ResultEntry>()) {
                string color = ColorUtility.ToHtmlStringRGB(MM_Text.RoleColor(entry.role));
                string who = entry.isPlayer && !string.IsNullOrEmpty(entry.username) ? $"{entry.name} <size=70%><color=#B8BCC2>({entry.username})</color></size>" : $"{entry.name} <size=70%><color=#B8BCC2>(NPC)</color></size>";
                string fate = entry.ejected ? "Ejected" : entry.survived ? "Survived" : "Died";
                sb.Append($"{who}<pos=36%><color=#{color}>{MM_Text.RoleName(entry.role)}</color><pos=58%>{fate}<pos=82%>{entry.kills}\n");
            }
            resultsBody.text = sb.ToString();
        }

        void UpdateMarkers() {
            Vector3[] bodies = hasState && state.classic && state.alive && state.phase == MM_Phase.Hunt && state.nearbyBodies != null
                ? state.nearbyBodies : System.Array.Empty<Vector3>();
            if (bodyMarkerPrefab == null)
                return;
            while (markers.Count < bodies.Length)
                markers.Add(CreateMarker());
            for (int i = 0; i < markers.Count; i++) {
                bool used = i < bodies.Length;
                markers[i].SetActive(used);
                if (used)
                    markers[i].transform.position = bodies[i];
            }
        }

        GameObject CreateMarker() {
            GameObject marker = Instantiate(bodyMarkerPrefab);
            SceneManager.MoveGameObjectToScene(marker, gameObject.scene);
            return marker;
        }
    }
}
