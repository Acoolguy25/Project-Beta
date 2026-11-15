using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Input;
using RyanAssets.Rounds;
using UnityEngine;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// Draws the defence from server snapshots (enemies, towers, shots), feeds the <see cref="TD_Hud"/>
    /// and sends build, upgrade and ready requests. Keys: 1-4 pick a tower, E builds or upgrades the
    /// pad in front of you, R votes to start the next wave early.
    /// </summary>
    [RequireComponent(typeof(TD_Hud))]
    public sealed class TD_ClientController : MonoBehaviour {
        public AudioSource sfx;
        public AudioClip buildClip, deniedClip, leakClip, waveClip;
        [Tooltip("How long the head of a tower keeps facing its last target before idling.")]
        [Min(0f)] public float idleSweepDelay = 2f;
        [Tooltip("Resting height of the floating build markers above their pads.")]
        public float markerHeight = 2.6f;

        TD_Hud hud;
        TD_Map map;
        TD_WorldFx fx;
        ClientManager manager;
        TD_StateBroadcast state;
        bool hasState, ready;
        int selected, focusedSlot = -1;
        float nextRequest, snapshotAt;
        readonly Dictionary<ushort, TD_EnemyView> enemyViews = new();
        readonly Dictionary<ushort, TD_EnemyKind> enemyKinds = new();
        readonly Dictionary<TD_EnemyKind, Stack<TD_EnemyView>> enemyPool = new();
        readonly HashSet<ushort> seen = new();
        readonly List<ushort> gone = new();
        readonly TD_TowerView[] towerViews = new TD_TowerView[TD_Rules.Slots.Length];
        readonly TD_TowerSnap[] towerSnaps = new TD_TowerSnap[TD_Rules.Slots.Length];
        readonly float[] towerLastShot = new float[TD_Rules.Slots.Length];
        readonly TD_SlotView[] slotViews = new TD_SlotView[TD_Rules.Slots.Length];
        readonly List<Vector3> slotPositions = new(TD_Rules.Slots);
        MaterialPropertyBlock block;
        int lastBase = TD_Rules.BaseHealth;
        TD_Phase lastPhase;
        int bannerWave;

        void Awake() {
            hud = GetComponent<TD_Hud>();
            block = new MaterialPropertyBlock();
            map = GetComponentInParent<TD_Map>();
            fx = new TD_WorldFx(map.effects, map.transform);
        }

        void Start() {
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
            MoveEnemies(Time.deltaTime);
            TurnTowers(Time.deltaTime);
            BobMarkers();
            fx.Update();
            UpdateFocus();
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
            hud.ShowState(state, ready);
            hud.Toast(ready ? "Ready! Waiting for the others..." : "Not ready", TD_Hud.ToastKind.Good, 1.5f);
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
            if (focusedSlot == previous) return;
            if (previous >= 0) Highlight(previous, false);
            if (focusedSlot >= 0) Highlight(focusedSlot, true);
            RefreshFocusCard();
        }

        void Highlight(int slot, bool on) {
            var pad = slotViews[slot];
            if (pad != null) {
                pad.focused = on;
                pad.Refresh();
                bool empty = towerViews[slot] == null;
                if (pad.rangeRing != null) {
                    pad.rangeRing.gameObject.SetActive(on && empty);
                    if (on && empty) SetRing(pad.rangeRing, TD_Rules.Range((TD_TowerKind)selected, 1), TD_Palette.Tower((TD_TowerKind)selected));
                }
            }
            var tower = towerViews[slot];
            if (tower != null && tower.rangeRing != null) {
                tower.rangeRing.gameObject.SetActive(on);
                if (on) SetRing(tower.rangeRing, TD_Rules.Range(towerSnaps[slot].kind, towerSnaps[slot].level), TD_Palette.Tower(towerSnaps[slot].kind));
            }
        }

        /// <summary>Lays a range ring out flat around its parent at <paramref name="radius"/> metres.</summary>
        static void SetRing(LineRenderer ring, float radius, Color color) {
            int n = ring.positionCount;
            Vector3 centre = ring.transform.position;
            for (int i = 0; i < n; i++) {
                float a = i * Mathf.PI * 2f / n;
                ring.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * radius, 0.15f, Mathf.Sin(a) * radius));
            }
            color.a = 0.85f;
            ring.startColor = ring.endColor = color;
        }

        void RefreshFocusCard() {
            if (focusedSlot < 0 || !hasState) hud.ClearFocus();
            else if (towerViews[focusedSlot] != null) hud.ShowFocusTower(towerSnaps[focusedSlot], state.cash);
            else hud.ShowFocusPad((TD_TowerKind)selected, state.cash);
        }

        // ---------------------------------------------------------------- state
        void OnState(TD_StateBroadcast message, Channel channel) {
            bool first = !hasState;
            state = message;
            hasState = true;
            if (message.phase != TD_Phase.Build) ready = false;
            if (!first && message.phase == TD_Phase.Wave && message.wave != bannerWave) {
                bannerWave = message.wave;
                hud.Banner($"WAVE {message.wave}", Describe(message.wave));
                Play(waveClip);
            }
            if (!first && message.phase == TD_Phase.Build && lastPhase == TD_Phase.Wave && message.wave > 1)
                hud.Toast($"Wave {message.wave - 1} cleared!  +${TD_Rules.WaveBonus(message.wave - 1)}", TD_Hud.ToastKind.Money, 2.5f);
            if (!first && message.baseHealth < lastBase) {
                hud.Toast($"The base took {lastBase - message.baseHealth} damage!", TD_Hud.ToastKind.Bad, 1.5f);
                Play(leakClip);
            }
            lastBase = message.baseHealth;
            lastPhase = message.phase;
            if (message.phase is TD_Phase.Victory or TD_Phase.Defeat) bannerWave = 0; // a new game starts again at wave 1
            ApplyTowers(message.towers);
            hud.ShowState(message, ready);
            hud.ShowShop(selected, message.cash, true);
            RefreshFocusCard();
        }

        static string Describe(int wave) {
            var counts = new int[TD_Rules.Enemies.Length];
            foreach (var g in TD_Rules.Wave(wave)) counts[(int)g.kind] += g.count;
            var parts = new List<string>();
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] > 0) parts.Add($"{counts[i]} {TD_Rules.Enemies[i].name}");
            return string.Join("  ·  ", parts);
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
                        Vector3 top = TD_Rules.Slots[t.slot] + Vector3.up * (slotViews[t.slot] != null ? slotViews[t.slot].topHeight : 0f);
                        var go = Instantiate(map.towerPrefabs[(int)t.kind], top, Quaternion.identity, map.transform);
                        view = towerViews[t.slot] = go.GetComponent<TD_TowerView>();
                        view.aim = view.head != null ? view.head.rotation : Quaternion.identity;
                        if (t.slot == focusedSlot) Highlight(t.slot, true);
                    }
                    towerSnaps[t.slot] = t;
                    if (view.label != null) view.label.text = t.owner ?? "";
                    if (view.levelStars != null) for (int i = 0; i < view.levelStars.Length; i++) view.levelStars[i].SetActive(i < t.level);
                    view.transform.localScale = Vector3.one * (1f + 0.08f * (t.level - 1));
                }
            for (int i = 0; i < towerViews.Length; i++) {
                if (!present[i] && towerViews[i] != null) { Destroy(towerViews[i].gameObject); towerViews[i] = null; }
                var pad = slotViews[i];
                if (pad == null) continue;
                if (pad.marker != null) pad.marker.gameObject.SetActive(!present[i]);
                pad.built = present[i];
                if (present[i]) pad.builtColor = TD_Palette.Tower(towerSnaps[i].kind);
                pad.Refresh();
            }
        }

        void OnResult(TD_ResultBroadcast message, Channel channel) {
            hud.Toast(message.message, message.accepted ? TD_Hud.ToastKind.Good : TD_Hud.ToastKind.Bad, 2.2f);
            Play(message.accepted ? buildClip : deniedClip);
        }

        void Play(AudioClip clip) {
            if (sfx != null && clip != null) sfx.PlayOneShot(clip);
        }

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
                        view.health = 1f;
                        enemyViews[e.id] = view;
                        enemyKinds[e.id] = e.kind;
                    }
                    view.targetDistance = e.distance;
                    view.speed = TD_Rules.Enemy(e.kind).speed * (e.slowed ? 0.6f : 1f);
                    SetHealth(view, e.health / 255f);
                    if (view.slowTint != null && view.slowTint.gameObject.activeSelf != e.slowed) view.slowTint.gameObject.SetActive(e.slowed);
                }
            gone.Clear();
            foreach (var id in enemyViews.Keys) if (!seen.Contains(id)) gone.Add(id);
            var killed = message.killed != null ? new HashSet<ushort>(message.killed) : null;
            foreach (var id in gone) {
                var view = enemyViews[id];
                var kind = enemyKinds[id];
                if (view.seen && (killed == null || killed.Contains(id))) fx.Destroyed(view.transform.position, TD_Rules.Enemy(kind).reward);
                Return(kind, view);
                enemyViews.Remove(id);
                enemyKinds.Remove(id);
            }
            if (message.shots != null) foreach (var shot in message.shots) DrawShot(shot);
        }

        void SetHealth(TD_EnemyView view, float fraction) {
            if (fraction < view.health - 0.001f) view.hitFlash = 1f;
            view.health = fraction;
            if (view.healthFill != null) view.healthFill.localScale = new Vector3(Mathf.Max(0.001f, fraction), 1f, 1f);
            if (view.healthRenderer != null) {
                view.healthRenderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", TD_Palette.Health(fraction));
                view.healthRenderer.SetPropertyBlock(block);
            }
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
                if (view.body != null) {
                    // A small shudder when hit sells the impact without per-enemy materials.
                    view.hitFlash = Mathf.MoveTowards(view.hitFlash, 0f, dt * 6f);
                    float shake = view.hitFlash * 0.12f;
                    view.body.localPosition = new Vector3(Mathf.Sin(Time.time * 80f) * shake, 0f, 0f);
                }
            }
        }

        void BobMarkers() {
            for (int i = 0; i < slotViews.Length; i++) {
                var marker = slotViews[i] != null ? slotViews[i].marker : null;
                if (marker == null || !marker.gameObject.activeSelf) continue;
                Vector3 p = marker.localPosition;
                p.y = markerHeight + Mathf.Sin(Time.time * 2.2f + i * 0.7f) * 0.25f;
                marker.localPosition = p;
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
            Vector3 aim = target.transform.position + Vector3.up * 1f;
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
            Vector3 from = tower.muzzle != null ? tower.muzzle.position : tower.transform.position + Vector3.up * 3f;
            fx.Shot(towerSnaps[shot.slot].kind, from, target.transform, aim);
            if (tower.fireSound != null) tower.fireSound.Play();
        }
    }
}
