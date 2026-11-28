using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.GameFeel;
using RyanAssets.Input;
using RyanAssets.Retention;
using RyanAssets.Retention.Client;
using RyanAssets.Rounds;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// Draws the defence from server snapshots (enemies, shields, heals, towers, shots, chains), feeds
    /// the <see cref="TD_Hud"/> and sends mode-vote, build, upgrade, sell, ready, fast-forward and
    /// ability requests. Keys: 1-3 vote for a mode before wave 1; then 1-6 pick a tower, E builds or
    /// upgrades the pad in front of you, X (twice) sells the tower there, T cycles its target, R votes to start the next
    /// wave early, G votes for fast forward, F calls an Airstrike and V fires an EMP. All feedback
    /// (blasts, popups, shake, sounds, base alarm) is a reaction to server broadcasts. The key names
    /// the HUD shows come from here. Unlocks come from the shared retention HUD's level.
    /// </summary>
    [RequireComponent(typeof(TD_Hud))]
    public sealed class TD_ClientController : MonoBehaviour {
        [Header("Feel")]
        public TD_FxTuning tuning = new();
        [Tooltip("Countdown beeps during the last seconds of a build phase.")]
        [Min(0)] public int countdownBeeps = 3;

        [Header("Keys")]
        public Key sellKey = Key.X;
        [Tooltip("Cycles what the focused tower shoots at (First, Last, Strongest, Closest).")]
        public Key targetKey = Key.T;
        [Tooltip("Puts the tower in hand away (a click on its card, Backspace and Escape do too).")]
        public Key dropKey = Key.Q;
        public Key fastForwardKey = Key.G;
        [Tooltip("Fires the EMP (the Airstrike is on the Tool map's Journal action, F).")]
        public Key empKey = Key.V;
        [Tooltip("On a level 3 tower: specialises it into its second elite form (the action key picks the first).")]
        public Key eliteKey = Key.Z;
        [Tooltip("Degrees per second an elite tower's glowing cores orbit it.")]
        public float eliteSpinSpeed = 80f;
        [Tooltip("Selling needs a second press of the sell key within this many seconds.")]
        [Min(0.5f)] public float sellConfirmSeconds = 2.5f;

        [Header("Placement preview")]
        [Tooltip("Ring and ghost colour on a pad where the selected tower cannot be built.")]
        public Color invalidColor = new(1f, 0.28f, 0.25f);
        [Range(0f, 1f)] public float ghostAlpha = 0.45f;
        [Tooltip("How far the ghost's opacity breathes around ghostAlpha.")]
        [Range(0f, 0.5f)] public float ghostPulse = 0.12f;
        [Min(0f)] public float ghostPulseSpeed = 3f;
        [Range(0f, 1f)] public float rangeRingAlpha = 0.85f;
        [Range(0f, 1f)] public float nextRangeRingAlpha = 0.35f;
        [Tooltip("Height of a range ring above the pad or tower it surrounds.")]
        public float rangeRingHeight = 0.15f;

        [Header("Towers and pads")]
        [Tooltip("How long the head of a tower keeps facing its last target before idling.")]
        [Min(0f)] public float idleSweepDelay = 2f;
        [Tooltip("Resting height of the floating build markers above their pads.")]
        public float markerHeight = 2.6f;
        [Tooltip("Extra height of the marker above a pad while its build ghost shows.")]
        public float markerGhostLift = 3.2f;

        [Header("Retention")]
        [Tooltip("The nested shared retention HUD: its level decides which towers and abilities show as locked.")]
        public RetentionHud retentionHud;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        TD_Hud hud;
        TD_Map map;
        TD_WorldFx fx;
        HitFlash baseFlash;
        ClientManager manager;
        TD_StateBroadcast state;
        bool hasState, ready, fastVote, toldRecord;
        int selected, focusedSlot = -1, sellSlot = -1, ghostKind = -1, confettiLeft;
        float nextConfetti;
        float nextRequest, snapshotAt, sellConfirmUntil, lastAlarm = float.NegativeInfinity;
        readonly Dictionary<ushort, TD_EnemyView> enemyViews = new();
        readonly Dictionary<ushort, TD_EnemyKind> enemyKinds = new();
        readonly Dictionary<TD_EnemyKind, Stack<TD_EnemyView>> enemyPool = new();
        readonly HashSet<ushort> seen = new();
        readonly HashSet<ushort> killedIds = new();
        readonly HashSet<ushort> leakedIds = new();
        readonly List<ushort> gone = new();
        readonly TD_TowerView[] towerViews = new TD_TowerView[TD_Rules.Slots.Length];
        readonly TD_TowerSnap[] towerSnaps = new TD_TowerSnap[TD_Rules.Slots.Length];
        readonly float[] towerLastShot = new float[TD_Rules.Slots.Length];
        readonly bool[] pendingPop = new bool[TD_Rules.Slots.Length];
        readonly bool[] present = new bool[TD_Rules.Slots.Length];
        readonly TD_SlotView[] slotViews = new TD_SlotView[TD_Rules.Slots.Length];
        readonly List<int> shownGrid = new();
        readonly Stack<TD_SlotView> padPool = new();
        float padTop;
        bool placing;
        GameObject[] ghosts;
        float[] markerLift;
        Renderer[][] ghostRenderers;
        MaterialPropertyBlock block;
        int lastBase = TD_Rules.BaseHealth;
        TD_Phase lastPhase;
        int bannerWave, lastSecondsLeft, myVote = -1;
        TD_ProfileBroadcast profile;
        TD_AbilityStateBroadcast abilities;
        bool hasProfile, hasAbilities, toldEndlessRecord, toldBlocked;
        float introUntil;
        readonly List<Vector3> empTargets = new();

        /// <summary>The player's Tower Defense level (1 until the retention HUD has its first snapshot).</summary>
        int Level => retentionHud != null ? retentionHud.Level : 1;
        TD_TowerPerks Perks => TD_Retention.PerksFor(Level);
        /// <summary>The day's modifier of the game being drawn (ranges, speeds and rewards as the server plays them).</summary>
        TD_Modifier Modifier => hasState ? state.modifier : TD_Modifier.None;

        void Awake() {
            hud = GetComponent<TD_Hud>();
            block = new MaterialPropertyBlock();
            map = GetComponentInParent<TD_Map>();
            fx = new TD_WorldFx(map.effects, tuning, map.transform);
            if (map.baseModel != null) baseFlash = map.baseModel.GetComponentInChildren<HitFlash>();
            ghosts = new GameObject[TD_Rules.Towers.Length];
            ghostRenderers = new Renderer[TD_Rules.Towers.Length][];
        }

        void Start() {
            // World popups stay off the status bar, pill row, toast and banner, and the kit's toast column.
            fx.guard = new TD_ScreenGuard(hud.keepClear, FindChild(retentionHud != null ? retentionHud.transform : null, "Toasts") as RectTransform, tuning);
            hud.SetDropKey(dropKey.ToString());
            hud.SetEliteKey(eliteKey.ToString());
            hud.SetKeys(BindingLabel("ReloadTool", "R"), fastForwardKey.ToString(), BindingLabel("Interact", "E"));
            hud.SetAbilityKeys(BindingLabel("Journal", "F"), empKey.ToString());
            padTop = map.slotPrefab.GetComponent<TD_SlotView>().topHeight;
            ShowShop();
        }

        void OnEnable() {
            ToolControls.interactPressed += Interact;
            CharacterControls.LockKeyboardZoom(); // E is this game's action key, not camera zoom
            ToolControls.toolBarHotkeyPressed += Select;
            ToolControls.reloadToolPressed += ToggleReady;
            ToolControls.journalPressed += CallAirstrike;
            hud.CardClicked += PickCard;
            if (retentionHud != null) retentionHud.LevelChanged += OnLevelChanged;
        }

        void OnDisable() {
            ToolControls.interactPressed -= Interact;
            CharacterControls.UnlockKeyboardZoom();
            ToolControls.toolBarHotkeyPressed -= Select;
            ToolControls.reloadToolPressed -= ToggleReady;
            ToolControls.journalPressed -= CallAirstrike;
            hud.CardClicked -= PickCard;
            if (retentionHud != null) retentionHud.LevelChanged -= OnLevelChanged;
        }

        /// <summary>A level-up may have unlocked a tower or an ability: refresh the shop and the focus card.</summary>
        void OnLevelChanged(int level) {
            ShowShop();
            if (focusedSlot >= 0) Highlight(focusedSlot, true);
            RefreshFocusCard();
            if (hasAbilities) hud.ShowAbilities(abilities, hasState ? state.phase : TD_Phase.Waiting, level);
        }

        void ShowShop() => hud.ShowShop(placing ? selected : -1, hasState ? state.cash : 0, hasState, Level);

        static Transform FindChild(Transform root, string name) {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <summary>The keyboard key bound to a tool action (ready = ReloadTool, build = Interact), for labels.</summary>
        static string BindingLabel(string actionName, string fallback) {
            foreach (var input in PlayerInput.all) {
                var action = input.actions != null ? input.actions.FindAction(actionName) : null;
                if (action == null) continue;
                for (int i = 0; i < action.bindings.Count; i++)
                    if (action.bindings[i].effectivePath.StartsWith("<Keyboard>")) return action.GetBindingDisplayString(i);
            }
            return fallback;
        }

        void OnDestroy() {
            ScreenFx.SetDanger(0f); // not ClearDanger, which can freeze a running flash on screen (see OnState)
            if (manager == null) return;
            manager.UnregisterBroadcast<TD_StateBroadcast>(OnState);
            manager.UnregisterBroadcast<TD_SnapshotBroadcast>(OnSnapshot);
            manager.UnregisterBroadcast<TD_ResultBroadcast>(OnResult);
            manager.UnregisterBroadcast<TD_TowerEventBroadcast>(OnTowerEvent);
            manager.UnregisterBroadcast<TD_ProfileBroadcast>(OnProfile);
            manager.UnregisterBroadcast<TD_AbilityBroadcast>(OnAbility);
            manager.UnregisterBroadcast<TD_AbilityStateBroadcast>(OnAbilityState);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<TD_StateBroadcast>(OnState);
                manager.RegisterBroadcast<TD_SnapshotBroadcast>(OnSnapshot);
                manager.RegisterBroadcast<TD_ResultBroadcast>(OnResult);
                manager.RegisterBroadcast<TD_TowerEventBroadcast>(OnTowerEvent);
                manager.RegisterBroadcast<TD_ProfileBroadcast>(OnProfile);
                manager.RegisterBroadcast<TD_AbilityBroadcast>(OnAbility);
                manager.RegisterBroadcast<TD_AbilityStateBroadcast>(OnAbilityState);
            }
            // Ask until both the game state and this player's profile are here (a profile the server sent
            // before these handlers were registered is lost; the request makes it send it again).
            if (manager != null && manager.Started && (!hasState || !hasProfile) && Time.unscaledTime > nextRequest) {
                nextRequest = Time.unscaledTime + 2f;
                manager.Broadcast(new TD_StateRequest());
            }
            if (introUntil > 0f && Time.unscaledTime > introUntil) {
                introUntil = 0f;
                hud.HideIntro();
            }
            ReadKeys();
            MoveEnemies(Time.deltaTime);
            TurnTowers(Time.deltaTime);
            UpdateBadges();
            ScreenConfetti();
            fx.Update();
            UpdateFocus();
            UpdateGhost();
        }

        // ---------------------------------------------------------------- input
        void Select(int key) {
            if (hasState && state.phase == TD_Phase.Vote) {
                Vote(key - 1);
                return;
            }
            if (key < 1 || key > TD_Rules.Towers.Length) return;
            Arm(key - 1);
        }

        /// <summary>A click on a shop card: arms that tower for placing, or drops it when it is already in hand.</summary>
        void PickCard(int index) {
            if (hasState && state.phase == TD_Phase.Vote) return;
            if (placing && selected == index) Drop();
            else Arm(index);
        }

        /// <summary>Takes a tower in hand: its ghost follows the aim and a click (or the build key) places it.</summary>
        void Arm(int index) {
            if (index < 0 || index >= TD_Rules.Towers.Length) return;
            var kind = (TD_TowerKind)index;
            if (!TD_Retention.CanBuild(Level, kind)) {
                hud.Toast($"The {TD_Rules.Tower(kind).name} unlocks at level {TD_Retention.LevelOf(TD_Rules.Tower(kind).unlockId)}.", TD_Hud.ToastKind.Info, tuning.shortToastSeconds);
                return;
            }
            selected = index;
            placing = true;
            GameSfx.Play(SfxCue.UiClick);
            RefreshPlacement();
        }

        /// <summary>Puts the tower in hand away without building.</summary>
        void Drop() {
            if (!placing) return;
            placing = false;
            GameSfx.Play(SfxCue.UiClick);
            RefreshPlacement();
        }

        void RefreshPlacement() {
            ShowShop();
            UpdateGrid();
            if (focusedSlot >= 0) Highlight(focusedSlot, true); // re-preview the range of the new pick
            RefreshFocusCard();
        }

        /// <summary>Votes for a mode before wave 1 (keys 1-3). Locked modes say what opens them instead.</summary>
        void Vote(int index) {
            if (index < 0 || index >= TD_Modes.Count || manager == null || !manager.Started) return;
            var mode = (TD_Difficulty)index;
            if (!hud.ModeOpen(mode)) {
                hud.Toast($"{TD_Modes.Get(mode).name} is locked: {TD_Modes.Get(mode).lockedHint}.", TD_Hud.ToastKind.Bad, tuning.toastSeconds);
                GameSfx.Play(SfxCue.Error);
                return;
            }
            myVote = index;
            hud.SetMyVote(index);
            manager.Broadcast(new TD_ModeVoteRequest { mode = mode });
            GameSfx.Play(SfxCue.UiClick);
            hud.ShowState(state, ready, fastVote);
        }

        void CallAirstrike() => UseAbility(TD_Ability.Airstrike);

        /// <summary>Asks the server to use an ability; a locked one says its level at once (the server checks again and explains any refusal).</summary>
        void UseAbility(TD_Ability ability) {
            if (manager == null || !manager.Started || !hasState) return;
            var stats = TD_Abilities.Get(ability);
            if (!TD_Retention.Owns(Level, stats.unlockId)) {
                hud.Toast($"{stats.name} unlocks at level {TD_Retention.LevelOf(stats.unlockId)}.", TD_Hud.ToastKind.Info, tuning.toastSeconds);
                return;
            }
            manager.Broadcast(new TD_AbilityRequest { ability = ability });
        }

        void ToggleReady() {
            if (manager == null || !hasState || state.phase != TD_Phase.Build) return;
            ready = !ready;
            manager.Broadcast(new TD_ReadyRequest { ready = ready });
            hud.ShowState(state, ready, fastVote);
            hud.Toast(ready ? "Ready! Waiting for the others..." : "Not ready", TD_Hud.ToastKind.Good, tuning.shortToastSeconds);
        }

        /// <summary>The action key (E): build, upgrade, or on a level 3 tower take its first elite form.</summary>
        void Interact() => Interact(false);

        /// <param name="click">A mouse click on the world: never buys an elite form (that is a choice, made on the card's keys).</param>
        void Interact(bool click) {
            UpdateFocus();
            if (focusedSlot < 0 || manager == null || !manager.Started) return;
            if (towerViews[focusedSlot] != null && TD_Elites.CanSpecialise(towerSnaps[focusedSlot].level, towerSnaps[focusedSlot].path)) {
                if (click) {
                    var kind = towerSnaps[focusedSlot].kind;
                    hud.Toast($"Pick an elite form: {hud.InteractKey} {TD_Elites.Get(kind, TD_TowerPath.First).name}  or  {eliteKey} {TD_Elites.Get(kind, TD_TowerPath.Second).name}",
                        TD_Hud.ToastKind.Info, tuning.shortToastSeconds);
                } else Specialise(TD_TowerPath.First);
                return;
            }
            // Fully upgraded: nothing to buy (a click would only fetch a refusal).
            if (towerViews[focusedSlot] != null && TD_Elites.IsElite(towerSnaps[focusedSlot].path)) return;
            if (towerViews[focusedSlot] == null && !placing) return; // an empty cell needs a tower in hand
            if (towerViews[focusedSlot] == null && !InPlaceReach(focusedSlot)) {
                hud.Toast("Too far: walk closer to build there.", TD_Hud.ToastKind.Bad, tuning.shortToastSeconds);
                GameSfx.Play(SfxCue.Error);
                return;
            }
            manager.Broadcast(new TD_BuildRequest { slot = (byte)focusedSlot, kind = (TD_TowerKind)selected });
        }

        void ReadKeys() {
            var keyboard = Keyboard.current;
            if (keyboard == null || !Application.isFocused || TextFieldFocused()) return;
            if (placing && (keyboard[dropKey].wasPressedThisFrame || keyboard[Key.Backspace].wasPressedThisFrame || keyboard[Key.Escape].wasPressedThisFrame)) Drop();
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !PointerOverUi()) {
                if (placing || focusedSlot >= 0 && towerViews[focusedSlot] != null) Interact(true);
            }
            if (keyboard[sellKey].wasPressedThisFrame) Sell();
            if (keyboard[targetKey].wasPressedThisFrame) CycleTarget();
            if (keyboard[fastForwardKey].wasPressedThisFrame) ToggleFastForward();
            if (keyboard[empKey].wasPressedThisFrame) UseAbility(TD_Ability.Emp);
            if (keyboard[eliteKey].wasPressedThisFrame) Specialise(TD_TowerPath.Second);
        }

        /// <summary>Asks the server to turn the focused level 3 tower into elite form <paramref name="path"/>; its reply toasts the result.</summary>
        void Specialise(TD_TowerPath path) {
            UpdateFocus();
            if (focusedSlot < 0 || towerViews[focusedSlot] == null || manager == null || !manager.Started) return;
            var tower = towerSnaps[focusedSlot];
            if (!TD_Elites.CanSpecialise(tower.level, tower.path)) return;
            if (!TD_Retention.Owns(Level, TD_Retention.EliteUnlock)) {
                hud.Toast($"Reach level {TD_Retention.EliteLevel} to specialise towers.", TD_Hud.ToastKind.Bad, tuning.shortToastSeconds);
                GameSfx.Play(SfxCue.Error);
                return;
            }
            manager.Broadcast(new TD_BuildRequest { slot = (byte)focusedSlot, kind = tower.kind, path = path });
        }

        static bool PointerOverUi() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        static bool TextFieldFocused() {
            var selectedObject = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selectedObject != null && selectedObject.GetComponent<TMP_InputField>() != null;
        }

        /// <summary>First press asks for confirmation, a second press on the same tower sells it.</summary>
        void Sell() {
            UpdateFocus();
            if (focusedSlot < 0 || towerViews[focusedSlot] == null || manager == null || !manager.Started) return;
            var tower = towerSnaps[focusedSlot];
            if (!CanSell(tower)) {
                // The server refuses it too; say so at once instead of asking for a second press.
                hud.Toast($"Only {tower.owner} can sell this {TD_Rules.Tower(tower.kind).name}.", TD_Hud.ToastKind.Bad, tuning.shortToastSeconds);
                return;
            }
            if (sellSlot == focusedSlot && Time.unscaledTime < sellConfirmUntil) {
                manager.Broadcast(new TD_SellRequest { slot = (byte)focusedSlot });
                sellSlot = -1;
                return;
            }
            sellSlot = focusedSlot;
            sellConfirmUntil = Time.unscaledTime + sellConfirmSeconds;
            hud.Toast($"Press {sellKey} again to sell the {TD_Rules.Tower(tower.kind).name} for ${TD_InfoPanel.RefundOf(tower)}", TD_Hud.ToastKind.Money, sellConfirmSeconds);
        }

        /// <summary>Asks the server to aim the focused tower at the next <see cref="TD_TargetMode"/>; its reply toasts the result.</summary>
        void CycleTarget() {
            UpdateFocus();
            if (focusedSlot < 0 || towerViews[focusedSlot] == null || manager == null || !manager.Started) return;
            var tower = towerSnaps[focusedSlot];
            if (!CanSell(tower)) {
                hud.Toast($"Only {tower.owner} can retarget this {TD_Rules.Tower(tower.kind).name}.", TD_Hud.ToastKind.Bad, tuning.shortToastSeconds);
                GameSfx.Play(SfxCue.Error);
                return;
            }
            manager.Broadcast(new TD_TargetRequest { slot = (byte)focusedSlot, mode = TD_Targeting.Next(tower.target) });
            GameSfx.Play(SfxCue.UiClick);
        }

        /// <summary>Whether this client may sell (or retarget) <paramref name="tower"/>: unclaimed, or built by this player (same rule as the server).</summary>
        bool CanSell(in TD_TowerSnap tower) => tower.ownerId < 0 || (manager != null && manager.Connection != null && tower.ownerId == manager.Connection.ClientId);

        void ToggleFastForward() {
            if (manager == null || !manager.Started || !hasState || !state.fastForwardAllowed) return;
            fastVote = !fastVote;
            manager.Broadcast(new TD_SpeedRequest { fast = fastVote });
            hud.ShowState(state, ready, fastVote);
            hud.Toast(fastVote ? "Voted for fast forward" : "Voted for normal speed", TD_Hud.ToastKind.Info, tuning.shortToastSeconds);
        }

        // ---------------------------------------------------------------- focus + placement preview
        /// <summary>Metres from the camera's aim (the cursor when it is free, else the screen centre) to the ground.</summary>
        const float AimLimit = 400f;

        void UpdateFocus() {
            var character = LocalPlayer.Character;
            var cam = Camera.main;
            int previous = focusedSlot;
            focusedSlot = -1;
            if (character != null && !character.IsDead && cam != null && hasState) focusedSlot = AimedCell(cam);
            if (focusedSlot == previous) return;
            if (previous >= 0) Highlight(previous, false);
            if (focusedSlot >= 0) Highlight(focusedSlot, true);
            UpdateGrid();
            RefreshFocusCard();
        }

        /// <summary>The cell under the aim: a tower the ray hits, else the grid cell on the ground there.</summary>
        int AimedCell(Camera cam) {
            Vector2 screen = Cursor.lockState == CursorLockMode.Locked || Mouse.current == null
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : Mouse.current.position.ReadValue();
            var ray = cam.ScreenPointToRay(screen);
            if (Physics.Raycast(ray, out var hit, AimLimit, ~0, QueryTriggerInteraction.Ignore)) {
                var tower = hit.collider.GetComponentInParent<TD_TowerView>();
                if (tower != null) {
                    int slot = System.Array.IndexOf(towerViews, tower);
                    if (slot >= 0) return slot;
                }
            }
            var ground = new Plane(Vector3.up, Vector3.zero);
            return ground.Raycast(ray, out float t) && t < AimLimit ? TD_Rules.CellAt(ray.GetPoint(t)) : -1;
        }

        bool InPlaceReach(int slot) {
            var character = LocalPlayer.Character;
            return character != null && FocusPicker.InReach(character.transform.position, TD_Rules.Slots[slot], TD_Rules.PlaceReach, 2.5f);
        }

        // ---------------------------------------------------------------- grid + pads
        /// <summary>The pad of a cell (made on first use): a helipad under a tower or the aimed cell, a faint ring on the other nearby cells.</summary>
        TD_SlotView Pad(int slot) {
            if (slotViews[slot] != null) return slotViews[slot];
            TD_SlotView view;
            if (padPool.Count > 0) view = padPool.Pop();
            else {
                view = Instantiate(map.slotPrefab, map.transform).GetComponent<TD_SlotView>();
                if (view.marker != null) view.marker.gameObject.SetActive(false);
            }
            view.transform.position = TD_Rules.Slots[slot];
            view.gameObject.SetActive(true);
            view.built = false;
            view.focused = false;
            slotViews[slot] = view;
            return view;
        }

        void ReleasePad(int slot) {
            var view = slotViews[slot];
            if (view == null) return;
            view.gameObject.SetActive(false);
            if (view.rangeRing != null) view.rangeRing.gameObject.SetActive(false);
            padPool.Push(view);
            slotViews[slot] = null;
        }

        /// <summary>While a tower is in hand, rings mark the cells around the aim; towers always keep their pad.</summary>
        void UpdateGrid() {
            shownGrid.Clear();
            if (placing && focusedSlot >= 0) {
                Vector3 centre = TD_Rules.Slots[focusedSlot];
                float radius = TD_Rules.CellSize * tuning.gridCells;
                for (int i = 0; i < TD_Rules.Slots.Length; i++)
                    if (!present[i] && (TD_Rules.Slots[i] - centre).sqrMagnitude <= radius * radius) shownGrid.Add(i);
            }
            for (int i = 0; i < slotViews.Length; i++) {
                bool inGrid = shownGrid.Contains(i);
                if (!present[i] && !inGrid) {
                    ReleasePad(i);
                    continue;
                }
                Pad(i).SetStyle(present[i] || i == focusedSlot ? TD_SlotView.Style.Pad : TD_SlotView.Style.Ring);
            }
        }

        /// <summary>Whether the selected tower can go on an empty pad right now (unlocked, money and phase).</summary>
        bool CanBuildSelected => hasState && state.phase is TD_Phase.Build or TD_Phase.Wave && state.cash >= TD_Rules.Towers[selected].cost
            && TD_Retention.CanBuild(Level, (TD_TowerKind)selected) && (focusedSlot < 0 || InPlaceReach(focusedSlot));

        bool CanUpgrade(int slot) => hasState && state.phase is TD_Phase.Build or TD_Phase.Wave
            && towerSnaps[slot].level < TD_Rules.MaxLevel && state.cash >= TD_Rules.UpgradeCost(towerSnaps[slot].kind, towerSnaps[slot].level, Perks.upgradeDiscount);

        /// <summary>Whether the focused level 3 tower can take an elite form now (unlocked, phase, and cash for the cheaper form).</summary>
        bool CanSpecialise(int slot) {
            var t = towerSnaps[slot];
            if (!hasState || state.phase is not (TD_Phase.Build or TD_Phase.Wave) || !TD_Elites.CanSpecialise(t.level, t.path)
                || !TD_Retention.Owns(Level, TD_Retention.EliteUnlock)) return false;
            float discount = Perks.upgradeDiscount;
            return state.cash >= Mathf.Min(TD_Elites.Cost(t.kind, TD_TowerPath.First, discount), TD_Elites.Cost(t.kind, TD_TowerPath.Second, discount));
        }

        void Highlight(int slot, bool on) {
            bool empty = towerViews[slot] == null;
            var pad = empty && !(on && placing) ? null : Pad(slot);
            if (pad != null) {
                pad.focused = on;
                pad.Refresh();
                if (pad.rangeRing != null) {
                    pad.rangeRing.gameObject.SetActive(on && empty && placing);
                    if (on && empty && placing) {
                        var kind = (TD_TowerKind)selected;
                        SetRing(pad.rangeRing, TD_Daily.Range(kind, 1, Modifier), CanBuildSelected ? TD_Palette.Tower(kind) : invalidColor, rangeRingAlpha);
                    }
                }
            }
            var tower = towerViews[slot];
            if (tower == null) return;
            var snap = towerSnaps[slot];
            Color color = TD_Palette.Tower(snap.kind);
            if (tower.rangeRing != null) {
                tower.rangeRing.gameObject.SetActive(on);
                if (on) SetRing(tower.rangeRing, TD_Elites.Range(snap.kind, snap.level, snap.path, Modifier), color, rangeRingAlpha);
            }
            if (tower.nextRangeRing != null) {
                // The next step's reach: the next level, or at level 3 the longer of the two elite forms (when one adds range).
                float now = TD_Elites.Range(snap.kind, snap.level, snap.path, Modifier), next = now;
                bool can = false;
                if (snap.level < TD_Rules.MaxLevel) {
                    next = TD_Daily.Range(snap.kind, snap.level + 1, Modifier);
                    can = CanUpgrade(slot);
                } else if (TD_Elites.CanSpecialise(snap.level, snap.path)) {
                    next = Mathf.Max(TD_Elites.Range(snap.kind, snap.level, TD_TowerPath.First, Modifier), TD_Elites.Range(snap.kind, snap.level, TD_TowerPath.Second, Modifier));
                    can = CanSpecialise(slot);
                }
                bool show = on && next > now + 0.01f;
                tower.nextRangeRing.gameObject.SetActive(show);
                if (show) SetRing(tower.nextRangeRing, next, can ? color : invalidColor, nextRangeRingAlpha);
            }
        }

        /// <summary>Lays a range ring out flat around its parent at <paramref name="radius"/> metres.</summary>
        void SetRing(LineRenderer ring, float radius, Color color, float alpha) {
            int n = ring.positionCount;
            Vector3 centre = ring.transform.position;
            for (int i = 0; i < n; i++) {
                float a = i * Mathf.PI * 2f / n;
                ring.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * radius, rangeRingHeight, Mathf.Sin(a) * radius));
            }
            color.a = alpha;
            ring.startColor = ring.endColor = color;
        }

        /// <summary>A see-through copy of the selected tower on the focused empty pad, red when it cannot be built.</summary>
        void UpdateGhost() {
            int wanted = placing && focusedSlot >= 0 && towerViews[focusedSlot] == null && map.ghostPrefabs != null && selected < map.ghostPrefabs.Length ? selected : -1;
            if (wanted != ghostKind) {
                if (ghostKind >= 0 && ghosts[ghostKind] != null) ghosts[ghostKind].SetActive(false);
                ghostKind = wanted;
            }
            if (ghostKind < 0) return;
            var ghost = Ghost(ghostKind);
            if (ghost == null) return;
            ghost.transform.position = TD_Rules.Slots[focusedSlot] + Vector3.up * padTop;
            ghost.SetActive(true);
            Color color = CanBuildSelected ? TD_Palette.Tower((TD_TowerKind)ghostKind) : invalidColor;
            color.a = Mathf.Clamp01(ghostAlpha + Mathf.Sin(Time.time * ghostPulseSpeed) * ghostPulse);
            block.Clear();
            block.SetColor(BaseColorId, color);
            foreach (var r in ghostRenderers[ghostKind]) r.SetPropertyBlock(block);
        }

        GameObject Ghost(int kind) {
            if (ghosts[kind] != null) return ghosts[kind];
            var prefab = map.ghostPrefabs[kind];
            if (prefab == null) return null;
            ghosts[kind] = Instantiate(prefab, map.transform);
            ghostRenderers[kind] = ghosts[kind].GetComponentsInChildren<Renderer>(true);
            return ghosts[kind];
        }

        void RefreshFocusCard() {
            if (focusedSlot < 0 && placing && hasState) hud.ShowPlaceHint((TD_TowerKind)selected);
            else if (focusedSlot < 0 || !hasState) hud.ClearFocus();
            else if (towerViews[focusedSlot] != null)
                hud.ShowFocusTower(towerSnaps[focusedSlot], state.cash, sellKey.ToString(), targetKey.ToString(), CanSell(towerSnaps[focusedSlot]), Perks.upgradeDiscount,
                    TD_Retention.Owns(Level, TD_Retention.EliteUnlock));
            else {
                var kind = (TD_TowerKind)selected;
                hud.ShowFocusPad(kind, state.cash, TD_Retention.CanBuild(Level, kind) ? 0 : TD_Retention.LevelOf(TD_Rules.Tower(kind).unlockId));
            }
        }

        // ---------------------------------------------------------------- state
        void OnState(TD_StateBroadcast message, Channel channel) {
            bool first = !hasState;
            var previousPhase = lastPhase;
            state = message;
            hasState = true;
            if (message.phase != TD_Phase.Build) ready = false;
            if (!first) {
                if (message.phase == TD_Phase.Wave && message.wave != bannerWave) WaveStarted(message);
                if (message.phase == TD_Phase.Build && previousPhase == TD_Phase.Wave && message.wave > 1) {
                    hud.Toast($"Wave {message.wave - 1} cleared!  +${TD_Daily.WaveBonus(message.wave - 1, message.mode, message.modifier)}", TD_Hud.ToastKind.Money, tuning.toastSeconds);
                    GameSfx.Play(SfxCue.Purchase);
                }
                if (message.phase == TD_Phase.Build && message.secondsLeft != lastSecondsLeft && message.secondsLeft <= countdownBeeps && message.secondsLeft > 0)
                    GameSfx.Play(SfxCue.CountdownBeep);
                if (message.baseHealth < lastBase) BaseHit(lastBase - message.baseHealth, message.baseHealth);
                if (message.phase != previousPhase && message.phase is TD_Phase.Victory or TD_Phase.Defeat) GameOver(message.phase == TD_Phase.Victory);
                if (message.phase == TD_Phase.Build && previousPhase == TD_Phase.Vote) ModeChosen(message.mode);
            }
            if (message.phase == TD_Phase.Vote && previousPhase != TD_Phase.Vote) {
                myVote = -1; // a fresh vote
                hud.SetMyVote(-1);
            }
            if (message.phase is TD_Phase.Waiting or TD_Phase.Victory or TD_Phase.Defeat) {
                // Not ClearDanger: called during the leak flash of the final hit, it stops the flash but leaves
                // the vignette at that frame's alpha (a red edge into the next game). The flash ends by itself.
                ScreenFx.SetDanger(0f);
                fastVote = false;
            }
            if (message.phase is TD_Phase.Victory or TD_Phase.Defeat) bannerWave = 0; // a new game starts again at wave 1
            // New enemies: their card shows through the build phase before their wave (and into it, see WaveStarted).
            if (message.phase == TD_Phase.Build) hud.ShowIntro(message.wave);
            if (message.phase is TD_Phase.Vote or TD_Phase.Victory or TD_Phase.Defeat or TD_Phase.Waiting) {
                hud.ResetIntro();
                introUntil = 0f;
                toldBlocked = false;
            }
            if (message.phase is TD_Phase.Vote || (message.phase == TD_Phase.Build && message.wave == 1)) toldRecord = toldEndlessRecord = false; // a new game: its bests may be told once
            if (!message.fastForwardAllowed) fastVote = false;
            lastBase = message.baseHealth;
            lastPhase = message.phase;
            lastSecondsLeft = message.secondsLeft;
            ApplyTowers(message.towers);
            hud.ShowState(message, ready, fastVote);
            ShowShop();
            if (hasAbilities && message.phase != previousPhase) hud.ShowAbilities(abilities, message.phase, Level);
            if (focusedSlot >= 0) Highlight(focusedSlot, true); // affordability may have changed the ring colours
            RefreshFocusCard();
        }

        /// <summary>The vote is over: a banner names the mode everyone plays.</summary>
        void ModeChosen(TD_Difficulty mode) {
            var stats = TD_Modes.Get(mode);
            var day = TD_Daily.Get(Modifier);
            hud.Banner($"{stats.name.ToUpperInvariant()} MODE", day.name.Length > 0 ? $"{stats.blurb}  Today: {day.name}. {day.blurb}" : stats.blurb, mode == TD_Difficulty.Hard);
            GameSfx.Play(SfxCue.Go);
        }

        void WaveStarted(in TD_StateBroadcast message) {
            bannerWave = message.wave;
            bool boss = TD_Rules.HasBoss(message.wave);
            string title = message.waveCount > 0 && message.wave >= message.waveCount ? "FINAL WAVE" : message.waveCount <= 0 ? $"ENDLESS WAVE {message.wave}" : $"WAVE {message.wave}";
            string fresh = NewKinds(message.wave);
            hud.Banner(title, (boss ? "BOSS WAVE  ·  " : "") + (fresh.Length > 0 ? $"NEW: {fresh}  ·  " : "") + Describe(message.wave), boss);
            GameSfx.Play(SfxCue.Horn);
            // The new-enemy card (up since the build phase, or from now for someone who just joined) stays a few seconds more.
            hud.ShowIntro(message.wave);
            introUntil = Time.unscaledTime + tuning.introWaveSeconds;
            if (message.earlyBonus > 0) {
                hud.Toast($"Called early!  +${message.earlyBonus}", TD_Hud.ToastKind.Money, tuning.toastSeconds);
                GameSfx.Play(SfxCue.Coin);
            }
        }

        /// <summary>
        /// Enemies got through: the base model flashes and the screen edges flash red for a moment
        /// (longer and stronger once the base is critical, with an alarm). Nothing stays on screen:
        /// the HUD's red base bar and pulsing base icon are the standing warning.
        /// </summary>
        void BaseHit(int damage, int health) {
            if (health > 0) hud.Toast($"The base took {damage} damage!", TD_Hud.ToastKind.Bad, tuning.shortToastSeconds); // at 0 the result card says it
            if (baseFlash != null) baseFlash.Flash(tuning.baseHitFlash, tuning.baseHitFlashSeconds);
            bool critical = health <= TD_Rules.BaseCriticalHealth;
            if (critical) ScreenFx.DangerFlash(tuning.leakFlashColor, tuning.criticalFlashSeconds, tuning.criticalFlashAlpha);
            else ScreenFx.DangerFlash(tuning.leakFlashColor, tuning.leakFlashSeconds, tuning.leakFlashAlpha);
            if (!critical) return;
            bool crossed = lastBase > TD_Rules.BaseCriticalHealth;
            if (!crossed && Time.unscaledTime - lastAlarm < tuning.alarmInterval) return;
            lastAlarm = Time.unscaledTime;
            GameSfx.Play(SfxCue.Alarm);
            if (crossed && health > 0) hud.Toast("The base is critical!", TD_Hud.ToastKind.Bad, tuning.toastSeconds);
        }

        void GameOver(bool won) {
            Vector3 at = map.baseModel != null ? map.baseModel.position : TD_Rules.Lane[^1];
            if (won) {
                GameFx.Play(FxKind.Confetti, at, tuning.victoryConfettiScale);
                confettiLeft = tuning.victoryScreenBursts;
                nextConfetti = Time.unscaledTime;
                GameSfx.Play(SfxCue.Win);
            } else {
                GameFx.Explosion(at, true, tuning.defeatShakeRadius);
                GameSfx.Play(SfxCue.Lose);
            }
        }

        /// <summary>Victory bursts in front of the camera, one every interval, at the tuning's viewport points beside the card.</summary>
        void ScreenConfetti() {
            if (confettiLeft <= 0 || Time.unscaledTime < nextConfetti) return;
            var cam = Camera.main;
            var points = tuning.victoryScreenBurstPoints;
            if (cam == null || points == null || points.Length == 0) {
                confettiLeft = 0;
                return;
            }
            Vector2 p = points[(tuning.victoryScreenBursts - confettiLeft) % points.Length];
            GameFx.Play(FxKind.Confetti, cam.ViewportToWorldPoint(new Vector3(p.x, p.y, tuning.victoryScreenBurstDepth)), tuning.victoryScreenBurstScale);
            confettiLeft--;
            nextConfetti = Time.unscaledTime + tuning.victoryScreenBurstInterval;
        }

        /// <summary>The enemies <paramref name="wave"/> brings for the first time, by name ("" for none).</summary>
        static string NewKinds(int wave) {
            var names = new List<string>();
            for (int i = 0; i < TD_Rules.Enemies.Length; i++)
                if (TD_Rules.IsNew((TD_EnemyKind)i, wave)) names.Add(TD_Rules.Enemies[i].name);
            return string.Join(", ", names);
        }

        static string Describe(int wave) {
            int[] counts = TD_Rules.WaveCounts(wave);
            var parts = new List<string>();
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] > 0) parts.Add($"{counts[i]} {TD_Rules.Enemies[i].name}");
            return string.Join("  ·  ", parts);
        }

        void ApplyTowers(TD_TowerSnap[] towers) {
            System.Array.Clear(present, 0, present.Length);
            if (towers != null)
                foreach (var t in towers) {
                    if (t.slot >= towerViews.Length) continue;
                    present[t.slot] = true;
                    var view = towerViews[t.slot];
                    if (view == null || towerSnaps[t.slot].kind != t.kind) {
                        if (view != null) Destroy(view.gameObject);
                        var go = Instantiate(map.towerPrefabs[(int)t.kind], PadTop(t.slot), Quaternion.identity, map.transform);
                        view = towerViews[t.slot] = go.GetComponent<TD_TowerView>();
                        view.aim = view.head != null ? view.head.rotation : Quaternion.identity;
                        towerSnaps[t.slot] = t;
                        view.ShowLevel(t.level, t.path);
                        if (pendingPop[t.slot]) Punch.Pop(view.head != null ? view.head : view.transform);
                        if (t.slot == focusedSlot) Highlight(t.slot, true);
                    }
                    pendingPop[t.slot] = false;
                    towerSnaps[t.slot] = t;
                    if (view.goldTrim != null && view.goldTrim.activeSelf != t.gold) view.goldTrim.SetActive(t.gold);
                    if (view.shownLevel != t.level || view.shownPath != t.path) {
                        view.ShowLevel(t.level, t.path);
                        if (t.slot == focusedSlot) Highlight(t.slot, true);
                    }
                }
            for (int i = 0; i < towerViews.Length; i++) {
                if (!present[i] && towerViews[i] != null) {
                    Destroy(towerViews[i].gameObject);
                    towerViews[i] = null;
                    if (i == focusedSlot) Highlight(i, true); // back to an empty pad: preview the selected tower
                }
            }
            UpdateGrid();
            for (int i = 0; i < present.Length; i++) {
                var pad = slotViews[i];
                if (pad == null) continue;
                pad.built = present[i];
                if (present[i]) pad.builtColor = TD_Palette.Tower(towerSnaps[i].kind);
                pad.Refresh();
            }
        }

        Vector3 PadTop(int slot) => TD_Rules.Slots[slot] + Vector3.up * padTop;

        void OnTowerEvent(TD_TowerEventBroadcast message, Channel channel) {
            if (message.slot >= towerViews.Length) return;
            var view = towerViews[message.slot];
            Transform punch = view == null ? null : view.head != null ? view.head : view.transform;
            Vector3 pad = PadTop(message.slot);
            switch (message.kind) {
                case TD_TowerEvent.Built:
                    pendingPop[message.slot] = true; // the tower itself arrives with the next state
                    fx.Built(punch, pad, message.tower);
                    break;
                case TD_TowerEvent.Upgraded:
                    fx.Upgraded(punch, pad);
                    break;
                case TD_TowerEvent.Sold:
                    fx.Sold(pad, message.amount);
                    break;
                case TD_TowerEvent.Specialised: {
                    var elite = TD_Elites.Get(message.tower, message.path);
                    fx.Specialised(punch, pad, TD_Palette.Elite(message.path));
                    string who = string.IsNullOrEmpty(message.player) ? "A player" : message.player;
                    hud.Toast($"{who} made an elite {elite.name}!", TD_Hud.ToastKind.Good, tuning.toastSeconds);
                    fx.guard?.Invalidate();
                    break;
                }
            }
        }

        void OnResult(TD_ResultBroadcast message, Channel channel) {
            hud.Toast(message.message, message.accepted ? TD_Hud.ToastKind.Good : TD_Hud.ToastKind.Bad, tuning.toastSeconds);
            if (!message.accepted) GameSfx.Play(SfxCue.Error);
        }

        void OnProfile(TD_ProfileBroadcast message, Channel channel) {
            profile = message;
            hasProfile = true;
            hud.ShowRecord(message);
            if (hasState && state.phase == TD_Phase.Vote) hud.ShowState(state, ready, fastVote); // the cards' locks and bests
            // The server announces a new best once per game; the result card repeats it as "New best!".
            if (state.phase is TD_Phase.Victory or TD_Phase.Defeat) return;
            if (message.newBestEndless && !toldEndlessRecord) {
                toldEndlessRecord = toldRecord = true;
                hud.Toast($"New Endless best: wave {message.bestEndlessWave}!", TD_Hud.ToastKind.Good, tuning.toastSeconds);
                GameSfx.Play(SfxCue.LevelUp);
            } else if (message.newBestWave && !toldRecord) {
                toldRecord = true;
                hud.Toast($"New personal best: wave {message.bestWave}!", TD_Hud.ToastKind.Good, tuning.toastSeconds);
                GameSfx.Play(SfxCue.LevelUp);
            }
        }

        void OnAbilityState(TD_AbilityStateBroadcast message, Channel channel) {
            abilities = message;
            hasAbilities = true;
            hud.ShowAbilities(message, hasState ? state.phase : TD_Phase.Waiting, Level);
        }

        /// <summary>Someone used an ability: the jet and bombs, or the EMP shockwave (the server already did the damage or the stun).</summary>
        void OnAbility(TD_AbilityBroadcast message, Channel channel) {
            var stats = TD_Abilities.Get(message.ability);
            string caster = string.IsNullOrEmpty(message.caster) ? "A player" : message.caster;
            // The toast first, so the ability's world callout below is kept clear of it (TD_ScreenGuard).
            // A second call in the same wave is weaker (TD_Abilities.RepeatScale); say so, so it is not a surprise.
            string weaker = message.power > 0f && message.power < 0.99f ? $"  ({Mathf.RoundToInt(message.power * 100f)}%: already used this wave)" : "";
            hud.Toast($"{caster}: {stats.name}!" + (message.hits > 0 && message.ability == TD_Ability.Emp ? $"  {message.hits} stopped" : "") + weaker, TD_Hud.ToastKind.Info, tuning.shortToastSeconds);
            fx.guard?.Invalidate();
            if (message.ability == TD_Ability.Airstrike) {
                Vector3 target = TD_Rules.LaneTrack.Evaluate(message.distance, out Vector3 forward);
                float speed = hasState && state.speed > 0f ? state.speed : 1f;
                fx.Airstrike(target, forward, stats.radius, message.delay / speed, caster);
            } else {
                empTargets.Clear();
                foreach (var view in enemyViews.Values) empTargets.Add(view.transform.position + Vector3.up * tuning.aimHeight);
                Vector3 origin = map.baseModel != null ? map.baseModel.position : TD_Rules.Lane[^1];
                fx.Emp(origin, empTargets, caster);
            }
        }

        // ---------------------------------------------------------------- snapshots
        void OnSnapshot(TD_SnapshotBroadcast message, Channel channel) {
            snapshotAt = Time.time;
            float speed = hasState && state.speed > 0f ? state.speed : 1f;
            seen.Clear();
            TD_EnemyView boss = null;
            int bosses = 0;
            if (message.enemies != null)
                foreach (var e in message.enemies) {
                    seen.Add(e.id);
                    if (!enemyViews.TryGetValue(e.id, out var view)) {
                        view = Rent(e.kind);
                        view.shownDistance = e.distance;
                        view.health = 1f;
                        enemyViews[e.id] = view;
                        enemyKinds[e.id] = e.kind;
                    }
                    view.targetDistance = e.distance;
                    float slow = e.slow > 0 ? e.slow / 255f : 1f; // TD_Rules.SlowFactor, as the server applied it
                    view.speed = TD_Daily.Speed(e.kind, Modifier) * slow * speed;
                    view.slowed = e.slowed;
                    SetHealth(view, e.health / 255f);
                    SetShield(view, e.shield / 255f);
                    // Frost shell only for real slows; a shield bubble and frost never show together (shields shrug off frost).
                    bool frosted = e.slowed && e.shield == 0 && e.slow > 0;
                    if (view.slowTint != null && view.slowTint.gameObject.activeSelf != frosted) view.slowTint.gameObject.SetActive(frosted);
                    if (TD_Rules.IsBoss(e.kind)) {
                        bosses++;
                        if (boss == null) boss = view; // the one furthest along the road
                    }
                }
            hud.ShowBoss(bosses, boss != null ? boss.health : 0f);

            // Shots, chains, breaks and heals first, while the enemies they touch still have their views.
            if (message.shots != null) foreach (var shot in message.shots) DrawShot(shot);
            if (message.shieldsBroken != null)
                foreach (var id in message.shieldsBroken)
                    if (enemyViews.TryGetValue(id, out var broken) && broken.seen) fx.ShieldBroken(broken.transform.position + Vector3.up * tuning.aimHeight);
            if (message.healed != null)
                foreach (var id in message.healed) {
                    if (!enemyViews.TryGetValue(id, out var healed) || !healed.seen || Time.time < healed.nextHealFx) continue;
                    healed.nextHealFx = Time.time + tuning.healFxInterval;
                    fx.Healed(healed.transform.position + Vector3.up * tuning.aimHeight);
                }

            Fill(killedIds, message.killed);
            Fill(leakedIds, message.leaked);
            gone.Clear();
            foreach (var id in enemyViews.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone) {
                var view = enemyViews[id];
                var kind = enemyKinds[id];
                if (view.seen) {
                    if (killedIds.Contains(id)) fx.Destroyed(kind, view.transform.position, TD_Daily.KillReward(kind, hasState ? state.mode : TD_Difficulty.Normal, Modifier));
                    else if (leakedIds.Contains(id)) fx.Leaked(kind, view.transform.position);
                }
                Return(kind, view);
                enemyViews.Remove(id);
                enemyKinds.Remove(id);
            }
        }

        static void Fill(HashSet<ushort> set, ushort[] ids) {
            set.Clear();
            if (ids != null) foreach (var id in ids) set.Add(id);
        }

        void SetHealth(TD_EnemyView view, float fraction) {
            if (fraction < view.health - 0.001f) view.hitFlash = 1f;
            view.health = fraction;
            if (view.healthFill != null) view.healthFill.localScale = new Vector3(Mathf.Max(0.001f, fraction), 1f, 1f);
            if (view.healthRenderer != null) {
                view.healthRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, Color.Lerp(tuning.enemyBarEmpty, tuning.enemyBarFull, fraction));
                view.healthRenderer.SetPropertyBlock(block);
            }
        }

        /// <summary>The shield bubble shows while the shield holds; its bar shows what is left of it.</summary>
        static void SetShield(TD_EnemyView view, float fraction) {
            view.shield = fraction;
            bool on = fraction > 0f;
            if (view.shieldBubble != null && view.shieldBubble.activeSelf != on) view.shieldBubble.SetActive(on);
            if (view.shieldFill == null) return;
            if (view.shieldFill.parent != null && view.shieldFill.parent.gameObject.activeSelf != on) view.shieldFill.parent.gameObject.SetActive(on);
            view.shieldFill.localScale = new Vector3(Mathf.Max(0.001f, fraction), 1f, 1f);
        }

        /// <summary>Slowed enemies are tinted frost blue; hits flash them bright for a moment.</summary>
        void PaintBody(TD_EnemyView view) {
            if (view.bodyRenderers == null) return;
            bool flashing = view.hitFlash > 0f;
            if (!flashing && !view.slowed) {
                if (!view.tinted) return;
                foreach (var r in view.bodyRenderers) r.SetPropertyBlock(null);
                view.tinted = false;
                return;
            }
            Color color = view.slowed ? tuning.slowTint : Color.white;
            if (flashing) color = Color.LerpUnclamped(color, tuning.hitFlashColor * tuning.hitFlashBrightness, view.hitFlash * tuning.hitFlashStrength);
            block.Clear();
            block.SetColor(BaseColorId, color);
            foreach (var r in view.bodyRenderers) r.SetPropertyBlock(block);
            view.tinted = true;
        }

        TD_EnemyView Rent(TD_EnemyKind kind) {
            if (enemyPool.TryGetValue(kind, out var pool) && pool.Count > 0) {
                var v = pool.Pop();
                v.gameObject.SetActive(true);
                v.seen = false;
                return v;
            }
            return Instantiate(map.enemyPrefabs[(int)kind], map.transform).GetComponent<TD_EnemyView>();
        }

        void Return(TD_EnemyKind kind, TD_EnemyView view) {
            view.slowed = false;
            view.nextHealFx = 0f;
            view.hitFlash = 0f;
            PaintBody(view);
            view.gameObject.SetActive(false);
            if (!enemyPool.TryGetValue(kind, out var pool)) enemyPool[kind] = pool = new Stack<TD_EnemyView>();
            pool.Push(view);
        }

        void MoveEnemies(float dt) {
            float since = Time.time - snapshotAt;
            foreach (var pair in enemyViews) {
                var view = pair.Value;
                float predicted = view.targetDistance + view.speed * Mathf.Min(since, 0.5f);
                view.shownDistance = Mathf.Lerp(view.shownDistance + view.speed * dt, predicted, 1f - Mathf.Exp(-8f * dt));
                Vector3 p = TD_Rules.EnemyPosition(enemyKinds[pair.Key], view.shownDistance, out Vector3 forward);
                view.transform.SetPositionAndRotation(p, Quaternion.Slerp(view.seen ? view.transform.rotation : Quaternion.LookRotation(forward), Quaternion.LookRotation(forward), 1f - Mathf.Exp(-10f * dt)));
                view.seen = true;
                view.hitFlash = Mathf.MoveTowards(view.hitFlash, 0f, dt * 6f);
                // A small shudder when hit sells the impact; the animator also turns wheels and rotors.
                float shake = Mathf.Sin(Time.time * 80f) * view.hitFlash * 0.12f * TD_Rules.WorldScale;
                if (view.animator != null) view.animator.Animate(view.speed, dt, shake);
                else if (view.body != null) view.body.localPosition = new Vector3(shake, 0f, 0f);
                PaintBody(view);
            }
        }

        /// <summary>
        /// Tower badges (level stars) face the camera plane and are capped in screen size by their
        /// Billboard; here they hide when too close to the camera, behind it, beside the screen edge or
        /// high enough to reach the status bar. The owner's name is on the info card, not in the world.
        /// </summary>
        void UpdateBadges() {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            float near = tuning.badgeHideDistance * tuning.badgeHideDistance;
            for (int i = 0; i < towerViews.Length; i++) {
                var tower = towerViews[i];
                if (tower == null || tower.badge == null) continue;
                Vector3 at = tower.badge.position;
                Vector3 v = cam.WorldToViewportPoint(at);
                bool show = v.z > 0f && (at - eye).sqrMagnitude > near && v.y < tuning.badgeMaxViewportY
                    && v.x > tuning.badgeEdgeMargin && v.x < 1f - tuning.badgeEdgeMargin;
                if (tower.badge.gameObject.activeSelf != show) tower.badge.gameObject.SetActive(show);
            }
        }

        void TurnTowers(float dt) {
            for (int i = 0; i < towerViews.Length; i++) {
                var tower = towerViews[i];
                if (tower == null || tower.head == null) continue;
                Quaternion wanted = tower.aim;
                if (Time.time - towerLastShot[i] > idleSweepDelay)
                    wanted = Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.4f + i) * 70f + i * 40f, 0f);
                tower.head.rotation = Quaternion.RotateTowards(tower.head.rotation, wanted, tower.turnSpeed * dt);
                tower.recoil = Mathf.MoveTowards(tower.recoil, 0f, dt * 4f);
                tower.head.localPosition = new Vector3(0f, tower.head.localPosition.y, 0f) - tower.head.localRotation * Vector3.forward * (tower.recoil * 0.35f);
                var spinner = tower.ActiveSpinner;
                if (spinner != null) spinner.localRotation = Quaternion.Euler(0f, Time.time * eliteSpinSpeed + i * 37f, 0f);
            }
        }

        void DrawShot(TD_Shot shot) {
            if (shot.slot >= towerViews.Length || towerViews[shot.slot] == null || !enemyViews.TryGetValue(shot.enemy, out var target)) return;
            var tower = towerViews[shot.slot];
            Vector3 aim = target.transform.position + Vector3.up * tuning.aimHeight;
            // A Tesla bolt jumping on from an enemy it already hit.
            if (shot.from != 0) {
                if (enemyViews.TryGetValue(shot.from, out var source)) {
                    Vector3 start = source.transform.position + Vector3.up * tuning.aimHeight;
                    fx.Chain(start, aim);
                    if (shot.blocked) Blocked(aim, start, towerSnaps[shot.slot].kind);
                }
                return;
            }
            if (tower.head != null) {
                Vector3 flat = aim - tower.head.position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f) {
                    tower.aim = Quaternion.LookRotation(flat);
                    tower.head.rotation = tower.aim; // snap: shots arrive ~10 times a second
                }
            }
            towerLastShot[shot.slot] = Time.time;
            tower.recoil = 1f;
            var muzzle = tower.Muzzle;
            Vector3 from = muzzle != null ? muzzle.position : tower.transform.position + Vector3.up * tuning.fallbackMuzzleHeight;
            var kind = towerSnaps[shot.slot].kind;
            fx.Shot(kind, from, target.transform, aim);
            // A hit that bounced off the shield (it did nothing) looks and sounds unlike one the shield soaked.
            if (shot.blocked) Blocked(aim, from, kind);
            // Instant hits the shield soaked spark in the shield colour (shells show it where they land).
            else if (target.shield > 0f && kind is TD_TowerKind.Gunner or TD_TowerKind.Sniper or TD_TowerKind.Tesla) fx.ShieldSpark(aim);
        }

        /// <summary>A shot that bounced off a shield: a ricochet, and the first time in a game a toast that says why and what to build.</summary>
        void Blocked(Vector3 at, Vector3 from, TD_TowerKind kind) {
            fx.Ricochet(at, from);
            if (toldBlocked) return;
            toldBlocked = true;
            hud.Toast($"{TD_Rules.Tower(kind).name} shots bounce off shields! Break them with a Cannon, Sniper or Tesla.", TD_Hud.ToastKind.Bad, tuning.blockedHintSeconds);
        }
    }
}
