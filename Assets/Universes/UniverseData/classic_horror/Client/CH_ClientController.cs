using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using RyanAssets.Input;
using RyanAssets.Shared.Globals;
using RyanAssets.Tools.Shared;
using RyanAssets.UI.GameHud;
using RyanAssets.UI.Navigation;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// Presentation and input only; the server decides every case outcome. Binds the server's case
    /// state to the HUD, the world markers and the presenters for the radio, journal, fear, scares
    /// and atmosphere that sit beside it on the investigation HUD.
    /// </summary>
    public sealed class CH_ClientController : MonoBehaviour {
        [Header("Case status")]
        public TMP_Text chapterLabel;
        public TMP_Text objectiveLabel;
        public UnityEngine.UI.Image caseIcon;
        public HudChip progressChip, timerChip;
        public ObjectiveCompass objectiveCompass;

        [Header("Prompts")]
        public HudPopup interactionPrompt;
        public TMP_Text interactionLabel;
        [Tooltip("The E keycap; hidden when the prompt shows feedback rather than an action.")]
        public GameObject interactionKey;
        [SerializeField, Min(0.5f)] float interactionReach = 3.5f;
        [Tooltip("How directly the reticle must face a record to focus it (dot product).")]
        [SerializeField, Range(0f, 1f)] float focusAlignment = 0.9f;
        [SerializeField, Min(0.02f)] float focusInterval = 0.08f;
        [SerializeField, Min(0.5f)] float feedbackSeconds = 2.5f;
        [Tooltip("Offerings placed at the source are smaller than the records found around the map.")]
        [SerializeField, Range(0.1f, 1f)] float offeringViewScale = 0.65f;
        [Tooltip("Case points sit above the ground for focus and the compass; their props are set down on whatever is below within this distance.")]
        [SerializeField, Min(0f)] float viewGroundProbe = 3f;

        [Header("Presenters")]
        public CH_RadioFeed radio;
        public CH_Journal journal;
        public CH_FearLoop fearLoop;
        public CH_AmbientScares ambientScares;
        public CH_Jumpscare jumpscare;

        [Header("Ending")]
        public HudPopup endingPopup;
        public UnityEngine.UI.Image endingDisc, endingIcon, endingStripe;
        public TMP_Text endingTitle, endingLabel, endingSummary;
        public HudChip endingNextCase, endingSolved;
        [SerializeField] Color solvedAccent = new(0.94f, 0.68f, 0.32f);
        [SerializeField] Color failedAccent = new(0.86f, 0.24f, 0.2f);

        [Header("Icons")]
        public Sprite evidenceIcon;
        public Sprite offeringIcon, solvedIcon, failedIcon;

        [Header("Feedback")]
        public AudioSource objectiveCollectAudio;
        public AudioClip objectiveCollectCue;
        [Tooltip("Burst played where any investigator secures a record.")]
        [SerializeField] Color collectBurstTint = new(1f, 0.72f, 0.36f);
        [SerializeField, Min(0f)] float collectBurstScale = 0.6f;
        [SerializeField, Range(0f, 1f)] float collectShake = 0.12f;
        [SerializeField, Min(0f)] float collectShakeSeconds = 0.2f;
        [SerializeField] Color chapterTitleColor = new(0.94f, 0.68f, 0.32f);
        [SerializeField, Min(0.5f)] float chapterTitleSeconds = 3f;
        [Tooltip("Put the flashlight in the investigator's hand when they spawn empty-handed; the night is unplayable without it.")]
        [SerializeField] bool equipFlashlightOnSpawn = true;
        [Tooltip("Footstep cadence while investigating (the shared default suits a third-person robot).")]
        [SerializeField, Min(0.1f)] float footstepPaceMultiplier = 3f;

        static readonly string[] TemperamentNames = { "Light seeker", "Light shy", "Listener" };

        readonly Dictionary<int, GameObject> pointViews = new();
        readonly Dictionary<int, CH_EvidenceGlow> pointGlows = new();
        readonly HashSet<int> collectedPoints = new();
        readonly List<Vector3> navigationObjectives = new();
        CH_Map map;
        ClientManager manager;
        CH_StateBroadcast state;
        int focusedId = -1, lastDialogue = -1;
        float nextSnapshot, nextFocus, feedbackUntil;
        string interactionFeedback;
        bool hasState;
        int visibilityMask;
        CharacterAnimator localCharacterAnimator;
        float previousFootstepPaceMultiplier = 1f;
        CH_EvidenceGlow focusedGlow;
        ToolFlashlightShared offeredFlashlight;
        // What the case chips show, so their text is only rebuilt when it changes.
        bool shownStatus, shownInvestigating;
        int shownProgress, shownSeconds = -1;
        bool endingShown;
        int endingSecondsShown = -1;

        bool CaseOver => hasState && state.phase is CH_Phase.Complete or CH_Phase.Failed;

        void Awake() {
            map = GetComponentInParent<CH_Map>();
            visibilityMask = ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast");
            endingPopup.HideImmediate();
            interactionPrompt.HideImmediate();
            radio.Clear();
            chapterLabel.text = "Investigator";
            objectiveLabel.text = "Establishing the radio link...";
        }

        void OnEnable() {
            ToolControls.interactPressed += Interact;
            ToolControls.journalPressed += OpenJournal;
        }

        void OnDisable() {
            ToolControls.interactPressed -= Interact;
            ToolControls.journalPressed -= OpenJournal;
            objectiveCompass?.SetDisplayAllowed(false);
            journal.Close();
        }

        void OnDestroy() {
            if (localCharacterAnimator != null)
                localCharacterAnimator.FootstepPaceMultiplier = previousFootstepPaceMultiplier;
            if (manager != null) {
                manager.UnregisterBroadcast<CH_StateBroadcast>(OnState);
                manager.UnregisterBroadcast<CH_ScareBroadcast>(OnScare);
                manager.UnregisterBroadcast<CH_InteractionResult>(OnInteractionResult);
                manager.UnregisterBroadcast<CH_AmbientScareBroadcast>(OnAmbientScare);
            }
            foreach (var view in pointViews.Values) if (view != null) Destroy(view);
        }

        void Update() {
            ConfigureLocalFootsteps();
            EquipFlashlightOnSpawn();
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<CH_StateBroadcast>(OnState);
                manager.RegisterBroadcast<CH_ScareBroadcast>(OnScare);
                manager.RegisterBroadcast<CH_InteractionResult>(OnInteractionResult);
                manager.RegisterBroadcast<CH_AmbientScareBroadcast>(OnAmbientScare);
            }
            if (manager != null && manager.Started && Time.unscaledTime > nextSnapshot && !hasState) {
                nextSnapshot = Time.unscaledTime + 2;
                manager.Broadcast(new CH_StateRequest { version = 1 });
            }
            if (!hasState) return;
            if (Time.unscaledTime > nextFocus) { nextFocus = Time.unscaledTime + focusInterval; UpdateFocus(); }
            radio.SetSuppressed(journal.IsOpen || CaseOver);
        }

        void ConfigureLocalFootsteps() {
            if (localCharacterAnimator != null || LocalPlayer.Character == null
                || !LocalPlayer.Character.TryGetComponent(out localCharacterAnimator)) return;
            previousFootstepPaceMultiplier = localCharacterAnimator.FootstepPaceMultiplier;
            localCharacterAnimator.FootstepPaceMultiplier = footstepPaceMultiplier;
        }

        /// <summary>
        /// Once per flashlight the server hands out (each spawn and each new case): put it in the
        /// investigator's hand unless they already hold a tool. A flashlight they put away stays away.
        /// </summary>
        void EquipFlashlightOnSpawn() {
            GameCharacter character = LocalPlayer.Character;
            if (!equipFlashlightOnSpawn || character == null || character.IsDead) return;
            ToolFlashlightShared flashlight = fearLoop.LocalFlashlight;
            if (flashlight == null || flashlight == offeredFlashlight) return;
            offeredFlashlight = flashlight;
            if (character.ActiveTool.Value == null) character.SwitchTool(flashlight);
        }

        void OnScare(CH_ScareBroadcast scare, Channel channel) {
            if (!hasState || scare.seed != state.seed) return;
            jumpscare.Play(scare.sequence, (CH_ScareKind)scare.kind);
        }

        void OnAmbientScare(CH_AmbientScareBroadcast scare, Channel channel) {
            if (!hasState || scare.seed != state.seed || CaseOver) return;
            ambientScares.Play(scare);
        }

        void OnInteractionResult(CH_InteractionResult result, Channel channel) {
            if (!hasState || result.seed != state.seed) return;
            interactionFeedback = result.accepted ? "" : result.message;
            feedbackUntil = result.accepted ? 0 : Time.unscaledTime + feedbackSeconds;
            if (result.accepted && result.playObjectiveCollect) PlayPickupSting();
            nextFocus = 0;
        }

        /// <summary>The collector's own confirmation: the sting and a small jolt.</summary>
        void PlayPickupSting() {
            if (objectiveCollectAudio != null && objectiveCollectCue != null)
                objectiveCollectAudio.PlayOneShot(objectiveCollectCue);
            CameraShake.Kick(collectShake, collectShakeSeconds);
        }

        void OnState(CH_StateBroadcast next, Channel channel) {
            if (next.points == null || next.journal == null || map == null) return;
            bool fresh = !hasState || state.seed != next.seed;
            if (fresh) ResetCase();
            bool chapterChanged = !fresh && next.phase != state.phase;
            state = next;
            hasState = true;

            UpdateCaseStatus(next);
            if (lastDialogue != next.dialogueRevision) {
                lastDialogue = next.dialogueRevision;
                radio.Show(next.dialogue);
            }
            if (chapterChanged) AnnounceChapter(next.phase);
            ShowEnding(next);
            journal.SetEntries(next.journal, fresh);
            UpdatePointViews(next, fresh);
            RefreshNavigationObjectives();
            fearLoop.SetThreat(FindMonster(next.monsterId), !CaseOver);
        }

        void ResetCase() {
            journal.Close();
            jumpscare.ResetCase();
            radio.Clear();
            foreach (var view in pointViews.Values) if (view != null) Destroy(view);
            pointViews.Clear();
            pointGlows.Clear();
            collectedPoints.Clear();
            focusedGlow = null;
            lastDialogue = -1;
            feedbackUntil = 0;
            endingShown = false;
        }

        void UpdateCaseStatus(CH_StateBroadcast next) {
            chapterLabel.text = ChapterName(next.phase);
            int split = next.objective.IndexOf('|');
            objectiveLabel.text = split >= 0 ? next.objective.Substring(split + 1).Trim() : next.objective;
            bool investigating = next.phase == CH_Phase.Investigation;
            int progress = investigating ? next.evidenceCount : next.relicCount;
            if (!shownStatus || investigating != shownInvestigating || progress != shownProgress) {
                shownStatus = true;
                shownInvestigating = investigating;
                shownProgress = progress;
                progressChip.SetIcon(investigating ? evidenceIcon : offeringIcon);
                progressChip.Text = investigating ? $"Evidence  {progress} / {CH_Case.RequiredEvidence}" : $"Offerings  {progress} / {CH_Case.OfferingCount}";
                if (caseIcon != null) caseIcon.sprite = investigating ? evidenceIcon : offeringIcon;
            }
            if (next.secondsLeft != shownSeconds) {
                shownSeconds = next.secondsLeft;
                timerChip.Text = FormatTime(next.secondsLeft);
            }
        }

        static string ChapterName(CH_Phase phase) => phase switch {
            CH_Phase.Investigation => "Chapter I  ·  The last call",
            CH_Phase.Descent => "Chapter II  ·  What answered",
            CH_Phase.Escape => "Chapter III  ·  Escape",
            _ => "Case closed",
        };

        void AnnounceChapter(CH_Phase phase) {
            if (phase is CH_Phase.Descent or CH_Phase.Escape)
                ScreenFx.ShowText(ChapterName(phase).ToUpperInvariant(), chapterTitleColor, chapterTitleSeconds);
        }

        /// <summary>Spawns, hides and celebrates the world markers for the case's records and offerings.</summary>
        void UpdatePointViews(CH_StateBroadcast next, bool fresh) {
            // Phase changes remove points from snapshots; those old views must
            // disappear too, otherwise completed offerings look interactable.
            foreach (var view in pointViews.Values) if (view != null) view.SetActive(false);
            foreach (var point in next.points) {
                if (!pointViews.TryGetValue(point.id, out var view)) {
                    view = Instantiate(point.id == CH_Case.SourceId ? map.sourceViewPrefab : map.clueViewPrefab, map.transform);
                    view.name = $"{point.title} / {point.area}";
                    view.transform.position = GroundBelow(point.position);
                    if (CH_Case.IsOfferingId(point.id)) view.transform.localScale = Vector3.one * offeringViewScale;
                    pointViews.Add(point.id, view);
                    if (view.TryGetComponent(out CH_EvidenceGlow glow)) pointGlows.Add(point.id, glow);
                }
                view.SetActive(!point.collected && !CaseOver);
                // Everyone sees a record vanish in a burst of light, not just the collector.
                if (point.collected && collectedPoints.Add(point.id) && !fresh && IsRecord(point.id))
                    GameFx.Play(FxKind.Sparks, point.position, collectBurstTint, collectBurstScale);
            }
        }

        /// <summary>The first solid surface under a case point, so a record's table stands on the floor instead of floating at the point's height.</summary>
        Vector3 GroundBelow(Vector3 point) =>
            Physics.Raycast(point, Vector3.down, out RaycastHit hit, viewGroundProbe, visibilityMask, QueryTriggerInteraction.Ignore) ? hit.point : point;

        static bool IsRecord(int id) => id is >= 0 and < CH_Case.RequiredLocations || id >= CH_Case.DiaryFirstId;

        Transform FindMonster(int monsterId) =>
            manager != null && manager.Objects.Spawned.TryGetValue(monsterId, out var monster) ? monster.transform : null;

        /// <summary>The ending card: the epilogue's first line is its title, the rest the story, then the case in numbers.</summary>
        void ShowEnding(CH_StateBroadcast next) {
            bool over = next.phase is CH_Phase.Complete or CH_Phase.Failed;
            endingPopup.SetVisible(over);
            if (!over) { endingShown = false; return; }
            if (next.secondsLeft != endingSecondsShown) {
                endingSecondsShown = next.secondsLeft;
                endingNextCase.Text = $"Next case in {next.secondsLeft}s";
            }
            // The epilogue and the case in numbers are fixed once the case is over.
            if (endingShown) return;
            endingShown = true;
            string ending = next.ending ?? string.Empty;
            int split = ending.IndexOf('\n');
            string title = split >= 0 ? ending.Substring(0, split) : ending;
            string story = split >= 0 ? ending.Substring(split + 1) : string.Empty;
            bool solved = next.phase == CH_Phase.Complete;
            Color accent = solved ? solvedAccent : failedAccent;
            endingTitle.text = string.IsNullOrEmpty(title) ? (solved ? "CASE CLOSED" : "STILL MISSING") : title.ToUpperInvariant();
            endingTitle.color = Color.Lerp(accent, Color.white, 0.3f);
            endingLabel.text = story;
            if (endingSummary != null) endingSummary.text = CaseSummary(next);
            endingDisc.color = endingStripe.color = accent;
            endingIcon.sprite = solved ? solvedIcon : failedIcon;
            endingSolved.Text = $"Cases solved: {next.completedCases}";
        }

        static string CaseSummary(CH_StateBroadcast next) {
            int temperament = Mathf.Clamp((int)next.temperament, 0, TemperamentNames.Length - 1);
            string diary = next.diaryTotal > 0 ? $"   ·   Diary {next.diaryCount}/{next.diaryTotal}" : string.Empty;
            return $"Evidence {next.evidenceCount}/{CH_Case.RequiredEvidence}   ·   Memories {next.memoryCount}/{CH_Case.OptionalMemories}{diary}"
                + $"\nThe Presence: {TemperamentNames[temperament]}   ·   Ritual mistakes {next.ritualMistakes}   ·   Lives lost {next.livesLost}   ·   {FormatTime(next.caseSeconds)}";
        }

        void UpdateFocus() {
            focusedId = -1;
            string prompt = null;
            var camera = Camera.main;
            bool canNavigate = camera != null && LocalPlayer.Character != null && !LocalPlayer.Character.IsDead
                && !journal.IsOpen && !CaseOver;
            objectiveCompass?.SetDisplayAllowed(canNavigate);
            if (!canNavigate) {
                SetFocusedGlow(null);
                interactionPrompt.SetVisible(false);
                return;
            }
            float best = focusAlignment;
            foreach (var point in state.points) {
                if (point.collected || point.id == CH_Case.SourceId && state.relicCount == CH_Case.OfferingCount) continue;
                Vector3 direction = point.position - camera.transform.position;
                float distance = direction.magnitude;
                float alignment = distance < 0.1f ? 1 : Vector3.Dot(camera.transform.forward, direction / distance);
                if (distance <= interactionReach && alignment > best && WorldInteraction.CanReach(camera.transform.position, point.position, interactionReach, visibilityMask)) {
                    best = alignment;
                    focusedId = point.id;
                    prompt = point.id == CH_Case.SourceId
                        ? state.relicCount < CH_Case.OfferingCount ? "<b>The source</b>  ·  recover all three offerings" : "Approach an offering. Follow the order in your journal."
                        : point.title;
                }
            }
            SetFocusedGlow(focusedId >= 0 && pointGlows.TryGetValue(focusedId, out var glow) ? glow : null);
            // The keycap only shows for something E acts on; the source and feedback are just text.
            bool actionable = prompt != null && focusedId != CH_Case.SourceId;
            if (Time.unscaledTime < feedbackUntil && !string.IsNullOrEmpty(interactionFeedback)) {
                prompt = interactionFeedback;
                actionable = false;
            }
            interactionPrompt.SetVisible(prompt != null);
            if (prompt == null) return;
            interactionLabel.text = prompt;
            if (interactionKey.activeSelf != actionable) interactionKey.SetActive(actionable);
        }

        void SetFocusedGlow(CH_EvidenceGlow glow) {
            if (focusedGlow == glow) return;
            if (focusedGlow != null) focusedGlow.SetFocused(false);
            focusedGlow = glow;
            if (focusedGlow != null) focusedGlow.SetFocused(true);
        }

        void RefreshNavigationObjectives() {
            navigationObjectives.Clear();
            if (state.points != null && !CaseOver) {
                foreach (var point in state.points) {
                    if (point.navigationTarget && !point.collected && (point.id != CH_Case.SourceId || state.relicCount < CH_Case.OfferingCount))
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
            if (focusedId >= 0 && focusedId != CH_Case.SourceId) SendInteraction(focusedId, -1);
        }

        void SendInteraction(int point, int option) {
            if (manager == null || !manager.Started || journal.IsOpen) return;
            manager.Broadcast(new CH_InteractRequest { seed = state.seed, targetId = point, option = option });
        }

        void OpenJournal() {
            if (hasState) journal.Open();
        }
    }
}
