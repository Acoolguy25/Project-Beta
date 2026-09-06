using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Input;
using RyanAssets.Shared.Globals;
using RyanAssets.Tools.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>Presentation and input only; the server decides every case outcome.</summary>
    public sealed class CH_ClientController : MonoBehaviour {
        public TMP_Text caseLabel, chapterLabel, objectiveLabel, countersLabel, clockLabel, interactionLabel;
        public TMP_Text dialogueLabel, controlsLabel, bearingLabel, journalBody, journalPageLabel, endingLabel;
        public GameObject dialoguePanel, journalPanel, endingPanel;
        public UnityEngine.UI.Image dangerOverlay;
        public UnityEngine.UI.Button journalClose, journalNext, journalPrevious;
        public AudioSource radioAudio, dangerAudio;
        public AudioClip radioCue;
        readonly Dictionary<int, GameObject> pointViews = new();
        CH_Map map;
        ClientManager manager;
        CH_StateBroadcast state;
        InputAction closeJournal;
        int focusedId = -1, lastDialogue = -1, journalPage;
        float dialogueUntil, nextSnapshot, nextFocus;
        float feedbackUntil;
        string interactionFeedback;
        bool hasState, journalOpen;
        CH_Jumpscare jumpscare;
        int visibilityMask;

        void Awake() {
            map = GetComponentInParent<CH_Map>();
            jumpscare = GetComponent<CH_Jumpscare>();
            visibilityMask = ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast");
            closeJournal = new InputAction("Close case journal", InputActionType.Button, "<Keyboard>/f");
            closeJournal.AddBinding("<Keyboard>/escape");
            closeJournal.AddBinding("<Gamepad>/buttonEast");
            closeJournal.performed += CloseJournalInput;
            journalClose.onClick.AddListener(CloseJournal);
            journalNext.onClick.AddListener(NextPage);
            journalPrevious.onClick.AddListener(PreviousPage);
            journalPanel.SetActive(false);
            endingPanel.SetActive(false);
            dialoguePanel.SetActive(false);
            chapterLabel.text = "INVESTIGATOR";
            objectiveLabel.text = "Establishing the radio link...";
            controlsLabel.text = "E  Interact    F  Journal";
        }
        void OnEnable() {
            ToolControls.interactPressed += Interact;
            ToolControls.journalPressed += OpenJournal;
        }
        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<CH_StateBroadcast>(OnState);
                manager.RegisterBroadcast<CH_ScareBroadcast>(OnScare);
                manager.RegisterBroadcast<CH_InteractionResult>(OnInteractionResult);
            }
            if (manager != null && manager.Started && Time.unscaledTime > nextSnapshot && !hasState) {
                nextSnapshot = Time.unscaledTime + 2;
                manager.Broadcast(new CH_StateRequest { version = 1 });
            }
            if (!hasState) return;
            if (Time.unscaledTime > nextFocus) { nextFocus = Time.unscaledTime + 0.08f; UpdateFocus(); }
            dialoguePanel.SetActive(!journalOpen && Time.unscaledTime < dialogueUntil && !endingPanel.activeSelf);
            UpdateThreat();
            bool outOfCase = LocalPlayer.Character == null || LocalPlayer.Character.IsDead;
            bool spectating = RyanAssets.DataService.PlayerData.localData != null
                && RyanAssets.DataService.PlayerData.localData.lockedCameraType.Value == (int)RyanAssets.Shared.Declarations.GameCameraType.SpectateCamera;
            controlsLabel.text = outOfCase ? spectating ? "SPECTATING / Return with the next investigation    F  Journal"
                : "REVIVING / Your evidence is safe    F  Journal" : "E  Interact    F  Journal";
        }

        void OnScare(CH_ScareBroadcast scare, Channel channel) {
            if (!hasState || scare.seed != state.seed || jumpscare == null) return;
            jumpscare.Play(scare.sequence, scare.kind);
        }
        void OnInteractionResult(CH_InteractionResult result, Channel channel) {
            if (!hasState || result.seed != state.seed) return;
            interactionFeedback = result.accepted ? "" : result.message;
            feedbackUntil = result.accepted ? 0 : Time.unscaledTime + 2.5f;
            nextFocus = 0;
        }

        void OnState(CH_StateBroadcast next, Channel channel) {
            if (next.points == null || next.journal == null || map == null) return;
            bool fresh = !hasState || state.seed != next.seed;
            if (fresh) {
                CloseJournal();
                jumpscare?.ResetCase();
                foreach (var view in pointViews.Values) if (view != null) Destroy(view);
                pointViews.Clear();
                lastDialogue = -1;
                journalPage = 0;
                feedbackUntil = 0;
            }
            bool chapterChanged = !fresh && next.phase != state.phase;
            state = next;
            hasState = true;
            caseLabel.text = $"CASE {unchecked((uint)next.seed):X8}    /    {next.caseTitle}";
            chapterLabel.text = next.phase == CH_Phase.Investigation ? "I   /   THE LAST CALL" : "II   /   WHAT ANSWERED";
            objectiveLabel.text = next.objective.Contains("|") ? next.objective.Substring(next.objective.IndexOf('|') + 1).Trim() : next.objective;
            int revives = RyanAssets.DataService.PlayerData.localData != null ? RyanAssets.DataService.PlayerData.localData.lives.Value : 0;
            countersLabel.text = $"EVIDENCE  {next.evidenceCount}/4     OFFERINGS  {next.relicCount}/3     REVIVES  {revives}";
            clockLabel.text = $"{next.secondsLeft / 60:00}:{next.secondsLeft % 60:00}";
            if (lastDialogue != next.dialogueRevision) {
                lastDialogue = next.dialogueRevision;
                dialogueLabel.text = next.dialogue;
                dialogueUntil = Time.unscaledTime + Mathf.Clamp(next.dialogue.Length / 14f, 8, 24);
                if (radioAudio != null && radioCue != null) radioAudio.PlayOneShot(radioCue, 0.14f);
            }
            if (chapterChanged && next.phase == CH_Phase.Descent) dialogueUntil = Time.unscaledTime + 20;
            endingPanel.SetActive(next.phase is CH_Phase.Complete or CH_Phase.Failed);
            endingLabel.text = next.ending + $"\n\nNext case in {next.secondsLeft}s\nA new source. New evidence. New rules.\nCases solved this session: {next.completedCases}";
            RefreshJournal();
            // Phase changes remove points from snapshots; those old views must
            // disappear too, otherwise completed offerings look interactable.
            foreach (var view in pointViews.Values) if (view != null) view.SetActive(false);
            foreach (var point in next.points) {
                if (!pointViews.TryGetValue(point.id, out var view)) {
                    view = Instantiate(point.id == 9 ? map.sourceViewPrefab : map.clueViewPrefab, map.transform);
                    view.name = $"{point.title} / {point.area}";
                    view.transform.position = point.position;
                    if (point.id >= 11) view.transform.localScale = Vector3.one * 0.65f;
                    pointViews.Add(point.id, view);
                }
                view.SetActive(!point.collected && !endingPanel.activeSelf);
            }
        }

        void UpdateFocus() {
            focusedId = -1;
            interactionLabel.text = "";
            bearingLabel.text = "";
            var camera = Camera.main;
            if (camera == null || LocalPlayer.Character == null || LocalPlayer.Character.IsDead || journalOpen || endingPanel.activeSelf) return;
            float best = 0.9f, nearest = float.MaxValue;
            CH_PointState nearestPoint = default;
            foreach (var point in state.points) {
                if (point.collected || point.id == 9 && state.relicCount == 3) continue;
                Vector3 direction = point.position - camera.transform.position;
                float distance = direction.magnitude;
                if (distance < nearest && (point.id < 4 || point.id >= 6)) { nearest = distance; nearestPoint = point; }
                float alignment = distance < 0.1f ? 1 : Vector3.Dot(camera.transform.forward, direction / distance);
                if (distance <= 3.5f && alignment > best && WorldInteraction.CanReach(camera.transform.position, point.position, 3.5f, visibilityMask)) {
                    best = alignment;
                    focusedId = point.id;
                    interactionLabel.text = point.id == 9
                        ? state.relicCount < 3 ? "THE SOURCE / Recover all three offerings" : "Approach an offering. Follow the order in your journal."
                        : $"[E]  {point.title.ToUpperInvariant()}";
                }
            }
            if (nearest < float.MaxValue) {
                Vector3 direction = nearestPoint.position - camera.transform.position;
                float bearing = Vector3.SignedAngle(Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up), Vector3.ProjectOnPlane(direction, Vector3.up), Vector3.up);
                string heading = Mathf.Abs(bearing) < 25 ? "AHEAD" : Mathf.Abs(bearing) > 140 ? "BEHIND" : bearing < 0 ? "LEFT" : "RIGHT";
                bearingLabel.text = $"SEARCH / {nearestPoint.area}   {heading}   {Mathf.CeilToInt(nearest)} m";
            }
            if (Time.unscaledTime < feedbackUntil) interactionLabel.text = interactionFeedback;
        }
        void Interact() {
            // Input can arrive between focus scans, especially while turning.
            UpdateFocus();
            if (focusedId >= 0 && focusedId != 9) SendInteraction(focusedId, -1);
        }
        void SendInteraction(int point, int option) {
            if (manager == null || !manager.Started || journalOpen) return;
            manager.Broadcast(new CH_InteractRequest { seed = state.seed, targetId = point, option = option });
        }

        void OpenJournal() {
            if (!hasState || journalOpen) return;
            journalOpen = true;
            journalPanel.SetActive(true);
            InputService.SetInputScreenActive(InputScreen.GameMenu, true);
            closeJournal.Enable();
            RefreshJournal();
        }
        void CloseJournalInput(InputAction.CallbackContext _) => CloseJournal();
        void CloseJournal() {
            if (!journalOpen) return;
            journalOpen = false;
            journalPanel.SetActive(false);
            closeJournal.Disable();
            InputService.SetInputScreenActive(InputScreen.GameMenu, false);
        }
        void NextPage() { journalPage++; RefreshJournal(); }
        void PreviousPage() { journalPage--; RefreshJournal(); }
        void RefreshJournal() {
            if (state.journal == null || state.journal.Length == 0) return;
            journalPage = Mathf.Clamp(journalPage, 0, state.journal.Length - 1);
            journalBody.text = state.journal[journalPage];
            journalPageLabel.text = $"FIELD NOTES    {journalPage + 1} / {state.journal.Length}";
            journalPrevious.interactable = journalPage > 0;
            journalNext.interactable = journalPage + 1 < state.journal.Length;
        }
        void UpdateThreat() {
            float intensity = 0;
            if (manager != null && Camera.main != null && manager.Objects.Spawned.TryGetValue(state.monsterId, out var monster)) {
                float distance = Vector3.Distance(monster.transform.position, Camera.main.transform.position);
                intensity = Mathf.InverseLerp(42, 7, distance);
            }
            if (state.phase is CH_Phase.Complete or CH_Phase.Failed) intensity = 0;
            Color color = dangerOverlay.color;
            color.a = Mathf.Lerp(color.a, intensity * (0.07f + 0.025f * Mathf.Sin(Time.unscaledTime * 5)), Time.unscaledDeltaTime * 4);
            dangerOverlay.color = color;
            if (dangerAudio != null) {
                dangerAudio.volume = intensity * 0.38f;
                dangerAudio.pitch = 0.7f + intensity * 0.35f;
            }
            if (map.practicalLights != null) foreach (var light in map.practicalLights)
                if (light != null) light.intensity = Mathf.Lerp(0.55f, 0.06f + Mathf.PerlinNoise(Time.time * 7, light.transform.position.x) * 0.45f, intensity);
        }
        void OnDisable() {
            ToolControls.interactPressed -= Interact;
            ToolControls.journalPressed -= OpenJournal;
            CloseJournal();
        }
        void OnDestroy() {
            if (manager != null) {
                manager.UnregisterBroadcast<CH_StateBroadcast>(OnState);
                manager.UnregisterBroadcast<CH_ScareBroadcast>(OnScare);
                manager.UnregisterBroadcast<CH_InteractionResult>(OnInteractionResult);
            }
            closeJournal?.Dispose();
            foreach (var view in pointViews.Values) if (view != null) Destroy(view);
        }
    }
}
