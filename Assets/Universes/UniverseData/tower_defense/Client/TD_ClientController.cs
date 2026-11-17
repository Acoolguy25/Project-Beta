using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.GameFeel;
using RyanAssets.Input;
using RyanAssets.Rounds;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// Draws the defence from server snapshots (enemies, towers, shots), feeds the <see cref="TD_Hud"/>
    /// and sends build, upgrade, sell, ready and fast-forward requests. Keys: 1-4 pick a tower, E
    /// builds or upgrades the pad in front of you, X (twice) sells the tower there, R votes to start
    /// the next wave early, G votes for fast forward. All feedback (blasts, popups, shake, sounds,
    /// base alarm) is a reaction to server broadcasts. The key names the HUD shows come from here.
    /// </summary>
    [RequireComponent(typeof(TD_Hud))]
    public sealed class TD_ClientController : MonoBehaviour {
        [Header("Feel")]
        public TD_FxTuning tuning = new();
        [Tooltip("Countdown beeps during the last seconds of a build phase.")]
        [Min(0)] public int countdownBeeps = 3;

        [Header("Keys")]
        public Key sellKey = Key.X;
        public Key fastForwardKey = Key.G;
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
        readonly List<Vector3> slotPositions = new(TD_Rules.Slots);
        GameObject[] ghosts;
        float[] markerLift;
        Renderer[][] ghostRenderers;
        MaterialPropertyBlock block;
        int lastBase = TD_Rules.BaseHealth;
        TD_Phase lastPhase;
        int bannerWave, lastSecondsLeft;

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
            hud.SetKeys(BindingLabel("ReloadTool", "R"), fastForwardKey.ToString(), BindingLabel("Interact", "E"));
            for (int i = 0; i < TD_Rules.Slots.Length; i++) {
                var go = Instantiate(map.slotPrefab, TD_Rules.Slots[i], Quaternion.identity, map.transform);
                go.name = $"Slot {i + 1}";
                slotViews[i] = go.GetComponent<TD_SlotView>();
                slotViews[i].Refresh();
            }
            hud.ShowShop(selected, 0, false);
        }

        void OnEnable() {
            ToolControls.interactPressed += Interact;
            ToolControls.toolBarHotkeyPressed += Select;
            ToolControls.reloadToolPressed += ToggleReady;
        }

        void OnDisable() {
            ToolControls.interactPressed -= Interact;
            ToolControls.toolBarHotkeyPressed -= Select;
            ToolControls.reloadToolPressed -= ToggleReady;
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
            ScreenFx.ClearDanger();
            if (manager == null) return;
            manager.UnregisterBroadcast<TD_StateBroadcast>(OnState);
            manager.UnregisterBroadcast<TD_SnapshotBroadcast>(OnSnapshot);
            manager.UnregisterBroadcast<TD_ResultBroadcast>(OnResult);
            manager.UnregisterBroadcast<TD_TowerEventBroadcast>(OnTowerEvent);
            manager.UnregisterBroadcast<TD_ProfileBroadcast>(OnProfile);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<TD_StateBroadcast>(OnState);
                manager.RegisterBroadcast<TD_SnapshotBroadcast>(OnSnapshot);
                manager.RegisterBroadcast<TD_ResultBroadcast>(OnResult);
                manager.RegisterBroadcast<TD_TowerEventBroadcast>(OnTowerEvent);
                manager.RegisterBroadcast<TD_ProfileBroadcast>(OnProfile);
            }
            if (manager != null && manager.Started && !hasState && Time.unscaledTime > nextRequest) {
                nextRequest = Time.unscaledTime + 2f;
                manager.Broadcast(new TD_StateRequest());
            }
            ReadKeys();
            MoveEnemies(Time.deltaTime);
            TurnTowers(Time.deltaTime);
            BobMarkers();
            UpdateBadges();
            ScreenConfetti();
            fx.Update();
            UpdateFocus();
            UpdateGhost();
        }

        // ---------------------------------------------------------------- input
        void Select(int key) {
            if (key < 1 || key > TD_Rules.Towers.Length) return;
            selected = key - 1;
            hud.ShowShop(selected, state.cash, hasState);
            if (focusedSlot >= 0) Highlight(focusedSlot, true); // re-preview the range of the new pick
            RefreshFocusCard();
        }

        void ToggleReady() {
            if (manager == null || !hasState || state.phase != TD_Phase.Build) return;
            ready = !ready;
            manager.Broadcast(new TD_ReadyRequest { ready = ready });
            hud.ShowState(state, ready, fastVote);
            hud.Toast(ready ? "Ready! Waiting for the others..." : "Not ready", TD_Hud.ToastKind.Good, tuning.shortToastSeconds);
        }

        void Interact() {
            UpdateFocus();
            if (focusedSlot < 0 || manager == null || !manager.Started) return;
            manager.Broadcast(new TD_BuildRequest { slot = (byte)focusedSlot, kind = (TD_TowerKind)selected });
        }

        void ReadKeys() {
            var keyboard = Keyboard.current;
            if (keyboard == null || !Application.isFocused || TextFieldFocused()) return;
            if (keyboard[sellKey].wasPressedThisFrame) Sell();
            if (keyboard[fastForwardKey].wasPressedThisFrame) ToggleFastForward();
        }

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
            hud.Toast($"Press {sellKey} again to sell the {TD_Rules.Tower(tower.kind).name} for ${TD_Rules.SellRefund(tower.kind, tower.level)}", TD_Hud.ToastKind.Money, sellConfirmSeconds);
        }

        /// <summary>Whether this client may sell <paramref name="tower"/>: unclaimed, or built by this player (same rule as the server).</summary>
        bool CanSell(in TD_TowerSnap tower) => tower.ownerId < 0 || (manager != null && manager.Connection != null && tower.ownerId == manager.Connection.ClientId);

        void ToggleFastForward() {
            if (manager == null || !manager.Started || !hasState || !state.fastForwardAllowed) return;
            fastVote = !fastVote;
            manager.Broadcast(new TD_SpeedRequest { fast = fastVote });
            hud.ShowState(state, ready, fastVote);
            hud.Toast(fastVote ? "Voted for fast forward" : "Voted for normal speed", TD_Hud.ToastKind.Info, tuning.shortToastSeconds);
        }

        // ---------------------------------------------------------------- focus + placement preview
        void UpdateFocus() {
            var character = LocalPlayer.Character;
            var cam = Camera.main;
            int previous = focusedSlot;
            focusedSlot = -1;
            if (character != null && !character.IsDead && cam != null && hasState)
                focusedSlot = FocusPicker.Pick(cam.transform.position, cam.transform.forward, slotPositions, TD_Rules.SlotReach, 0.2f, body: character.transform.position);
            if (focusedSlot == previous) return;
            if (previous >= 0) Highlight(previous, false);
            if (focusedSlot >= 0) Highlight(focusedSlot, true);
            RefreshFocusCard();
        }

        /// <summary>Whether the selected tower can go on an empty pad right now (money and phase).</summary>
        bool CanBuildSelected => hasState && state.phase is not (TD_Phase.Victory or TD_Phase.Defeat) && state.cash >= TD_Rules.Towers[selected].cost;

        bool CanUpgrade(int slot) => hasState && state.phase is not (TD_Phase.Victory or TD_Phase.Defeat)
            && towerSnaps[slot].level < TD_Rules.MaxLevel && state.cash >= TD_Rules.UpgradeCost(towerSnaps[slot].kind, towerSnaps[slot].level);

        void Highlight(int slot, bool on) {
            var pad = slotViews[slot];
            bool empty = towerViews[slot] == null;
            if (pad != null) {
                pad.focused = on;
                pad.Refresh();
                if (pad.rangeRing != null) {
                    pad.rangeRing.gameObject.SetActive(on && empty);
                    if (on && empty) {
                        var kind = (TD_TowerKind)selected;
                        SetRing(pad.rangeRing, TD_Rules.Range(kind, 1), CanBuildSelected ? TD_Palette.Tower(kind) : invalidColor, rangeRingAlpha);
                    }
                }
            }
            var tower = towerViews[slot];
            if (tower == null) return;
            var snap = towerSnaps[slot];
            Color color = TD_Palette.Tower(snap.kind);
            if (tower.rangeRing != null) {
                tower.rangeRing.gameObject.SetActive(on);
                if (on) SetRing(tower.rangeRing, TD_Rules.Range(snap.kind, snap.level), color, rangeRingAlpha);
            }
            if (tower.nextRangeRing != null) {
                bool next = on && snap.level < TD_Rules.MaxLevel;
                tower.nextRangeRing.gameObject.SetActive(next);
                if (next) SetRing(tower.nextRangeRing, TD_Rules.Range(snap.kind, snap.level + 1), CanUpgrade(slot) ? color : invalidColor, nextRangeRingAlpha);
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
            int wanted = focusedSlot >= 0 && towerViews[focusedSlot] == null && map.ghostPrefabs != null && selected < map.ghostPrefabs.Length ? selected : -1;
            if (wanted != ghostKind) {
                if (ghostKind >= 0 && ghosts[ghostKind] != null) ghosts[ghostKind].SetActive(false);
                ghostKind = wanted;
            }
            if (ghostKind < 0) return;
            var ghost = Ghost(ghostKind);
            if (ghost == null) return;
            var pad = slotViews[focusedSlot];
            ghost.transform.position = TD_Rules.Slots[focusedSlot] + Vector3.up * (pad != null ? pad.topHeight : 0f);
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
            if (focusedSlot < 0 || !hasState) hud.ClearFocus();
            else if (towerViews[focusedSlot] != null) hud.ShowFocusTower(towerSnaps[focusedSlot], state.cash, sellKey.ToString(), CanSell(towerSnaps[focusedSlot]));
            else hud.ShowFocusPad((TD_TowerKind)selected, state.cash);
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
                    hud.Toast($"Wave {message.wave - 1} cleared!  +${TD_Rules.WaveBonus(message.wave - 1)}", TD_Hud.ToastKind.Money, tuning.toastSeconds);
                    GameSfx.Play(SfxCue.Purchase);
                }
                if (message.phase == TD_Phase.Build && message.secondsLeft != lastSecondsLeft && message.secondsLeft <= countdownBeeps && message.secondsLeft > 0)
                    GameSfx.Play(SfxCue.CountdownBeep);
                if (message.baseHealth < lastBase) BaseHit(lastBase - message.baseHealth, message.baseHealth);
                if (message.phase != previousPhase && message.phase is TD_Phase.Victory or TD_Phase.Defeat) GameOver(message.phase == TD_Phase.Victory);
            }
            if (message.phase is TD_Phase.Waiting or TD_Phase.Victory or TD_Phase.Defeat) {
                ScreenFx.ClearDanger();
                fastVote = false;
            }
            if (message.phase is TD_Phase.Victory or TD_Phase.Defeat) bannerWave = 0; // a new game starts again at wave 1
            if (message.phase == TD_Phase.Build && message.wave == 1) toldRecord = false; // a new game: its best may be told once
            if (!message.fastForwardAllowed) fastVote = false;
            lastBase = message.baseHealth;
            lastPhase = message.phase;
            lastSecondsLeft = message.secondsLeft;
            ApplyTowers(message.towers);
            hud.ShowState(message, ready, fastVote);
            hud.ShowShop(selected, message.cash, true);
            if (focusedSlot >= 0) Highlight(focusedSlot, true); // affordability may have changed the ring colours
            RefreshFocusCard();
        }

        void WaveStarted(in TD_StateBroadcast message) {
            bannerWave = message.wave;
            bool boss = TD_Rules.HasBoss(message.wave);
            hud.Banner($"WAVE {message.wave}", (boss ? "BOSS WAVE  ·  " : "") + Describe(message.wave), boss);
            GameSfx.Play(SfxCue.Horn);
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
                        view.ShowLevel(t.level);
                        if (pendingPop[t.slot]) Punch.Pop(view.head != null ? view.head : view.transform);
                        if (t.slot == focusedSlot) Highlight(t.slot, true);
                    }
                    pendingPop[t.slot] = false;
                    towerSnaps[t.slot] = t;
                    if (view.shownLevel != t.level) {
                        view.ShowLevel(t.level);
                        if (t.slot == focusedSlot) Highlight(t.slot, true);
                    }
                }
            for (int i = 0; i < towerViews.Length; i++) {
                if (!present[i] && towerViews[i] != null) {
                    Destroy(towerViews[i].gameObject);
                    towerViews[i] = null;
                    if (i == focusedSlot) Highlight(i, true); // back to an empty pad: preview the selected tower
                }
                var pad = slotViews[i];
                if (pad == null) continue;
                if (pad.marker != null) pad.marker.gameObject.SetActive(!present[i]);
                pad.built = present[i];
                if (present[i]) pad.builtColor = TD_Palette.Tower(towerSnaps[i].kind);
                pad.Refresh();
            }
        }

        Vector3 PadTop(int slot) => TD_Rules.Slots[slot] + Vector3.up * (slotViews[slot] != null ? slotViews[slot].topHeight : 0f);

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
            }
        }

        void OnResult(TD_ResultBroadcast message, Channel channel) {
            hud.Toast(message.message, message.accepted ? TD_Hud.ToastKind.Good : TD_Hud.ToastKind.Bad, tuning.toastSeconds);
            if (!message.accepted) GameSfx.Play(SfxCue.Error);
        }

        void OnProfile(TD_ProfileBroadcast message, Channel channel) {
            hud.ShowRecord(message);
            // The server announces a new best once per game; the result card repeats it as "New best!".
            if (!message.newBestWave || toldRecord || state.phase is TD_Phase.Victory or TD_Phase.Defeat) return;
            toldRecord = true;
            hud.Toast($"New personal best: wave {message.bestWave}!", TD_Hud.ToastKind.Good, tuning.toastSeconds);
            GameSfx.Play(SfxCue.LevelUp);
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
                    view.speed = TD_Rules.Enemy(e.kind).speed * slow * speed;
                    view.slowed = e.slowed;
                    SetHealth(view, e.health / 255f);
                    if (view.slowTint != null && view.slowTint.gameObject.activeSelf != e.slowed) view.slowTint.gameObject.SetActive(e.slowed);
                    if (TD_Rules.IsBoss(e.kind)) {
                        bosses++;
                        if (boss == null) boss = view; // the one furthest along the road
                    }
                }
            hud.ShowBoss(bosses, boss != null ? boss.health : 0f);

            Fill(killedIds, message.killed);
            Fill(leakedIds, message.leaked);
            gone.Clear();
            foreach (var id in enemyViews.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone) {
                var view = enemyViews[id];
                var kind = enemyKinds[id];
                if (view.seen) {
                    if (killedIds.Contains(id)) fx.Destroyed(kind, view.transform.position, TD_Rules.Enemy(kind).reward);
                    else if (leakedIds.Contains(id)) fx.Leaked(kind, view.transform.position);
                }
                Return(kind, view);
                enemyViews.Remove(id);
                enemyKinds.Remove(id);
            }
            if (message.shots != null) foreach (var shot in message.shots) DrawShot(shot);
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
                if (view.body != null) {
                    // A small shudder when hit sells the impact.
                    float shake = view.hitFlash * 0.12f;
                    view.body.localPosition = new Vector3(Mathf.Sin(Time.time * 80f) * shake, 0f, 0f);
                }
                PaintBody(view);
            }
        }

        void BobMarkers() {
            for (int i = 0; i < slotViews.Length; i++) {
                var marker = slotViews[i] != null ? slotViews[i].marker : null;
                if (marker == null || !marker.gameObject.activeSelf) continue;
                Vector3 p = marker.localPosition;
                // On the focused pad the marker floats up out of the build ghost instead of through it.
                markerLift ??= new float[slotViews.Length];
                float lift = i == focusedSlot && ghostKind >= 0 ? markerGhostLift : 0f;
                markerLift[i] = Mathf.MoveTowards(markerLift[i], lift, Time.deltaTime * tuning.markerLiftSpeed);
                p.y = markerHeight + markerLift[i] + Mathf.Sin(Time.time * 2.2f + i * 0.7f) * 0.25f;
                marker.localPosition = p;
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
            }
        }

        void DrawShot(TD_Shot shot) {
            if (shot.slot >= towerViews.Length || towerViews[shot.slot] == null || !enemyViews.TryGetValue(shot.enemy, out var target)) return;
            var tower = towerViews[shot.slot];
            Vector3 aim = target.transform.position + Vector3.up * tuning.aimHeight;
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
            Vector3 from = tower.muzzle != null ? tower.muzzle.position : tower.transform.position + Vector3.up * tuning.fallbackMuzzleHeight;
            fx.Shot(towerSnaps[shot.slot].kind, from, target.transform, aim);
        }
    }
}
