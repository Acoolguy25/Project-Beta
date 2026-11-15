using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientAudio;
using RyanAssets.Input;
using RyanAssets.Shared.Globals;
using RyanAssets.Tools.Shared;
using RyanAssets.UI.GameHud;
using RyanAssets.UI.Navigation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>Presentation and input only; the server decides every case outcome.</summary>
    public sealed class CH_ClientController : MonoBehaviour {
        const float FootstepPaceMultiplier = 3f;
        [Header("Case status")]
        public TMP_Text chapterLabel;
        public TMP_Text objectiveLabel;
        public UnityEngine.UI.Image caseIcon;
        public HudChip progressChip, timerChip;
        public ObjectiveCompass objectiveCompass;

        [Header("Prompts and radio")]
        public HudPopup interactionPrompt;
        public TMP_Text interactionLabel;
        [Tooltip("The E keycap; hidden when the prompt shows feedback rather than an action.")]
        public GameObject interactionKey;
        public HudPopup dialoguePopup;
        public TMP_Text dialogueLabel;

        [Header("Journal")]
        public GameObject journalPanel;
        public TMP_Text journalBody, journalPageLabel;

        [Header("Ending")]
        public HudPopup endingPopup;
        public UnityEngine.UI.Image endingDisc, endingIcon, endingStripe;
        public TMP_Text endingTitle, endingLabel;
        public HudChip endingNextCase, endingSolved;

        [Header("Icons")]
        public Sprite evidenceIcon;
        public Sprite offeringIcon, solvedIcon, failedIcon;

        public UnityEngine.UI.Image dangerOverlay;
        public UnityEngine.UI.Button journalClose, journalNext, journalPrevious;
        public AudioSource radioAudio, dangerAudio, objectiveCollectAudio;
        public AudioClip radioCue, objectiveCollectCue;
        readonly Dictionary<int, GameObject> pointViews = new();
        readonly List<Vector3> navigationObjectives = new();
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
        CharacterAnimator localCharacterAnimator;
        float previousFootstepPaceMultiplier = 1f;

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
            endingPopup.HideImmediate();
            dialoguePopup.HideImmediate();
            interactionPrompt.HideImmediate();
            // LocalCharacter.OnCharacterAdded.Subscribe();
            chapterLabel.text = "Investigator";
            objectiveLabel.text = "Establishing the radio link...";
        }
        // void OnCharacterAdded(LocalCharacter character){
        //     character.OnDied += (_, _) => [

        //     ]
        // }
        void OnEnable() {
            ToolControls.interactPressed += Interact;
            ToolControls.journalPressed += OpenJournal;
        }
        void Update() {
            ConfigureLocalFootsteps();
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
            dialoguePopup.SetVisible(!journalOpen && Time.unscaledTime < dialogueUntil && !CaseOver);
            UpdateThreat();
        }

        void OnScare(CH_ScareBroadcast scare, Channel channel) {
            if (!hasState || scare.seed != state.seed || jumpscare == null) return;
            jumpscare.Play(scare.sequence, scare.kind);
        }

        void ConfigureLocalFootsteps() {
            if (localCharacterAnimator != null || LocalPlayer.Character == null
                || !LocalPlayer.Character.TryGetComponent(out localCharacterAnimator)) return;

            previousFootstepPaceMultiplier = localCharacterAnimator.FootstepPaceMultiplier;
            localCharacterAnimator.FootstepPaceMultiplier = FootstepPaceMultiplier;
        }
        void OnInteractionResult(CH_InteractionResult result, Channel channel) {
            if (!hasState || result.seed != state.seed) return;
            interactionFeedback = result.accepted ? "" : result.message;
            feedbackUntil = result.accepted ? 0 : Time.unscaledTime + 2.5f;
            if (result.accepted && result.playObjectiveCollect && objectiveCollectAudio != null && objectiveCollectCue != null)
                objectiveCollectAudio.PlayOneShot(objectiveCollectCue);
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
            chapterLabel.text = next.phase switch {
                CH_Phase.Investigation => "Chapter I  ·  The last call",
                CH_Phase.Descent => "Chapter II  ·  What answered",
                CH_Phase.Escape => "Chapter III  ·  Escape",
                _ => "Case closed",
            };
            objectiveLabel.text = next.objective.Contains("|") ? next.objective.Substring(next.objective.IndexOf('|') + 1).Trim() : next.objective;
            bool investigating = next.phase == CH_Phase.Investigation;
            progressChip.SetIcon(investigating ? evidenceIcon : offeringIcon);
            progressChip.Text = investigating ? $"Evidence  {next.evidenceCount} / 4" : $"Offerings  {next.relicCount} / 3";
            if (caseIcon != null) caseIcon.sprite = investigating ? evidenceIcon : offeringIcon;
            timerChip.Text = FormatTime(next.secondsLeft);
            if (lastDialogue != next.dialogueRevision) {
                lastDialogue = next.dialogueRevision;
                dialogueLabel.text = next.dialogue;
                dialogueUntil = Time.unscaledTime + Mathf.Clamp(next.dialogue.Length / 14f, 8, 24);
                if (radioAudio != null && radioCue != null) radioAudio.PlayOneShot(radioCue, 0.14f);
            }
            if (chapterChanged && next.phase == CH_Phase.Descent) dialogueUntil = Time.unscaledTime + 20;
            ShowEnding(next);
            RefreshJournal();
            // Phase changes remove points from snapshots; those old views must
            // disappear too, otherwise completed offerings look interactable.
            foreach (var view in pointViews.Values) if (view != null) view.SetActive(false);
            foreach (var point in next.points) {
                if (!pointViews.TryGetValue(point.id, out var view)) {
                    view = Instantiate(point.id == 9 ? map.sourceViewPrefab : map.clueViewPrefab, map.transform);
                    view.name = $"{point.title} / {point.area}";
                    view.transform.position = point.position;
                    if (point.id >= 11 && point.id <= 13) view.transform.localScale = Vector3.one * 0.65f;
                    pointViews.Add(point.id, view);
                }
                view.SetActive(!point.collected && !CaseOver);
            }
            RefreshNavigationObjectives();
        }

        bool CaseOver => hasState && state.phase is CH_Phase.Complete or CH_Phase.Failed;

        /// <summary>The ending card: the epilogue's first line is its title, the rest the story.</summary>
        void ShowEnding(CH_StateBroadcast next) {
            bool over = next.phase is CH_Phase.Complete or CH_Phase.Failed;
            endingPopup.SetVisible(over);
            if (!over) return;
            string ending = next.ending ?? string.Empty;
            int split = ending.IndexOf('\n');
            string title = split >= 0 ? ending.Substring(0, split) : ending;
            string story = split >= 0 ? ending.Substring(split + 1) : string.Empty;
            bool solved = next.phase == CH_Phase.Complete;
            Color accent = solved ? new Color(0.94f, 0.68f, 0.32f) : new Color(0.86f, 0.24f, 0.2f);
            endingTitle.text = string.IsNullOrEmpty(title) ? (solved ? "CASE CLOSED" : "STILL MISSING") : title.ToUpperInvariant();
            endingTitle.color = Color.Lerp(accent, Color.white, 0.3f);
            endingLabel.text = story;
            endingDisc.color = endingStripe.color = accent;
            endingIcon.sprite = solved ? solvedIcon : failedIcon;
            endingNextCase.Text = $"Next case in {next.secondsLeft}s";
            endingSolved.Text = $"Cases solved: {next.completedCases}";
        }

        void UpdateFocus() {
            focusedId = -1;
            string prompt = null;
            var camera = Camera.main;
            bool canNavigate = camera != null && LocalPlayer.Character != null && !LocalPlayer.Character.IsDead
                && !journalOpen && !CaseOver;
            objectiveCompass?.SetDisplayAllowed(canNavigate);
            if (!canNavigate) {
                interactionPrompt.SetVisible(false);
                return;
            }
            float best = 0.9f;
            foreach (var point in state.points) {
                if (point.collected || point.id == 9 && state.relicCount == 3) continue;
                Vector3 direction = point.position - camera.transform.position;
                float distance = direction.magnitude;
                float alignment = distance < 0.1f ? 1 : Vector3.Dot(camera.transform.forward, direction / distance);
                if (distance <= 3.5f && alignment > best && WorldInteraction.CanReach(camera.transform.position, point.position, 3.5f, visibilityMask)) {
                    best = alignment;
                    focusedId = point.id;
                    prompt = point.id == 9
                        ? state.relicCount < 3 ? "<b>The source</b>  ·  recover all three offerings" : "Approach an offering. Follow the order in your journal."
                        : point.title;
                }
            }
            // The keycap only shows for something E acts on; the source and feedback are just text.
            bool actionable = prompt != null && focusedId != 9;
            if (Time.unscaledTime < feedbackUntil && !string.IsNullOrEmpty(interactionFeedback)) {
                prompt = interactionFeedback;
                actionable = false;
            }
            interactionPrompt.SetVisible(prompt != null);
            if (prompt == null) return;
            interactionLabel.text = prompt;
            if (interactionKey.activeSelf != actionable) interactionKey.SetActive(actionable);
        }
        void RefreshNavigationObjectives() {
            navigationObjectives.Clear();
            if (state.points != null && state.phase is not (CH_Phase.Complete or CH_Phase.Failed)) {
                foreach (var point in state.points) {
                    if (point.navigationTarget && !point.collected && (point.id != 9 || state.relicCount < 3))
                        navigationObjectives.Add(point.position);
                }
            }
            objectiveCompass?.SetObjectives(navigationObjectives);
        }
        static string FormatTime(int seconds) {
            seconds = Mathf.Max(0, seconds);
            return $"{seconds / 60:00}:{seconds % 60:00}";
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
            journalPageLabel.text = $"Page {journalPage + 1} of {state.journal.Length}";
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
            objectiveCompass?.SetDisplayAllowed(false);
            CloseJournal();
        }
        void OnDestroy() {
            if (localCharacterAnimator != null)
                localCharacterAnimator.FootstepPaceMultiplier = previousFootstepPaceMultiplier;
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
