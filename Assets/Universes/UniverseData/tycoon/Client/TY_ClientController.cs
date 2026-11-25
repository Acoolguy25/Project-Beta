using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.tycoon.Client {
    /// <summary>
    /// Draws Brick Tycoon from server state: who owns each plot, bought items, lane buy pads,
    /// collector totals, gold coins, the local player's daily chest and the bricks that ride each belt,
    /// and feeds the HUD and the game feel. Bricks are cosmetic; cash is counted on the server.
    /// </summary>
    public sealed class TY_ClientController : MonoBehaviour {
        [Tooltip("The HUD on this prefab.")]
        public TY_Hud hud;
        [Tooltip("Sounds, particles and animations on this prefab.")]
        public TY_Feedback feedback;
        [Tooltip("Most bricks a belt shows at once.")]
        [SerializeField, Min(8)] int maxBricksPerBelt = 80;
        [Tooltip("Height above the belt where a dropped brick appears.")]
        [SerializeField, Min(0f)] float brickDropHeight = 2.2f;
        [Tooltip("How much bigger a brick gets per upgrader it passes (0.1 = 10%).")]
        [SerializeField, Min(0f)] float brickGrowthPerStage = 0.12f;
        [Tooltip("Smallest centre-to-centre gap between bricks on the belt, in metres; keep it above a fully grown brick's length "
            + "so bricks never touch. A due dropper waits for a gap. Visual only: income is counted on the server.")]
        [SerializeField, Min(0.1f)] float minBrickSpacing = 1.4f;
        [Tooltip("Share (0..1) of the belt's room the droppers may fill. When the owned droppers together want more bricks than that, "
            + "every dropper's visual interval is stretched by the same factor, so all of them keep dropping in proportion.")]
        [SerializeField, Range(0.1f, 1f)] float beltFill = 0.75f;

        [Header("Brick glow")]
        [Tooltip("Strength (0..1) of the pool of light under a brick per upgrade stage (0 = raw), in that stage's colour; 0 hides it. "
            + "It lies on the dark belt, so upgraded bricks stand out without bloom.")]
        [SerializeField] float[] brickGlowStrength = { 0f, 1f, 1f, 1f };

        [Header("Belt texture")]
        [Tooltip("+1 or -1: whichever makes the belt's arrows move with the bricks.")]
        [SerializeField] float beltScrollDirection = 1f;

        [Header("Auto-collector")]
        [Tooltip("Seconds between the '+$' popups of an auto-collecting plot.")]
        [SerializeField, Min(0.5f)] float autoPopupSeconds = 3f;

        static TY_ItemKind[] PadLanes => TY_Lanes.PadLanes;

        sealed class Ore { public Transform t; public TY_OreView view; public float distance; public int stage; public float scale; }

        sealed class PlotVisual {
            public TY_PlotState state;
            public readonly GameObject[] items = new GameObject[TY_Catalog.Count];
            public readonly TY_ButtonView[] pads = new TY_ButtonView[PadLanes.Length];
            public readonly float[] dropTimers = new float[TY_Catalog.Count];
#if UNITY_EDITOR
            /// <summary>Editor-only diagnostics: bricks dropped per item on this plot (read by test probes).</summary>
            public readonly int[] dropCounts = new int[TY_Catalog.Count];
#endif
            public readonly List<Ore> ores = new();
            public GameObject golden;
            public FxHandle beacon;
            /// <summary>The daily chest: shown on the local player's own plot only.</summary>
            public TY_ChestView chest;
            public bool hasState;
        }

        static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

        TY_Map map;
        ClientManager manager;
        PlotVisual[] visuals;
        readonly Stack<Ore> orePool = new();
        TY_WalletBroadcast wallet;
        bool hasWallet, hasPlots;
        float nextRequest;
        float[] upgraderDistances;
        int[] upgraderItems;
        Color[] oreTints;
        MaterialPropertyBlock beltBlock;
        Vector4 beltTiling;
        float beltOffset, beltOffsetPerSecond;
        long autoEarned;
        float nextAutoPopup;
        float brickHalfHeight;
        /// <summary>Unscaled time when the daily chest comes back (from the last wallet).</summary>
        float chestBackAt;

        void Awake() {
            map = GetComponentInParent<TY_Map>();
            var distances = new List<float>();
            var items = new List<int>();
            for (int i = 0; i < TY_Catalog.Count; i++)
                if (TY_Catalog.Items[i].kind == TY_ItemKind.Upgrader) { distances.Add(TY_Catalog.BeltDistance(TY_Catalog.Items[i].itemPosition)); items.Add(i); }
            upgraderDistances = distances.ToArray();
            upgraderItems = items.ToArray();
            oreTints = new Color[map.oreMaterials?.Length ?? 0];
            for (int i = 0; i < oreTints.Length; i++)
                oreTints[i] = map.oreMaterials[i] != null && map.oreMaterials[i].HasProperty("_BaseColor") ? map.oreMaterials[i].GetColor("_BaseColor") : Color.white;
            brickHalfHeight = map.orePrefab != null ? HalfHeight(map.orePrefab.GetComponent<TY_OreView>()) : 0f;
        }

        /// <summary>Distance from the brick's pivot down to its bottom face, read from the authored brick mesh.</summary>
        static float HalfHeight(TY_OreView view) {
            var filter = view != null && view.body != null ? view.body.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null) return 0f;
            var body = view.body.transform;
            return filter.sharedMesh.bounds.extents.y * body.localScale.y - body.localPosition.y;
        }

        TY_BuildPreview preview;

        void Start() {
            preview = gameObject.AddComponent<TY_BuildPreview>();
            preview.Setup(map.ghostMaterial, PadLanes.Length);
            visuals = new PlotVisual[map.PlotCount];
            for (int i = 0; i < visuals.Length; i++) {
                var v = visuals[i] = new PlotVisual();
                var view = map.plots[i];
                for (int lane = 0; lane < PadLanes.Length; lane++) {
                    var go = Instantiate(map.padPrefabs[(int)PadLanes[lane]], view.itemRoot);
                    go.transform.localPosition = TY_Catalog.LanePosition(PadLanes[lane]);
                    go.transform.localRotation = Quaternion.identity;
                    go.name = $"Pad {TY_Lanes.Title(PadLanes[lane])}";
                    v.pads[lane] = go.GetComponent<TY_ButtonView>();
                    go.SetActive(false);
                }
                if (map.goldCoinPrefab != null) {
                    v.golden = Instantiate(map.goldCoinPrefab, view.itemRoot);
                    v.golden.SetActive(false);
                }
                if (map.chestPrefab != null) {
                    var chest = Instantiate(map.chestPrefab, view.itemRoot);
                    chest.transform.localPosition = TY_Catalog.ChestPosition;
                    chest.transform.localRotation = Quaternion.identity;
                    v.chest = chest.GetComponent<TY_ChestView>();
                    chest.SetActive(false);
                }
                ApplyPlot(i, new TY_PlotState { plot = (byte)i, ownerId = -1 }, false);
            }
            SetUpBelt();
            hud.offlineCard.Closed += feedback.OfflineCollected;
        }

        void OnDestroy() {
            if (hud != null && hud.offlineCard != null && feedback != null) hud.offlineCard.Closed -= feedback.OfflineCollected;
            // The game-feel kit outlives this scene: stop the coin beacons.
            if (visuals != null) foreach (var v in visuals) v.beacon.Stop(true);
            if (manager == null) return;
            manager.UnregisterBroadcast<TY_PlotsBroadcast>(OnPlots);
            manager.UnregisterBroadcast<TY_WalletBroadcast>(OnWallet);
            manager.UnregisterBroadcast<TY_EventBroadcast>(OnEvent);
        }

        void Update() {
            if (manager == null && InstanceFinder.ClientManager != null) {
                manager = InstanceFinder.ClientManager;
                manager.RegisterBroadcast<TY_PlotsBroadcast>(OnPlots);
                manager.RegisterBroadcast<TY_WalletBroadcast>(OnWallet);
                manager.RegisterBroadcast<TY_EventBroadcast>(OnEvent);
            }
            if (manager != null && manager.Started && !hasPlots && Time.unscaledTime > nextRequest) {
                nextRequest = Time.unscaledTime + 2f;
                manager.Broadcast(new TY_StateRequest());
            }
            if (visuals == null) return;
            int local = LocalClientId;
            for (int i = 0; i < visuals.Length; i++) AnimateOre(i, Time.deltaTime, visuals[i].state.ownerId == local);
            ScrollBelts(Time.deltaTime);
            PopAutoEarnings();
            RefreshChest();
        }

        // ---------------------------------------------------------------- state
        void OnPlots(TY_PlotsBroadcast message, Channel channel) {
            if (message.plots == null || visuals == null) return;
            // The first snapshot after joining is drawn as it is; later changes are animated.
            bool live = hasPlots;
            hasPlots = true;
            foreach (var state in message.plots)
                if (state.plot < visuals.Length) ApplyPlot(state.plot, state, live);
            RefreshHud();
        }

        void OnWallet(TY_WalletBroadcast message, Channel channel) {
            if (hasWallet && message.plot != wallet.plot) ResetAutoEarnings();
            else if (hasWallet && message.plot >= 0 && message.plot == wallet.plot && message.plot < visuals.Length
                && TY_Catalog.AutoCollects(visuals[message.plot].state.purchased))
                autoEarned += message.lifetime - wallet.lifetime;
            wallet = message;
            hasWallet = true;
            chestBackAt = Time.unscaledTime + message.chestSeconds;
            RefreshPads();
            RefreshHud();
        }

        void OnEvent(TY_EventBroadcast e, Channel channel) {
            hud.ShowEvent(e);
            bool onPlot = e.plot >= 0 && e.plot < visuals.Length;
            switch (e.kind) {
                case TY_EventKind.Bought when onPlot && IsLandmark(e.item): LandmarkBuilt(e.plot, e.item); break;
                case TY_EventKind.Bought: feedback.Bought(); break;
                case TY_EventKind.Claimed when onPlot: feedback.Claimed(map.World(e.plot, TY_Catalog.ClaimPosition)); break;
                case TY_EventKind.TooPoor: feedback.Denied(); break;
                case TY_EventKind.Collected when onPlot:
                    feedback.Collected(map.World(e.plot, TY_Catalog.CollectPosition), e.amount);
                    hud.PunchCash();
                    break;
                case TY_EventKind.GoldenBrick when onPlot:
                    feedback.CoinGrabbed(map.World(e.plot, visuals[e.plot].state.goldenPosition), e.amount);
                    hud.PunchCash();
                    // Already shown: keep it out of the auto-collector's next popup.
                    if (TY_Catalog.AutoCollects(visuals[e.plot].state.purchased)) autoEarned -= e.amount;
                    break;
                case TY_EventKind.Rebirth:
                    // Earnings from before the rebirth must not pop up over the torn-down plot.
                    ResetAutoEarnings();
                    feedback.RebirthScreen((int)e.amount);
                    break;
                case TY_EventKind.Perk: feedback.Perk(); break;
                case TY_EventKind.CollectorFull: feedback.CollectorFull(); break;
                // The server sends wallets (with its multiplier) before this event.
                case TY_EventKind.RushHour: feedback.RushStarted(hasWallet ? wallet.rushMultiplier : 0f); break;
                case TY_EventKind.RushHourOver: feedback.RushEnded(); break;
                case TY_EventKind.Goal: feedback.GoalCompleted(e.extra > 0); break;
                case TY_EventKind.DailyChest when onPlot:
                    feedback.ChestOpened(map.World(e.plot, TY_Catalog.ChestPosition), e.amount);
                    hud.PunchCash();
                    break;
                case TY_EventKind.OfflineEarnings: feedback.WelcomeBack(); break;
            }
        }

        static bool IsLandmark(int item) => item >= 0 && item < TY_Catalog.Count && TY_Catalog.Items[item].kind == TY_ItemKind.Landmark;

        /// <summary>
        /// A landmark is the reward of a rebirth milestone: a banner and fanfare as it lands, and the Brick
        /// Rocket (the last one) takes off once it has landed, then comes back to its pad.
        /// </summary>
        void LandmarkBuilt(int plot, int item) {
            TY_Item data = TY_Catalog.Items[item];
            feedback.LandmarkBuilt(map.World(plot, data.itemPosition), data.name);
            // The event comes before the plot update that places the model: launch once it is there.
            StartCoroutine(LaunchWhenPlaced(plot, item));
        }

        System.Collections.IEnumerator LaunchWhenPlaced(int plot, int item) {
            for (float waited = 0f; waited < 5f; waited += Time.deltaTime) {
                GameObject built = visuals[plot].items[item];
                if (built != null) {
                    if (built.TryGetComponent(out TY_RocketView rocket)) feedback.LaunchRocket(rocket);
                    yield break;
                }
                yield return null;
            }
        }

        int LocalClientId => manager != null && manager.Connection != null ? manager.Connection.ClientId : -2;

        /// <param name="live">Animate what changed (false for the first state drawn after joining).</param>
        void ApplyPlot(int index, TY_PlotState state, bool live) {
            var v = visuals[index];
            var view = map.plots[index];
            TY_PlotState previous = v.state;
            bool ownerChanged = !v.hasState || previous.ownerId != state.ownerId;
            v.state = state;
            v.hasState = true;
            bool owned = state.ownerId >= 0;
            bool mine = owned && state.ownerId == LocalClientId;
            // A claim drops the claimer's saved build in; a rebirth tears the old build down and rebuilds.
            bool claimed = live && owned && ownerChanged && previous.ownerId < 0;
            bool reborn = live && owned && !ownerChanged && state.rebirths > previous.rebirths;
            bool animate = live && owned && (!ownerChanged || claimed);
            if (view.claimPad != null && view.claimPad.activeSelf == owned) view.claimPad.SetActive(!owned);
            if (view.ownerLabel != null)
                view.ownerLabel.text = owned
                    ? $"{state.ownerName}'s Tycoon" + (state.rebirths > 0 ? $"\n<size=60%><color=#FF9CF2>Rebirth {state.rebirths}</color></size>" : "")
                    : "FREE PLOT\n<size=60%>Step on the green pad to claim</size>";
            if (view.bankLabel != null)
                view.bankLabel.text = !owned ? ""
                    : TY_Catalog.AutoCollects(state.purchased) ? $"${TY_Catalog.Money(state.income)}/s\n<size=55%>auto-collecting</size>"
                    : $"${TY_Catalog.Money(state.bank)}<size=55%> / ${TY_Catalog.Money(state.capacity)}</size>\n<size=55%>{(state.bank >= state.capacity ? "<color=#FF6B6B>FULL! step here</color>" : "step here to collect")}</size>";
            if (reborn) feedback.RebirthWorld(map.World(index, Vector3.zero));
            int dropped = 0, torn = 0;
            // On a rebirth the starter collapses with the rest of the build and drops back in, so it shares the
            // tear-down spread; what the rebirth keeps (landmarks, a Head Office collector lane) stands still.
            ulong rebuilt = reborn ? previous.purchased & state.purchased & TY_Catalog.StarterMask : 0ul;
            int collapsing = TY_Catalog.OwnedCount(previous.purchased & ~state.purchased) + TY_Catalog.OwnedCount(rebuilt);
            float rebuildDelay = reborn ? feedback.rebuildDelay : 0f;
            for (int item = 0; item < TY_Catalog.Count; item++) {
                var data = TY_Catalog.Items[item];
                bool has = owned && TY_Catalog.Owns(state.purchased, item) && map.itemPrefabs[item] != null;
                if (has && v.items[item] == null) {
                    var go = Instantiate(map.itemPrefabs[item], view.itemRoot);
                    go.transform.localPosition = data.itemPosition;
                    go.transform.localRotation = Quaternion.Euler(0f, data.itemYaw, 0f);
                    go.name = data.name;
                    v.items[item] = go;
                    if (animate) DropIn(go.transform, data.itemPosition, dropped++);
                } else if (has && TY_Catalog.Owns(rebuilt, item)) {
                    // Restored by the rebirth: it collapses with the rest of the build, then drops back in.
                    int order = dropped++;
                    feedback.Rebuild(v.items[item].transform, data.itemPosition, feedback.TearDownDelay(torn++, collapsing),
                        rebuildDelay + feedback.DropDelay(order), mine && order == 0);
                } else if (!has && v.items[item] != null) {
                    if (animate) feedback.TearDown(v.items[item], feedback.TearDownDelay(torn++, collapsing));
                    else Destroy(v.items[item]);
                    v.items[item] = null;
                }
            }
            if (v.golden != null) {
                bool show = owned && state.golden;
                bool appeared = show && !v.golden.activeSelf;
                if (show) v.golden.transform.localPosition = state.goldenPosition;
                if (v.golden.activeSelf != show) v.golden.SetActive(show);
                if (appeared && mine && live) feedback.CoinAppeared(v.golden.transform.position);
                // A light pillar over your own coin, so it can be found from anywhere on the plot.
                bool beacon = show && mine;
                if (beacon && !v.beacon.IsPlaying) v.beacon = feedback.StartBeacon(v.golden.transform);
                else if (!beacon && v.beacon.IsPlaying) v.beacon.Stop();
            }
            if (!owned) ClearOre(v);
            if (v.chest != null && v.chest.gameObject.activeSelf != mine) v.chest.gameObject.SetActive(mine);
            RefreshPads(index);

            // Only the first of several drops shakes the camera, so a restored build doesn't rattle for seconds.
            void DropIn(Transform item, Vector3 rest, int order) =>
                feedback.DropIn(item, rest, rebuildDelay + feedback.DropDelay(order), mine && order == 0);
        }

        void RefreshPads() {
            if (visuals != null) for (int i = 0; i < visuals.Length; i++) RefreshPads(i);
        }

        void RefreshPads(int index) {
            var v = visuals[index];
            bool owned = v.state.ownerId >= 0;
            bool mine = owned && v.state.ownerId == LocalClientId;
            for (int lane = 0; lane < PadLanes.Length; lane++) {
                var pad = v.pads[lane];
                if (pad == null) continue;
                ulong purchased = v.state.purchased;
                int rebirths = v.state.rebirths;
                int item = TY_Catalog.PadItem(purchased, PadLanes[lane], rebirths);
                // Investments are the owner's own business: other players' plots do not show the pad.
                bool show = owned && item >= 0 && (mine || PadLanes[lane] != TY_ItemKind.Invest);
                if (pad.gameObject.activeSelf != show) pad.gameObject.SetActive(show);
                // Your own pads hover a see-through copy of what they would build, where it would stand.
                if (mine) preview.Set(lane, show ? item : -1, show ? map.itemPrefabs[item] : null, map.plots[index].itemRoot, pad.transform, TY_Lanes.Color(PadLanes[lane]));
                else preview.Release(lane, pad.transform);
                if (!show) continue;
                var data = TY_Catalog.Items[item];
                long price = TY_Catalog.Price(item, rebirths, mine && hasWallet ? wallet.invested : 0);
                bool locked = !TY_Catalog.Available(purchased, item, rebirths);
                TY_ButtonLook look = !mine ? TY_ButtonLook.Foreign
                    : locked ? TY_ButtonLook.Locked
                    : hasWallet && wallet.cash >= price ? TY_ButtonLook.Affordable
                    : TY_ButtonLook.Expensive;
                float saved = price <= 0 ? 1f : hasWallet ? Mathf.Clamp01((float)wallet.cash / price) : 0f;
                TY_ButtonLook before = pad.Look;
                pad.Show(data, price, TY_Catalog.Effect(item, rebirths), map.Picture(item), look, TY_Catalog.LockReason(purchased, item, rebirths), saved);
                if (before == TY_ButtonLook.Expensive && look == TY_ButtonLook.Affordable) feedback.PadAffordable(pad.transform);
            }
        }

        void RefreshHud() {
            if (!hasWallet) return;
            TY_PlotState? plot = wallet.plot >= 0 && wallet.plot < visuals.Length ? visuals[wallet.plot].state : null;
            hud.Refresh(wallet, plot, map);
        }

        // ---------------------------------------------------------------- bricks on the belt
        /// <param name="mine">The local player's plot: dropper ticks and upgrade sparkles play only there.</param>
        void AnimateOre(int index, float dt, bool mine) {
            var v = visuals[index];
            if (!v.hasState || v.state.ownerId < 0 || map.orePrefab == null) return;
            var plot = map.plots[index].transform;
            DropBricks(v, plot, dt, mine);
            float end = TY_Catalog.BeltEndX - TY_Catalog.BeltStartX + 1.5f;
            for (int i = v.ores.Count - 1; i >= 0; i--) {
                var ore = v.ores[i];
                ore.distance += TY_Catalog.BeltSpeed * dt;
                int stage = 0;
                for (int u = 0; u < upgraderDistances.Length; u++)
                    if (ore.distance >= upgraderDistances[u] && TY_Catalog.Owns(v.state.purchased, upgraderItems[u])) stage++;
                if (stage != ore.stage) {
                    // Each upgrader passed: the brick takes the next stage's colour and glow, grows, and sparkles.
                    bool upgraded = ore.stage >= 0 && stage > ore.stage;
                    ore.stage = stage;
                    int look = oreTints.Length > 0 ? Mathf.Min(stage, oreTints.Length - 1) : -1;
                    if (look >= 0) ore.view.body.sharedMaterial = map.oreMaterials[look];
                    ore.scale = 1f + brickGrowthPerStage * stage;
                    ore.t.localScale = Vector3.one * ore.scale;
                    SetGlow(ore, stage, look);
                    if (upgraded && mine) feedback.BrickUpgraded(ore.t.position, look >= 0 ? oreTints[look] : Color.white);
                }
                if (ore.distance >= end) {
                    ore.t.gameObject.SetActive(false);
                    orePool.Push(ore);
                    v.ores.RemoveAt(i);
                    continue;
                }
                // The brick grows about its centre, so lift it with its size: its bottom (and the light under it) stays on the belt.
                ore.t.position = plot.TransformPoint(new Vector3(TY_Catalog.BeltStartX + ore.distance, TY_Catalog.BeltTop + brickHalfHeight * ore.scale, TY_Catalog.BeltZ));
                ore.t.rotation = plot.rotation;
            }
        }

        /// <summary>
        /// Runs the droppers' visual timers and drops the bricks that are due. Every owned dropper must visibly drop, so:
        /// intervals are stretched by one common factor when the belt cannot fit them all (<see cref="DropStretch"/>), and a
        /// dropper may not drop while a dropper further down the belt has been waiting longer, because its brick would ride
        /// through that dropper's gap. Downstream droppers therefore cannot be starved by the cheap upstream ones.
        /// </summary>
        void DropBricks(PlotVisual v, Transform plot, float dt, bool mine) {
            float stretch = DropStretch(v.state.purchased);
            for (int item = 0; item < TY_Catalog.Count; item++) {
                if (!IsOwnedDropper(v, item)) continue;
                float interval = TY_Catalog.Items[item].interval * stretch;
                v.dropTimers[item] = Mathf.Min(v.dropTimers[item] + dt, interval * MaxDropBacklog);
            }
            for (int item = 0; item < TY_Catalog.Count; item++) {
                if (!IsOwnedDropper(v, item)) continue;
                var data = TY_Catalog.Items[item];
                float interval = data.interval * stretch;
                float waited = v.dropTimers[item] - interval;
                if (waited < 0f) continue;
                float drop = TY_Catalog.BeltDistance(data.itemPosition);
                if (!BeltClearAt(v, drop) || LongerWaitDownstream(v, item, drop, waited, stretch)) continue;
                v.dropTimers[item] -= interval;
                if (v.ores.Count >= maxBricksPerBelt) continue;
                v.ores.Add(SpawnOre(plot, drop));
#if UNITY_EDITOR
                v.dropCounts[item]++;
#endif
                if (mine) feedback.BrickDropped(plot.TransformPoint(data.itemPosition + Vector3.up * TY_Catalog.BeltTop));
            }
        }

        /// <summary>Most intervals a waiting dropper may fall behind; beyond that its missed bricks are skipped, not queued.</summary>
        const float MaxDropBacklog = 2f;

        static bool IsOwnedDropper(PlotVisual v, int item) =>
            TY_Catalog.Items[item].kind == TY_ItemKind.Dropper && TY_Catalog.Owns(v.state.purchased, item);

        /// <summary>1, or the factor that slows every dropper equally so their bricks fill at most <see cref="beltFill"/> of the belt.</summary>
        float DropStretch(ulong purchased) {
            float bricksPerSecond = 0f;
            for (int item = 0; item < TY_Catalog.Count; item++)
                if (TY_Catalog.Items[item].kind == TY_ItemKind.Dropper && TY_Catalog.Owns(purchased, item))
                    bricksPerSecond += 1f / TY_Catalog.Items[item].interval;
            float room = beltFill * TY_Catalog.BeltSpeed / minBrickSpacing;
            return bricksPerSecond > room ? bricksPerSecond / room : 1f;
        }

        /// <summary>True when a due dropper further down the belt than <paramref name="drop"/> has waited longer.</summary>
        bool LongerWaitDownstream(PlotVisual v, int item, float drop, float waited, float stretch) {
            for (int other = 0; other < TY_Catalog.Count; other++) {
                if (other == item || !IsOwnedDropper(v, other)) continue;
                var data = TY_Catalog.Items[other];
                float otherWaited = v.dropTimers[other] - data.interval * stretch;
                if (otherWaited > waited && TY_Catalog.BeltDistance(data.itemPosition) > drop) return true;
            }
            return false;
        }

        /// <summary>True when no brick on the belt is within <see cref="minBrickSpacing"/> of <paramref name="distance"/>.</summary>
        bool BeltClearAt(PlotVisual v, float distance) {
            foreach (var ore in v.ores)
                if (Mathf.Abs(ore.distance - distance) < minBrickSpacing) return false;
            return true;
        }

        /// <summary>Tints the light under the brick for its stage; raw bricks (strength 0) have none.</summary>
        void SetGlow(Ore ore, int stage, int look) {
            float strength = brickGlowStrength.Length > 0 ? brickGlowStrength[Mathf.Min(stage, brickGlowStrength.Length - 1)] : 0f;
            bool show = strength > 0f && look >= 0;
            ore.view.glow.enabled = show;
            if (show) {
                Color tint = oreTints[look];
                tint.a = strength;
                ore.view.glow.color = tint;
            }
        }

        Ore SpawnOre(Transform plot, float distance) {
            Ore ore = orePool.Count > 0 ? orePool.Pop() : null;
            if (ore == null) {
                var go = Instantiate(map.orePrefab, map.transform);
                ore = new Ore { t = go.transform, view = go.GetComponent<TY_OreView>() };
            }
            ore.distance = distance;
            ore.stage = -1;
            ore.scale = 1f;
            ore.t.gameObject.SetActive(true);
            ore.t.position = plot.TransformPoint(new Vector3(TY_Catalog.BeltStartX + distance, TY_Catalog.BeltTop + brickDropHeight, TY_Catalog.BeltZ));
            return ore;
        }

        void ClearOre(PlotVisual v) {
            foreach (var ore in v.ores) { ore.t.gameObject.SetActive(false); orePool.Push(ore); }
            v.ores.Clear();
        }

        // ---------------------------------------------------------------- belt texture
        /// <summary>Reads the belt's tiling so its arrows can scroll at the speed the bricks ride.</summary>
        void SetUpBelt() {
            Renderer belt = map.plots.Length > 0 ? map.plots[0].belt : null;
            if (belt == null || belt.sharedMaterial == null || !belt.sharedMaterial.HasProperty(BaseMapST)) return;
            beltBlock = new MaterialPropertyBlock();
            beltTiling = belt.sharedMaterial.GetVector(BaseMapST);
            // The belt is a unit cube stretched along X; its texture repeats beltTiling.x times over that length.
            float length = Mathf.Max(0.01f, belt.transform.localScale.x);
            beltOffsetPerSecond = TY_Catalog.BeltSpeed * beltTiling.x / length * Mathf.Sign(beltScrollDirection);
        }

        void ScrollBelts(float dt) {
            if (beltBlock == null) return;
            beltOffset = Mathf.Repeat(beltOffset + beltOffsetPerSecond * dt, 1f);
            beltBlock.SetVector(BaseMapST, new Vector4(beltTiling.x, beltTiling.y, beltTiling.z + beltOffset, beltTiling.w));
            for (int i = 0; i < visuals.Length; i++) {
                // Idle (unclaimed) belts stand still.
                Renderer belt = map.plots[i].belt;
                if (belt != null && visuals[i].state.ownerId >= 0) belt.SetPropertyBlock(beltBlock);
            }
        }

        // ---------------------------------------------------------------- daily chest
        /// <summary>Keeps the local player's chest ready or counting down to the next UTC day.</summary>
        void RefreshChest() {
            if (!hasWallet || wallet.plot < 0 || wallet.plot >= visuals.Length) return;
            TY_ChestView chest = visuals[wallet.plot].chest;
            if (chest != null && chest.gameObject.activeSelf)
                chest.Show(wallet.chestReady, chestBackAt - Time.unscaledTime, wallet.chestStreak, TY_DailyChest.PrizeText(wallet.chestMinutes, wallet.chestTokens));
        }

        // ---------------------------------------------------------------- auto-collector popups
        /// <summary>Drops earnings not shown yet and holds the next popup back for a full interval.</summary>
        void ResetAutoEarnings() {
            autoEarned = 0;
            nextAutoPopup = Time.time + autoPopupSeconds;
        }

        void PopAutoEarnings() {
            if (autoEarned <= 0 || Time.time < nextAutoPopup || !hasWallet || wallet.plot < 0 || wallet.plot >= visuals.Length) return;
            nextAutoPopup = Time.time + autoPopupSeconds;
            feedback.Earned(map.World(wallet.plot, TY_Catalog.CollectPosition), autoEarned);
            autoEarned = 0;
        }
    }
}
