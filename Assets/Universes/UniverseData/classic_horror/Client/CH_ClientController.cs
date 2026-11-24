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
    /// Presentation and input for The Drowned Choir; the server decides everything. Binds the night's state to the HUD,
    /// the items in the world, the cutscenes, the voices and the scares, and sends what the investigator presses E on
    /// (and R, to put down what they carry).
    /// </summary>
    public sealed class CH_ClientController : MonoBehaviour {
        [Header("Objective card")]
        public GameObject caseStatusCard;
        public TMP_Text chapterLabel;
        public TMP_Text objectiveLabel;
        public TMP_Text detailLabel;
        public UnityEngine.UI.Image caseIcon;
        public HudChip progressChip, timerChip;
        [Tooltip("What you are carrying and where it goes; hidden with empty hands.")]
        public HudChip carryChip;
        public ObjectiveCompass objectiveCompass;

        [Header("Prompts")]
        public HudPopup interactionPrompt;
        public TMP_Text interactionLabel;
        public GameObject interactionKey;
        [SerializeField, Min(0.5f)] float interactionReach = 3f;
        [SerializeField, Range(0f, 1f)] float focusAlignment = 0.88f;
        [SerializeField, Min(0.02f)] float focusInterval = 0.08f;
        [SerializeField, Min(0.5f)] float feedbackSeconds = 2.5f;
        [SerializeField, Min(0.1f)] float pendingInteractionSeconds = 1.5f;

        [Header("Hiding")]
        [Tooltip("The view through the gaps of a wardrobe door while you hide.")]
        public CanvasGroup hidingOverlay;
        public TMP_Text hidingLabel;

        [Header("Boarding")]
        public HudPopup boardingPopup;
        public TMP_Text boardingLabel;

        [Header("Presenters")]
        public CH_RadioFeed radio;
        public CH_Journal journal;
        public CH_FearLoop fearLoop;
        public CH_AmbientScares ambientScares;
        public CH_Jumpscare jumpscare;
        public CH_VoicePlayer voice;
        public CH_CutsceneDirector cutscenes;
        public CH_ItemPresenter items;
        public CH_Apparitions apparitions;

        [Header("Ending")]
        public HudPopup endingPopup;
        public UnityEngine.UI.Image endingDisc, endingIcon, endingStripe;
        public TMP_Text endingTitle, endingLabel, endingSummary;
        public HudChip endingNextCase, endingSolved;
        [SerializeField] Color solvedAccent = new(0.94f, 0.68f, 0.32f);
        [SerializeField] Color failedAccent = new(0.86f, 0.24f, 0.2f);

        [Header("Icons")]
        public Sprite boatIcon;
        public Sprite choirIcon, bellIcon, solvedIcon, failedIcon;

        [Header("Feedback")]
        public AudioSource objectiveCollectAudio;
        public AudioClip objectiveCollectCue;
        [SerializeField, Range(0f, 1f)] float collectShake = 0.12f;
        [SerializeField] Color actTitleColor = new(0.94f, 0.68f, 0.32f);
        [SerializeField, Min(0.5f)] float actTitleSeconds = 3.5f;
        [Tooltip("Act card size relative to the shared overlay's largest text (1 = a countdown number).")]
        [SerializeField, Range(0.2f, 1f)] float actTitleSize = 0.45f;
        [SerializeField] bool equipFlashlightOnSpawn = true;
        [SerializeField, Min(0.1f)] float footstepPaceMultiplier = 3f;

        enum FocusKind { None, Item, Slot, Hide }

        readonly List<Vector3> navigationObjectives = new();
        // Teammates in a hiding place, with the renderers this switched off (only those come back on), and everyone hidden now.
        readonly Dictionary<int, List<Renderer>> hiddenCharacters = new();
        readonly HashSet<int> hiddenNow = new();
        readonly List<int> releasedCharacters = new();
        bool localHidden;
        CH_Map map;
        ClientManager manager;
        CH_StateBroadcast state;
        bool hasState;
        FocusKind focusKind, promptKind;
        int focusId = -1, focusIndex = -1, promptIndex = -1, pendingId = -1;
        CH_ItemKind? promptCarried;
        string focusPrompt;
        FocusKind pendingKind;
        float nextSnapshot, nextFocus, feedbackUntil, pendingUntil;
        string feedback;
        int visibilityMask;
        CharacterAnimator localCharacterAnimator;
        float previousFootstepPaceMultiplier = 1f;
        CH_ItemGlow focusedGlow;
        ToolFlashlightShared offeredFlashlight;
        string shownObjective, shownDetail;
        int shownProgress = -1, shownTotal = -1, shownCountdown = -1, shownTranscript = -1;
        CH_ItemKind? shownCarry;
        bool endingShown, hasProgress;
        CH_ProgressBroadcast progress;
        CH_Phase cutscenePhase = CH_Phase.Waiting;

        bool NightOver => hasState && state.phase is CH_Phase.Complete or CH_Phase.Failed;
        bool InCutscene => hasState && CH_Story.IsCutscene(state.phase);

        void Awake() {
            map = GetComponentInParent<CH_Map>();
            visibilityMask = ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast");
            endingPopup.HideImmediate();
            interactionPrompt.HideImmediate();
            boardingPopup?.HideImmediate();
            if (hidingOverlay != null) hidingOverlay.alpha = 0f;
            radio.Clear();
            chapterLabel.text = CH_Story.Title;
            objectiveLabel.text = "Crossing the reservoir...";
            if (detailLabel != null) detailLabel.text = string.Empty;
            if (map != null) {
                voice.Bind(map.voices);
                // One puppet priest for the cutscenes and another for the apparitions, so neither interrupts the other.
                cutscenes.Bind(map.cutscenes, SpawnPuppet(map.priestActorPrefab, "Priest (cutscenes)"), SpawnPuppet(map.maraActorPrefab, "Mara (cutscenes)"));
                apparitions.actor = SpawnPuppet(map.priestActorPrefab, "Priest (apparitions)");
            }
        }

        CH_Puppet SpawnPuppet(GameObject prefab, string label) {
            if (prefab == null) { Debug.LogError($"[Drowned Choir] CH_Map has no puppet prefab for {label}; run Build Story Set."); return null; }
            GameObject puppet = Instantiate(prefab, map.transform);
            puppet.name = label;
            return puppet.GetComponent<CH_Puppet>();
        }

        void OnEnable() {
            ToolControls.interactPressed += Interact;
            ToolControls.journalPressed += OpenJournal;
            ToolControls.reloadToolPressed += PutDown;
        }

        void OnDisable() {
            ToolControls.interactPressed -= Interact;
            ToolControls.journalPressed -= OpenJournal;
            ToolControls.reloadToolPressed -= PutDown;
            objectiveCompass?.SetDisplayAllowed(false);
            journal.Close();
        }

        void OnDestroy() {
            if (localCharacterAnimator != null) localCharacterAnimator.FootstepPaceMultiplier = previousFootstepPaceMultiplier;
            if (manager == null) return;
            manager.UnregisterBroadcast<CH_StateBroadcast>(OnState);
            manager.UnregisterBroadcast<CH_ScareBroadcast>(OnScare);
            manager.UnregisterBroadcast<CH_InteractionResult>(OnInteractionResult);
            manager.UnregisterBroadcast<CH_AmbientScareBroadcast>(OnAmbientScare);
            manager.UnregisterBroadcast<CH_VoiceBroadcast>(OnVoice);
            manager.UnregisterBroadcast<CH_ProgressBroadcast>(OnProgress);
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
                manager.RegisterBroadcast<CH_VoiceBroadcast>(OnVoice);
                manager.RegisterBroadcast<CH_ProgressBroadcast>(OnProgress);
                items.Bind(map, manager);
            }
            if (manager != null && manager.Started && Time.unscaledTime > nextSnapshot && !hasState) {
                nextSnapshot = Time.unscaledTime + 2;
                manager.Broadcast(new CH_StateRequest { version = 2 });
            }
            if (!hasState) return;
            if (Time.unscaledTime > nextFocus) { nextFocus = Time.unscaledTime + focusInterval; UpdateFocus(); }
            radio.SetSuppressed(NightOver);
            items.SetLocalHeldVisible(!InCutscene && !localHidden);
            items.SetGlowsAllowed(!InCutscene);
            UpdateHiding();
            if (voice.TranscriptRevision != shownTranscript) {
                shownTranscript = voice.TranscriptRevision;
                journal.SetEntries(TranscriptEntries(), false);
            }
        }

        void ConfigureLocalFootsteps() {
            if (localCharacterAnimator != null || LocalPlayer.Character == null
                || !LocalPlayer.Character.TryGetComponent(out localCharacterAnimator)) return;
            previousFootstepPaceMultiplier = localCharacterAnimator.FootstepPaceMultiplier;
            localCharacterAnimator.FootstepPaceMultiplier = footstepPaceMultiplier;
        }

        /// <summary>Once per flashlight the server hands out: put it in the investigator's hand unless they hold another tool.</summary>
        void EquipFlashlightOnSpawn() {
            GameCharacter character = LocalPlayer.Character;
            if (!equipFlashlightOnSpawn || character == null || character.IsDead) return;
            ToolFlashlightShared flashlight = fearLoop.LocalFlashlight;
            if (flashlight == null || flashlight == offeredFlashlight) return;
            offeredFlashlight = flashlight;
            if (character.ActiveTool.Value == null) character.SwitchTool(flashlight);
        }

        // ---- Broadcasts --------------------------------------------------------------------------------------------------

        void OnState(CH_StateBroadcast next, Channel channel) {
            if (next.items == null || next.slots == null || map == null) return;
            bool fresh = !hasState || state.seed != next.seed;
            if (fresh) ResetNight();
            CH_Phase previous = hasState ? state.phase : CH_Phase.Waiting;
            state = next;
            hasState = true;

            UpdateCutscene(next, previous, fresh);
            UpdateObjective(next);
            if (!fresh && next.phase != previous && CH_Story.ActCard(next.phase) is string card)
                ScreenFx.ShowText(card, actTitleColor, actTitleSeconds, actTitleSize);
            items.Apply(next, LocalCharacterId(), LocalCarried(), fresh);
            UpdateHiddenCharacters(next);
            UpdateBoarding(next);
            ShowEnding(next);
            RefreshNavigation();
            Transform presence = FindObject(next.monsterId);
            voice.SetMonster(presence);
            fearLoop.SetThreat(presence, CH_Story.IsPlayable(next.phase));
        }

        void ResetNight() {
            journal.Close();
            jumpscare.ResetCase();
            apparitions.ResetNight();
            voice.ResetNight();
            items.ResetNight();
            cutscenes.Stop();
            cutscenePhase = CH_Phase.Waiting;
            feedbackUntil = 0;
            endingShown = false;
            pendingId = -1;
            shownObjective = shownDetail = null;
            shownProgress = shownTotal = shownCountdown = -1;
            shownCarry = null;
            foreach (var pair in hiddenCharacters) ShowCharacter(pair.Value);
            hiddenCharacters.Clear();
            localHidden = false;
            promptKind = FocusKind.None;
            promptIndex = -1;
        }

        void OnVoice(CH_VoiceBroadcast line, Channel channel) {
            if (!hasState || line.seed != state.seed) return;
            voice.Play(line);
        }

        void OnProgress(CH_ProgressBroadcast record, Channel channel) {
            progress = record;
            hasProgress = true;
            // The card may already be up when the record arrives (it is sent once the night's ending is saved).
            if (endingShown && endingSummary != null) endingSummary.text = Summary(state);
        }

        void OnScare(CH_ScareBroadcast scare, Channel channel) {
            if (!hasState || scare.seed != state.seed) return;
            if (scare.kind is CH_ScareKind.Apparition or CH_ScareKind.Watcher) apparitions.Play(scare.kind, scare.sequence);
            else jumpscare.Play(scare.sequence, scare.kind);
        }

        void OnAmbientScare(CH_AmbientScareBroadcast scare, Channel channel) {
            if (!hasState || scare.seed != state.seed || NightOver) return;
            ambientScares.Play(scare);
        }

        void OnInteractionResult(CH_InteractionResult result, Channel channel) {
            if (!hasState || result.seed != state.seed) return;
            feedback = result.accepted ? "" : result.message;
            feedbackUntil = result.accepted ? 0 : Time.unscaledTime + feedbackSeconds;
            pendingId = -1;
            if (result.accepted && result.target is CH_Target.Item or CH_Target.Slot) {
                if (objectiveCollectAudio != null && objectiveCollectCue != null) objectiveCollectAudio.PlayOneShot(objectiveCollectCue);
                CameraShake.Kick(collectShake, 0.2f);
            }
            nextFocus = 0;
        }

        // ---- Presentation ------------------------------------------------------------------------------------------------

        void UpdateCutscene(CH_StateBroadcast next, CH_Phase previous, bool fresh) {
            if (CH_Story.IsCutscene(next.phase)) {
                if (cutscenePhase != next.phase) {
                    cutscenePhase = next.phase;
                    journal.Close();
                    cutscenes.Play(next.phase, fresh ? next.phaseSeconds : 0f);
                }
            } else if (cutscenePhase != CH_Phase.Waiting) {
                cutscenePhase = CH_Phase.Waiting;
                cutscenes.Stop();
            }
        }

        void UpdateObjective(CH_StateBroadcast next) {
            if (caseStatusCard != null && caseStatusCard.activeSelf == NightOver) caseStatusCard.SetActive(!NightOver);
            if (next.objective != shownObjective) {
                shownObjective = next.objective;
                chapterLabel.text = CH_Story.ActName(next.phase);
                objectiveLabel.text = next.objective;
                if (caseIcon != null) caseIcon.sprite = Act(next.phase) == 1 ? boatIcon : Act(next.phase) == 2 ? choirIcon : bellIcon;
            }
            if (detailLabel != null && next.detail != shownDetail) {
                shownDetail = next.detail;
                detailLabel.text = next.detail;
            }
            if (next.progress != shownProgress || next.progressTotal != shownTotal) {
                shownProgress = next.progress;
                shownTotal = next.progressTotal;
                progressChip.gameObject.SetActive(next.progressTotal > 0);
                progressChip.SetIcon(Act(next.phase) == 1 ? boatIcon : choirIcon);
                progressChip.Text = $"{next.progress} / {next.progressTotal}";
            }
            int seconds = next.phase == CH_Phase.Boarding ? next.countdown : next.nightSeconds;
            if (seconds != shownCountdown) {
                shownCountdown = seconds;
                timerChip.Text = FormatTime(seconds);
            }
            CH_ItemKind? carried = LocalCarried();
            if (carryChip != null && carried != shownCarry) {
                shownCarry = carried;
                carryChip.gameObject.SetActive(carried.HasValue);
                if (carried.HasValue) carryChip.Text = CarryText(carried.Value);
                // The chip row has room for the carry label or the "F Journal" key hint, not both.
                if (journal.hint != null) journal.hint.gameObject.SetActive(!carried.HasValue);
            }
        }

        static int Act(CH_Phase phase) => phase switch {
            CH_Phase.Choir or CH_Phase.ChapelOpens => 2,
            CH_Phase.Sermon or CH_Phase.Boarding or CH_Phase.Departure => 3,
            _ => 1
        };

        // Short enough to share the objective panel's chip row with the timer and the journal.
        string CarryText(CH_ItemKind kind) => kind switch {
            CH_ItemKind.Fuel => "Fuel · heavy · R drop",
            CH_ItemKind.Bell => "Bell · he hears it · R drop",
            CH_ItemKind.MusicBox => "Music box · R drop",
            _ => CH_Story.ItemName(kind) + " · R drop"
        };

        void UpdateBoarding(CH_StateBroadcast next) {
            if (boardingPopup == null) return;
            bool boarding = next.phase == CH_Phase.Boarding;
            boardingPopup.SetVisible(boarding);
            if (boarding) boardingLabel.text = $"GET TO THE BOAT  ·  {next.countdown}";
        }

        void UpdateHiddenCharacters(CH_StateBroadcast next) {
            // A teammate inside a wardrobe is not standing in front of it.
            hiddenNow.Clear();
            if (next.hides != null) foreach (CH_HideView hide in next.hides) if (hide.occupantId >= 0) hiddenNow.Add(hide.occupantId);
            int local = LocalCharacterId();
            localHidden = local >= 0 && hiddenNow.Contains(local);
            items.SetHiddenCharacters(hiddenNow);
            // Only remembered once their character exists here, so a late joiner keeps trying on each state.
            foreach (int id in hiddenNow)
                if (id != local && !hiddenCharacters.ContainsKey(id) && TryHideCharacter(id, out List<Renderer> switchedOff)) hiddenCharacters.Add(id, switchedOff);
            releasedCharacters.Clear();
            foreach (int id in hiddenCharacters.Keys) if (!hiddenNow.Contains(id)) releasedCharacters.Add(id);
            foreach (int id in releasedCharacters) { ShowCharacter(hiddenCharacters[id]); hiddenCharacters.Remove(id); }
        }

        /// <summary>Switches off a teammate's visible renderers and returns them; false if their character has not spawned here.</summary>
        bool TryHideCharacter(int characterId, out List<Renderer> switchedOff) {
            switchedOff = null;
            Transform character = FindObject(characterId);
            if (character == null) return false;
            switchedOff = new List<Renderer>();
            foreach (Renderer part in character.GetComponentsInChildren<Renderer>(true)) {
                if (!part.enabled) continue;
                part.enabled = false;
                switchedOff.Add(part);
            }
            return true;
        }

        /// <summary>Turns back on exactly what <see cref="TryHideCharacter"/> turned off (parts off for other reasons stay off).</summary>
        static void ShowCharacter(List<Renderer> switchedOff) {
            foreach (Renderer part in switchedOff) if (part != null) part.enabled = true;
        }

        bool LocalHidden => hasState && localHidden;

        void UpdateHiding() {
            if (hidingOverlay == null) return;
            float target = LocalHidden ? 1f : 0f;
            hidingOverlay.alpha = Mathf.MoveTowards(hidingOverlay.alpha, target, Time.deltaTime * 4f);
            if (hidingLabel != null && LocalHidden) hidingLabel.text = fearLoop.Fear > 0.4f ? "Don't move. Don't breathe." : "Hiding  ·  press E to leave";
        }

        /// <summary>The ending card: its title, the epilogue, the night in numbers and when the next night starts.</summary>
        void ShowEnding(CH_StateBroadcast next) {
            bool over = next.phase is CH_Phase.Complete or CH_Phase.Failed;
            endingPopup.SetVisible(over);
            if (!over) { endingShown = false; return; }
            endingNextCase.Text = $"Next night in {next.nextNightSeconds}s";
            endingSolved.Text = $"Escapes tonight: {next.escapes}";
            if (endingShown) return;
            endingShown = true;
            string text = CH_Story.Endings[Mathf.Clamp(next.ending, 0, CH_Story.Endings.Length - 1)];
            int split = text.IndexOf('\n');
            bool escaped = next.phase == CH_Phase.Complete;
            Color accent = escaped ? solvedAccent : failedAccent;
            endingTitle.text = split >= 0 ? text.Substring(0, split) : text;
            endingTitle.color = Color.Lerp(accent, Color.white, 0.3f);
            endingLabel.text = split >= 0 ? text.Substring(split + 1) : string.Empty;
            if (endingSummary != null) endingSummary.text = Summary(next);
            endingDisc.color = endingStripe.color = accent;
            endingIcon.sprite = escaped ? solvedIcon : failedIcon;
        }

        string Summary(CH_StateBroadcast next) {
            int freed = 0, total = 0;
            foreach (CH_SlotView slot in next.slots) if (slot.kind == CH_SlotKind.Grave && slot.filled) freed++;
            foreach (CH_ItemView item in next.items) if (item.kind is CH_ItemKind.Hymnal or CH_ItemKind.Photograph or CH_ItemKind.Candle) total++;
            bool musicBox = System.Array.Exists(next.slots, slot => slot.kind == CH_SlotKind.LilyMarker && slot.filled);
            string secret = musicBox ? "Lily's music box returned" : "Somewhere in Hollowmere, a music box is still waiting...";
            string tonight = $"Singers freed {freed}/{total}   ·   Lives lost {next.livesLost}   ·   {FormatTime(next.nightSeconds)}\n{secret}";
            return hasProgress ? tonight + "\n" + Record(progress) : tonight;
        }

        /// <summary>The investigator's own record across nights: what there is still to find.</summary>
        static string Record(CH_ProgressBroadcast record) {
            int endings = 0;
            foreach (byte ending in RealEndings) if ((record.endingsSeen & (1 << ending)) != 0) endings++;
            int singers = 0;
            for (int i = 0; i < CH_Story.SingerCount; i++) if ((record.singersMet & (1 << i)) != 0) singers++;
            string best = record.bestSeconds > 0 ? FormatTime(record.bestSeconds) : "--:--";
            return $"Endings {endings}/{RealEndings.Length}   ·   Singers met {singers}/{CH_Story.SingerCount}   ·   Best escape {best}   ·   Nights {record.nightsPlayed}";
        }

        static readonly byte[] RealEndings = { CH_Story.EndingTrue, CH_Story.EndingEscaped, CH_Story.EndingDrowned };

        // ---- Focus and interaction ------------------------------------------------------------------------------------------

        void UpdateFocus() {
            focusKind = FocusKind.None;
            focusId = focusIndex = -1;
            string prompt = null;
            Camera camera = Camera.main;
            bool canAct = camera != null && LocalPlayer.Character != null && !LocalPlayer.Character.IsDead
                && !journal.IsOpen && !NightOver && !InCutscene && CH_Story.IsPlayable(state.phase);
            objectiveCompass?.SetDisplayAllowed(canAct && !LocalHidden);
            if (!canAct) {
                SetFocusedGlow(null);
                interactionPrompt.SetVisible(false);
                return;
            }
            if (pendingId >= 0 && Time.unscaledTime > pendingUntil) pendingId = -1;
            if (LocalHidden) {
                focusKind = FocusKind.Hide;
                focusId = -1;
                interactionPrompt.SetVisible(false);
                return;
            }
            Vector3 eye = camera.transform.position, forward = camera.transform.forward;
            float best = focusAlignment;
            CH_ItemKind? carried = LocalCarried();
            for (int i = 0; i < state.items.Length; i++) {
                CH_ItemView item = state.items[i];
                if (item.state != CH_ItemState.Resting || (pendingKind == FocusKind.Item && item.id == pendingId)) continue;
                Consider(FocusKind.Item, i, item.position + Vector3.up * 0.3f);
            }
            if (carried.HasValue) {
                for (int i = 0; i < state.slots.Length; i++)
                    if (state.slots[i].open) Consider(FocusKind.Slot, i, CH_Night.SlotReachPoint(state.slots[i].kind, state.slots[i].position));
            }
            if (map.hidingSpots != null) {
                for (int i = 0; i < map.hidingSpots.Length; i++) {
                    CH_HidingSpot spot = map.hidingSpots[i];
                    if (spot == null || (state.hides != null && i < state.hides.Length && state.hides[i].occupantId >= 0)) continue;
                    Consider(FocusKind.Hide, i, spot.Handle);
                }
            }
            // Only the winner's prompt is built, and only when the focus changed.
            if (focusKind != FocusKind.None) {
                if (focusKind != promptKind || focusIndex != promptIndex || carried != promptCarried) {
                    promptKind = focusKind;
                    promptIndex = focusIndex;
                    promptCarried = carried;
                    focusPrompt = PromptFor(focusKind, focusIndex, carried);
                }
                prompt = focusPrompt;
                focusId = focusKind switch { FocusKind.Item => state.items[focusIndex].id, FocusKind.Slot => state.slots[focusIndex].id, _ => focusIndex };
            }
            SetFocusedGlow(focusKind == FocusKind.Item ? items.GlowOf(focusId) : null);
            bool actionable = prompt != null;
            if (Time.unscaledTime < feedbackUntil && !string.IsNullOrEmpty(feedback)) { prompt = feedback; actionable = false; }
            interactionPrompt.SetVisible(prompt != null);
            if (prompt == null) return;
            if (interactionLabel.text != prompt) interactionLabel.text = prompt;
            if (interactionKey.activeSelf != actionable) interactionKey.SetActive(actionable);

            void Consider(FocusKind kind, int index, Vector3 point) {
                Vector3 direction = point - eye;
                float distance = direction.magnitude;
                if (distance > interactionReach) return;
                float alignment = distance < 0.1f ? 1f : Vector3.Dot(forward, direction / distance);
                // Close things are easier to point at.
                float needed = Mathf.Lerp(0.55f, best, Mathf.InverseLerp(0.8f, interactionReach, distance));
                if (alignment <= needed || alignment <= best - 0.25f || !WorldInteraction.CanReach(eye, point, interactionReach, visibilityMask)) return;
                best = Mathf.Max(best, alignment);
                focusKind = kind;
                focusIndex = index;
            }
        }

        string PromptFor(FocusKind kind, int index, CH_ItemKind? carried) {
            switch (kind) {
                case FocusKind.Item:
                    CH_ItemView item = state.items[index];
                    string name = item.singer >= 0 ? item.title : "the " + item.title.ToLowerInvariant();
                    return carried.HasValue ? $"Swap for {name}" : $"Take {name}";
                case FocusKind.Slot:
                    return SlotPrompt(state.slots[index], carried ?? CH_ItemKind.Fuel);
                default:
                    return $"Hide in the {map.hidingSpots[index].label}";
            }
        }

        static string SlotPrompt(CH_SlotView slot, CH_ItemKind carried) => slot.kind switch {
            CH_SlotKind.Grave => $"Lay it on {slot.title}",
            CH_SlotKind.LilyMarker => "Leave the music box for Lily",
            CH_SlotKind.BoatBell => "Throw the bell into the boat",
            _ => $"Fit the {CH_Story.ItemName(carried).ToLowerInvariant()} to the boat"
        };

        void SetFocusedGlow(CH_ItemGlow glow) {
            if (focusedGlow == glow) return;
            if (focusedGlow != null) focusedGlow.SetFocused(false);
            focusedGlow = glow;
            if (focusedGlow != null) focusedGlow.SetFocused(true);
        }

        void Interact() {
            if (!hasState) return;
            UpdateFocus();
            if (focusKind == FocusKind.Hide && LocalHidden) { Send(CH_Target.Hide, -1); return; }
            if (focusKind == FocusKind.None || focusId < 0) return;
            CH_Target target = focusKind switch { FocusKind.Item => CH_Target.Item, FocusKind.Slot => CH_Target.Slot, _ => CH_Target.Hide };
            if (!Send(target, focusId)) return;
            pendingKind = focusKind;
            pendingId = focusId;
            pendingUntil = Time.unscaledTime + pendingInteractionSeconds;
        }

        /// <summary>R: set down what you carry, at your feet.</summary>
        void PutDown() {
            if (!hasState || LocalHidden || !CH_Story.IsPlayable(state.phase) || LocalCarried() == null) return;
            if (LocalPlayer.Character == null || LocalPlayer.Character.IsDead) return;
            Send(CH_Target.Drop, -1);
        }

        bool Send(CH_Target target, int id) {
            if (manager == null || !manager.Started || journal.IsOpen || InCutscene) return false;
            manager.Broadcast(new CH_InteractRequest { seed = state.seed, target = target, id = id });
            return true;
        }

        void OpenJournal() {
            if (hasState && !InCutscene) journal.Open();
        }

        string[] TranscriptEntries() {
            var lines = voice.Transcript;
            if (lines.Count == 0) return new[] { $"{CH_Story.Title.ToUpperInvariant()} / Everything said tonight is written down here." };
            var entries = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++) entries[i] = lines[i];
            return entries;
        }

        // ---- Navigation --------------------------------------------------------------------------------------------------

        /// <summary>The compass points at the act's items, or, with something in hand, at where it goes.</summary>
        void RefreshNavigation() {
            navigationObjectives.Clear();
            if (hasState && !NightOver && CH_Story.IsPlayable(state.phase)) {
                CH_ItemKind? carried = LocalCarried();
                CH_ItemView? held = LocalCarriedView();
                if (carried.HasValue) {
                    foreach (CH_SlotView slot in state.slots)
                        if (slot.open && Fits(held.Value, slot)) navigationObjectives.Add(slot.position);
                } else if (state.phase == CH_Phase.Boarding) {
                    navigationObjectives.Add(map.boardingPoint.position);
                } else {
                    foreach (CH_ItemView item in state.items)
                        if (item.objective && item.state == CH_ItemState.Resting) navigationObjectives.Add(item.position);
                }
            }
            objectiveCompass?.SetObjectives(navigationObjectives);
        }

        /// <summary>A relic goes on its own singer's grave; everything else goes where its kind goes (as CH_Night.Fits).</summary>
        static bool Fits(CH_ItemView item, CH_SlotView slot) =>
            slot.kind == CH_SlotKind.Grave ? item.singer >= 0 && item.singer == slot.singer : slot.accepts == item.kind;

        // ---- Lookups -----------------------------------------------------------------------------------------------------

        int LocalCharacterId() => LocalPlayer.Character != null ? LocalPlayer.Character.ObjectId : -1;

        CH_ItemView? LocalCarriedView() {
            if (!hasState || state.items == null) return null;
            int local = LocalCharacterId();
            if (local < 0) return null;
            foreach (CH_ItemView item in state.items) if (item.state == CH_ItemState.Carried && item.carrierId == local) return item;
            return null;
        }

        CH_ItemKind? LocalCarried() => LocalCarriedView()?.kind;

        Transform FindObject(int objectId) =>
            manager != null && objectId >= 0 && manager.Objects.Spawned.TryGetValue(objectId, out var found) && found != null ? found.transform : null;

        static string FormatTime(int seconds) {
            seconds = Mathf.Max(0, seconds);
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
}
