using System.Collections.Generic;
using System.Text;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Input;
using RyanAssets.Rounds;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// Draws the defence from server snapshots (enemies, towers, shots) and sends build, upgrade and
    /// ready requests. Keys: 1-4 pick a tower, E builds or upgrades the pad in front of you, R votes
    /// to start the next wave early.
    /// </summary>
    public sealed class TD_ClientController : MonoBehaviour {
        public TMP_Text waveLabel, cashLabel, baseLabel, promptLabel, toastLabel, bannerLabel, headlineLabel;
        public Image baseFill;
        public TMP_Text[] towerButtons;
        public Image[] towerButtonBacks;
        public Color selectedColor = new(1f, 0.8f, 0.25f, 0.9f), idleColor = new(0.05f, 0.07f, 0.12f, 0.75f);
        public AudioSource sfx;
        public AudioClip buildClip, deniedClip, leakClip, waveClip;

        TD_Map map;
        ClientManager manager;
        TD_StateBroadcast state;
        bool hasState, ready;
        int selected, focusedSlot = -1;
        float toastUntil, nextRequest, snapshotAt;
        readonly Dictionary<ushort, TD_EnemyView> enemyViews = new();
        readonly Dictionary<ushort, TD_EnemyKind> enemyKinds = new();
        readonly Dictionary<TD_EnemyKind, Stack<TD_EnemyView>> enemyPool = new();
        readonly HashSet<ushort> seen = new();
        readonly TD_TowerView[] towerViews = new TD_TowerView[TD_Rules.Slots.Length];
        readonly TD_TowerSnap[] towerSnaps = new TD_TowerSnap[TD_Rules.Slots.Length];
        readonly TD_SlotView[] slotViews = new TD_SlotView[TD_Rules.Slots.Length];
        readonly List<Vector3> slotPositions = new(TD_Rules.Slots);
        readonly Stack<LineRenderer> tracerPool = new();
        readonly List<(LineRenderer line, float until)> liveTracers = new();
        int lastBase = TD_Rules.BaseHealth;
        TD_Phase lastPhase;

        void Awake() {
            map = GetComponentInParent<TD_Map>();
            toastLabel.text = bannerLabel.text = promptLabel.text = headlineLabel.text = "";
            waveLabel.text = "TOWER DEFENSE";
            cashLabel.text = baseLabel.text = "";
        }

        void Start() {
            for (int i = 0; i < TD_Rules.Slots.Length; i++) {
                var go = Instantiate(map.slotPrefab, TD_Rules.Slots[i], Quaternion.identity, map.transform);
                go.name = $"Slot {i + 1}";
                slotViews[i] = go.GetComponent<TD_SlotView>();
            }
            RefreshTowerBar();
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

        void OnDestroy() {
            if (manager == null) return;
            manager.UnregisterBroadcast<TD_StateBroadcast>(OnState);
            manager.UnregisterBroadcast<TD_SnapshotBroadcast>(OnSnapshot);
            manager.UnregisterBroadcast<TD_ResultBroadcast>(OnResult);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<TD_StateBroadcast>(OnState);
                manager.RegisterBroadcast<TD_SnapshotBroadcast>(OnSnapshot);
                manager.RegisterBroadcast<TD_ResultBroadcast>(OnResult);
            }
            if (manager != null && manager.Started && !hasState && Time.unscaledTime > nextRequest) {
                nextRequest = Time.unscaledTime + 2f;
                manager.Broadcast(new TD_StateRequest());
            }
            if (toastLabel.text.Length > 0 && Time.unscaledTime > toastUntil) toastLabel.text = "";
            MoveEnemies(Time.deltaTime);
            UpdateTracers();
            UpdateFocus();
        }

        // ---------------------------------------------------------------- input
        void Select(int key) {
            if (key < 1 || key > TD_Rules.Towers.Length) return;
            selected = key - 1;
            RefreshTowerBar();
        }

        void ToggleReady() {
            if (manager == null || !hasState || state.phase != TD_Phase.Build) return;
            ready = !ready;
            manager.Broadcast(new TD_ReadyRequest { ready = ready });
            Toast(ready ? "Ready! Waiting for the others..." : "Not ready.", null, 1.5f);
        }

        void Interact() {
            UpdateFocus();
            if (focusedSlot < 0 || manager == null || !manager.Started) return;
            manager.Broadcast(new TD_BuildRequest { slot = (byte)focusedSlot, kind = (TD_TowerKind)selected });
        }

        void UpdateFocus() {
            var character = LocalPlayer.Character;
            var cam = Camera.main;
            int previous = focusedSlot;
            focusedSlot = -1;
            if (character != null && !character.IsDead && cam != null && hasState)
                focusedSlot = FocusPicker.Pick(cam.transform.position, cam.transform.forward, slotPositions, TD_Rules.SlotReach, 0.2f, body: character.transform.position);
            if (focusedSlot != previous) {
                if (previous >= 0 && slotViews[previous] != null) slotViews[previous].pad.sharedMaterial = slotViews[previous].idle;
                if (focusedSlot >= 0 && slotViews[focusedSlot] != null) slotViews[focusedSlot].pad.sharedMaterial = slotViews[focusedSlot].focused;
            }
            promptLabel.text = focusedSlot < 0 ? "" : Prompt(focusedSlot);
        }

        string Prompt(int slot) {
            if (towerViews[slot] != null) {
                var t = towerSnaps[slot];
                var stats = TD_Rules.Tower(t.kind);
                if (t.level >= TD_Rules.MaxLevel) return $"{stats.name} (max level)  by {t.owner}";
                int cost = TD_Rules.UpgradeCost(t.kind, t.level);
                return $"[E] Upgrade {stats.name} to level {t.level + 1}  <color={(state.cash >= cost ? "#7CFF8A" : "#FF7A6B")}>${cost}</color>";
            }
            var pick = TD_Rules.Towers[selected];
            return $"[E] Build {pick.name}  <color={(state.cash >= pick.cost ? "#7CFF8A" : "#FF7A6B")}>${pick.cost}</color>   <size=70%>(1-4 to change)</size>";
        }

        // ---------------------------------------------------------------- state
        void OnState(TD_StateBroadcast message, Channel channel) {
            bool first = !hasState;
            state = message;
            hasState = true;
            if (message.phase != TD_Phase.Build) ready = false;
            if (!first && message.phase == TD_Phase.Wave && lastPhase != TD_Phase.Wave) Toast($"WAVE {message.wave}!", waveClip, 2f);
            if (!first && message.baseHealth < lastBase) Toast($"The base took {lastBase - message.baseHealth} damage!", leakClip, 1.5f);
            lastBase = message.baseHealth;
            lastPhase = message.phase;
            ApplyTowers(message.towers);
            RefreshHud();
            RefreshTowerBar();
        }

        void ApplyTowers(TD_TowerSnap[] towers) {
            var present = new bool[towerViews.Length];
            if (towers != null)
                foreach (var t in towers) {
                    if (t.slot >= towerViews.Length) continue;
                    present[t.slot] = true;
                    var view = towerViews[t.slot];
                    if (view == null || towerSnaps[t.slot].kind != t.kind) {
                        if (view != null) Destroy(view.gameObject);
                        var go = Instantiate(map.towerPrefabs[(int)t.kind], TD_Rules.Slots[t.slot], Quaternion.identity, map.transform);
                        view = towerViews[t.slot] = go.GetComponent<TD_TowerView>();
                    }
                    towerSnaps[t.slot] = t;
                    if (view.label != null) view.label.text = $"{TD_Rules.Tower(t.kind).name}  L{t.level}\n<size=70%>{t.owner}</size>";
                    if (view.levelPips != null) for (int i = 0; i < view.levelPips.Length; i++) view.levelPips[i].SetActive(i < t.level);
                    view.transform.localScale = Vector3.one * (1f + 0.08f * (t.level - 1));
                }
            for (int i = 0; i < towerViews.Length; i++) {
                if (!present[i] && towerViews[i] != null) { Destroy(towerViews[i].gameObject); towerViews[i] = null; }
                if (slotViews[i] != null && slotViews[i].label != null) slotViews[i].label.gameObject.SetActive(!present[i]);
            }
        }

        void RefreshHud() {
            waveLabel.text = state.phase switch {
                TD_Phase.Waiting => "Waiting for players",
                TD_Phase.Build => $"WAVE {state.wave}/{TD_Rules.WaveCount} in {state.secondsLeft}s" + (ready ? "  <color=#7CFF8A>READY</color>" : "  <size=70%>[R] ready</size>"),
                TD_Phase.Wave => $"WAVE {state.wave}/{TD_Rules.WaveCount}   {state.enemiesLeft} enemies left",
                TD_Phase.Victory => "VICTORY",
                _ => "DEFEAT"
            };
            cashLabel.text = $"${state.cash}";
            baseLabel.text = $"BASE {state.baseHealth}/{TD_Rules.BaseHealth}";
            baseFill.fillAmount = Mathf.Clamp01(state.baseHealth / (float)TD_Rules.BaseHealth);
            headlineLabel.text = state.headline;
            bannerLabel.text = state.phase switch {
                TD_Phase.Victory => $"VICTORY!\n<size=40%>New game in {state.secondsLeft}</size>",
                TD_Phase.Defeat => $"BASE DESTROYED\n<size=40%>Reached wave {state.wave}. New game in {state.secondsLeft}</size>",
                _ => ""
            };
        }

        void RefreshTowerBar() {
            for (int i = 0; i < towerButtons.Length && i < TD_Rules.Towers.Length; i++) {
                var t = TD_Rules.Towers[i];
                string colour = hasState && state.cash >= t.cost ? "#FFFFFF" : "#FF9A8F";
                towerButtons[i].text = $"<b>{i + 1}  {t.name}</b>  <color={colour}>${t.cost}</color>\n<size=70%>{t.blurb}</size>";
                towerButtonBacks[i].color = i == selected ? selectedColor : idleColor;
            }
        }

        void OnResult(TD_ResultBroadcast message, Channel channel) => Toast(message.message, message.accepted ? buildClip : deniedClip, 2.2f);

        // ---------------------------------------------------------------- snapshots
        void OnSnapshot(TD_SnapshotBroadcast message, Channel channel) {
            snapshotAt = Time.time;
            seen.Clear();
            if (message.enemies != null)
                foreach (var e in message.enemies) {
                    seen.Add(e.id);
                    if (!enemyViews.TryGetValue(e.id, out var view)) {
                        view = Rent(e.kind);
                        view.shownDistance = e.distance;
                        enemyViews[e.id] = view;
                        enemyKinds[e.id] = e.kind;
                    }
                    view.targetDistance = e.distance;
                    view.speed = TD_Rules.Enemy(e.kind).speed * (e.slowed ? 0.6f : 1f);
                    if (view.healthFill != null) view.healthFill.localScale = new Vector3(e.health / 255f, 1f, 1f);
                    if (view.slowTint != null && view.slowTint.gameObject.activeSelf != e.slowed) view.slowTint.gameObject.SetActive(e.slowed);
                }
            var gone = new List<ushort>();
            foreach (var id in enemyViews.Keys) if (!seen.Contains(id)) gone.Add(id);
            var killed = message.killed != null ? new HashSet<ushort>(message.killed) : null;
            foreach (var id in gone) {
                var view = enemyViews[id];
                if (map.explosionPrefab != null && view.seen && (killed == null || killed.Contains(id)))
                    Destroy(Instantiate(map.explosionPrefab, view.transform.position, Quaternion.identity, map.transform), 2f);
                Return(enemyKinds[id], view);
                enemyViews.Remove(id);
                enemyKinds.Remove(id);
            }
            if (message.shots != null) foreach (var shot in message.shots) DrawShot(shot);
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
            }
        }

        void DrawShot(TD_Shot shot) {
            if (shot.slot >= towerViews.Length || towerViews[shot.slot] == null || !enemyViews.TryGetValue(shot.enemy, out var target)) return;
            var tower = towerViews[shot.slot];
            Vector3 aim = target.transform.position + Vector3.up * 1f;
            if (tower.head != null) {
                Vector3 flat = aim - tower.head.position; flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f) tower.head.rotation = Quaternion.LookRotation(flat);
            }
            Vector3 from = tower.muzzle != null ? tower.muzzle.position : tower.transform.position + Vector3.up * 3f;
            var line = tracerPool.Count > 0 ? tracerPool.Pop() : Instantiate(map.tracerPrefab, map.transform).GetComponent<LineRenderer>();
            line.gameObject.SetActive(true);
            line.SetPosition(0, from);
            line.SetPosition(1, aim);
            var kind = towerSnaps[shot.slot].kind;
            Color c = kind switch { TD_TowerKind.Frost => new Color(0.5f, 0.9f, 1f), TD_TowerKind.Cannon => new Color(1f, 0.6f, 0.2f), TD_TowerKind.Sniper => new Color(1f, 0.3f, 0.3f), _ => new Color(1f, 0.95f, 0.5f) };
            line.startColor = line.endColor = c;
            line.widthMultiplier = kind == TD_TowerKind.Cannon ? 0.35f : kind == TD_TowerKind.Sniper ? 0.18f : 0.1f;
            liveTracers.Add((line, Time.time + (kind == TD_TowerKind.Sniper ? 0.15f : 0.07f)));
            if (kind == TD_TowerKind.Cannon && map.explosionPrefab != null)
                Destroy(Instantiate(map.explosionPrefab, target.transform.position, Quaternion.identity, map.transform), 1.5f);
            if (tower.fireSound != null) tower.fireSound.Play();
        }

        void UpdateTracers() {
            for (int i = liveTracers.Count - 1; i >= 0; i--) {
                if (Time.time < liveTracers[i].until) continue;
                liveTracers[i].line.gameObject.SetActive(false);
                tracerPool.Push(liveTracers[i].line);
                liveTracers.RemoveAt(i);
            }
        }

        void Toast(string message, AudioClip clip, float seconds) {
            toastLabel.text = message;
            toastUntil = Time.unscaledTime + seconds;
            if (sfx != null && clip != null) sfx.PlayOneShot(clip);
        }
    }
}
