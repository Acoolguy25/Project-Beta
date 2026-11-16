using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.tycoon.Client {
    /// <summary>
    /// Draws Brick Tycoon from server state: who owns each plot, bought items, lane buy pads,
    /// collector totals, gold coins and the bricks that ride each belt, and feeds the HUD and the
    /// game feel. Bricks are cosmetic; cash is counted on the server.
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
        [Tooltip("Height of a brick above the belt surface.")]
        [SerializeField, Min(0f)] float brickRideHeight = 0.2f;
        [Tooltip("How much bigger a brick gets per upgrader it passes (0.1 = 10%).")]
        [SerializeField, Min(0f)] float brickGrowthPerStage = 0.1f;

        [Header("Belt texture")]
        [Tooltip("+1 or -1: whichever makes the belt's arrows move with the bricks.")]
        [SerializeField] float beltScrollDirection = 1f;

        [Header("Auto-collector")]
        [Tooltip("Seconds between the '+$' popups of an auto-collecting plot.")]
        [SerializeField, Min(0.5f)] float autoPopupSeconds = 3f;

        static readonly TY_ItemKind[] PadLanes = { TY_ItemKind.Dropper, TY_ItemKind.Upgrader, TY_ItemKind.Building, TY_ItemKind.Utility, TY_ItemKind.Rebirth };

        sealed class Ore { public Transform t; public Renderer r; public float distance; public int stage; }

        sealed class PlotVisual {
            public TY_PlotState state;
            public readonly GameObject[] items = new GameObject[TY_Catalog.Count];
            public readonly TY_ButtonView[] pads = new TY_ButtonView[PadLanes.Length];
            public readonly float[] dropTimers = new float[TY_Catalog.Count];
            public readonly List<Ore> ores = new();
            public GameObject golden;
            public FxHandle beacon;
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
        }

        void Start() {
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
                ApplyPlot(i, new TY_PlotState { plot = (byte)i, ownerId = -1 }, false);
            }
            SetUpBelt();
        }

        void OnDestroy() {
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
            if (hasWallet && message.plot >= 0 && message.plot == wallet.plot && message.plot < visuals.Length
                && TY_Catalog.AutoCollects(visuals[message.plot].state.purchased))
                autoEarned += message.lifetime - wallet.lifetime;
            wallet = message;
            hasWallet = true;
            RefreshPads();
            RefreshHud();
        }

        void OnEvent(TY_EventBroadcast e, Channel channel) {
            hud.ShowEvent(e);
            bool onPlot = e.plot >= 0 && e.plot < visuals.Length;
            switch (e.kind) {
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
                case TY_EventKind.Rebirth: feedback.RebirthScreen((int)e.amount); break;
                case TY_EventKind.Perk: feedback.Perk(); break;
                case TY_EventKind.CollectorFull: feedback.CollectorFull(); break;
                case TY_EventKind.RushHour: feedback.RushStarted(hasWallet && wallet.rushMultiplier > 1f ? wallet.rushMultiplier : 2f); break;
                case TY_EventKind.RushHourOver: feedback.RushEnded(); break;
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
            int removed = TY_Catalog.OwnedCount(previous.purchased & ~state.purchased);
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
                } else if (has && reborn) {
                    DropIn(v.items[item].transform, data.itemPosition, dropped++);
                } else if (!has && v.items[item] != null) {
                    if (animate) feedback.TearDown(v.items[item], feedback.TearDownDelay(torn++, removed));
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
                int item = PadLanes[lane] == TY_ItemKind.Rebirth
                    ? (TY_Catalog.Available(v.state.purchased, TY_Catalog.RebirthIndex) ? TY_Catalog.RebirthIndex : -1)
                    : TY_Catalog.NextInLane(v.state.purchased, PadLanes[lane]);
                bool show = owned && item >= 0;
                if (pad.gameObject.activeSelf != show) pad.gameObject.SetActive(show);
                if (!show) continue;
                var data = TY_Catalog.Items[item];
                bool locked = !TY_Catalog.Available(v.state.purchased, item);
                TY_ButtonLook look = !mine ? TY_ButtonLook.Foreign
                    : locked ? TY_ButtonLook.Locked
                    : hasWallet && wallet.cash >= data.price ? TY_ButtonLook.Affordable
                    : TY_ButtonLook.Expensive;
                float saved = data.price <= 0 ? 1f : hasWallet ? Mathf.Clamp01((float)wallet.cash / data.price) : 0f;
                TY_ButtonLook before = pad.Look;
                pad.Show(data, map.Picture(item), look, TY_Hud.LockedNote(item), saved);
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
            for (int item = 0; item < TY_Catalog.Count; item++) {
                var data = TY_Catalog.Items[item];
                if (data.kind != TY_ItemKind.Dropper || !TY_Catalog.Owns(v.state.purchased, item)) continue;
                v.dropTimers[item] += dt;
                if (v.dropTimers[item] < data.interval) continue;
                v.dropTimers[item] -= data.interval;
                if (v.ores.Count >= maxBricksPerBelt) continue;
                v.ores.Add(SpawnOre(plot, TY_Catalog.BeltDistance(data.itemPosition)));
                if (mine) feedback.BrickDropped(plot.TransformPoint(data.itemPosition + Vector3.up * TY_Catalog.BeltTop));
            }
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
                    if (look >= 0) ore.r.sharedMaterial = map.oreMaterials[look];
                    ore.t.localScale = Vector3.one * (1f + brickGrowthPerStage * stage);
                    if (upgraded && mine) feedback.BrickUpgraded(ore.t.position, look >= 0 ? oreTints[look] : Color.white);
                }
                if (ore.distance >= end) {
                    ore.t.gameObject.SetActive(false);
                    orePool.Push(ore);
                    v.ores.RemoveAt(i);
                    continue;
                }
                ore.t.position = plot.TransformPoint(new Vector3(TY_Catalog.BeltStartX + ore.distance, TY_Catalog.BeltTop + brickRideHeight, TY_Catalog.BeltZ));
                ore.t.rotation = plot.rotation;
            }
        }

        Ore SpawnOre(Transform plot, float distance) {
            Ore ore = orePool.Count > 0 ? orePool.Pop() : null;
            if (ore == null) {
                var go = Instantiate(map.orePrefab, map.transform);
                ore = new Ore { t = go.transform, r = go.GetComponentInChildren<Renderer>() };
            }
            ore.distance = distance;
            ore.stage = -1;
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

        // ---------------------------------------------------------------- auto-collector popups
        void PopAutoEarnings() {
            if (autoEarned <= 0 || Time.time < nextAutoPopup || !hasWallet || wallet.plot < 0 || wallet.plot >= visuals.Length) return;
            nextAutoPopup = Time.time + autoPopupSeconds;
            feedback.Earned(map.World(wallet.plot, TY_Catalog.CollectPosition), autoEarned);
            autoEarned = 0;
        }
    }
}
