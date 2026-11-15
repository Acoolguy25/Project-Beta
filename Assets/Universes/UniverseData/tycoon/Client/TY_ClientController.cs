using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using UnityEngine;

namespace Universes.UniverseData.tycoon.Client {
    /// <summary>
    /// Draws Brick Tycoon from server state: who owns each plot, bought items, lane buy pads,
    /// collector totals, gold coins and the bricks that ride each belt, and feeds the HUD.
    /// Bricks are cosmetic; cash is counted on the server.
    /// </summary>
    public sealed class TY_ClientController : MonoBehaviour {
        [Tooltip("The HUD on this prefab.")]
        public TY_Hud hud;
        [Tooltip("Most bricks a belt shows at once.")]
        [SerializeField, Min(8)] int maxBricksPerBelt = 80;

        static readonly TY_ItemKind[] PadLanes = { TY_ItemKind.Dropper, TY_ItemKind.Upgrader, TY_ItemKind.Building, TY_ItemKind.Utility, TY_ItemKind.Rebirth };

        sealed class Ore { public Transform t; public Renderer r; public float distance; public int stage; }

        sealed class PlotVisual {
            public TY_PlotState state;
            public readonly GameObject[] items = new GameObject[TY_Catalog.Count];
            public readonly TY_ButtonView[] pads = new TY_ButtonView[PadLanes.Length];
            public readonly float[] dropTimers = new float[TY_Catalog.Count];
            public readonly List<Ore> ores = new();
            public GameObject golden;
            public bool hasState;
        }

        TY_Map map;
        ClientManager manager;
        PlotVisual[] visuals;
        readonly Stack<Ore> orePool = new();
        TY_WalletBroadcast wallet;
        bool hasWallet, hasPlots;
        float nextRequest;
        float[] upgraderDistances;
        int[] upgraderItems;

        void Awake() {
            map = GetComponentInParent<TY_Map>();
            var distances = new List<float>();
            var items = new List<int>();
            for (int i = 0; i < TY_Catalog.Count; i++)
                if (TY_Catalog.Items[i].kind == TY_ItemKind.Upgrader) { distances.Add(TY_Catalog.BeltDistance(TY_Catalog.Items[i].itemPosition)); items.Add(i); }
            upgraderDistances = distances.ToArray();
            upgraderItems = items.ToArray();
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
                ApplyPlot(i, new TY_PlotState { plot = (byte)i, ownerId = -1 });
            }
        }

        void OnDestroy() {
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
            if (visuals != null) for (int i = 0; i < visuals.Length; i++) AnimateOre(i, Time.deltaTime);
        }

        // ---------------------------------------------------------------- state
        void OnPlots(TY_PlotsBroadcast message, Channel channel) {
            if (message.plots == null || visuals == null) return;
            hasPlots = true;
            foreach (var state in message.plots)
                if (state.plot < visuals.Length) ApplyPlot(state.plot, state);
            RefreshHud();
        }

        void OnWallet(TY_WalletBroadcast message, Channel channel) {
            wallet = message;
            hasWallet = true;
            RefreshPads();
            RefreshHud();
        }

        void OnEvent(TY_EventBroadcast message, Channel channel) => hud.ShowEvent(message);

        int LocalClientId => manager != null && manager.Connection != null ? manager.Connection.ClientId : -2;

        void ApplyPlot(int index, TY_PlotState state) {
            var v = visuals[index];
            var view = map.plots[index];
            bool ownerChanged = !v.hasState || v.state.ownerId != state.ownerId;
            v.state = state;
            v.hasState = true;
            bool owned = state.ownerId >= 0;
            if (view.claimPad != null && view.claimPad.activeSelf == owned) view.claimPad.SetActive(!owned);
            if (view.ownerLabel != null)
                view.ownerLabel.text = owned
                    ? $"{state.ownerName}'s Tycoon" + (state.rebirths > 0 ? $"\n<size=60%><color=#FF9CF2>Rebirth {state.rebirths}</color></size>" : "")
                    : "FREE PLOT\n<size=60%>Step on the green pad to claim</size>";
            if (view.bankLabel != null)
                view.bankLabel.text = !owned ? ""
                    : TY_Catalog.AutoCollects(state.purchased) ? $"${TY_Catalog.Money(state.income)}/s\n<size=55%>auto-collecting</size>"
                    : $"${TY_Catalog.Money(state.bank)}<size=55%> / ${TY_Catalog.Money(state.capacity)}</size>\n<size=55%>{(state.bank >= state.capacity ? "<color=#FF6B6B>FULL! step here</color>" : "step here to collect")}</size>";
            for (int item = 0; item < TY_Catalog.Count; item++) {
                bool has = owned && TY_Catalog.Owns(state.purchased, item) && map.itemPrefabs[item] != null;
                if (has && v.items[item] == null) {
                    var data = TY_Catalog.Items[item];
                    var go = Instantiate(map.itemPrefabs[item], view.itemRoot);
                    go.transform.localPosition = data.itemPosition;
                    go.transform.localRotation = Quaternion.Euler(0f, data.itemYaw, 0f);
                    go.name = data.name;
                    v.items[item] = go;
                    if (!ownerChanged) StartCoroutine(PopIn(go.transform));
                } else if (!has && v.items[item] != null) {
                    Destroy(v.items[item]);
                    v.items[item] = null;
                }
            }
            if (v.golden != null) {
                bool show = owned && state.golden;
                if (show) v.golden.transform.localPosition = state.goldenPosition;
                if (v.golden.activeSelf != show) v.golden.SetActive(show);
            }
            if (!owned) ClearOre(v);
            RefreshPads(index);
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
                pad.Show(data, map.Picture(item), look, TY_Hud.LockedNote(item));
            }
        }

        void RefreshHud() {
            if (!hasWallet) return;
            TY_PlotState? plot = wallet.plot >= 0 && wallet.plot < visuals.Length ? visuals[wallet.plot].state : null;
            hud.Refresh(wallet, plot, map);
        }

        // ---------------------------------------------------------------- bricks on the belt
        void AnimateOre(int index, float dt) {
            var v = visuals[index];
            if (!v.hasState || v.state.ownerId < 0 || map.orePrefab == null) return;
            var plot = map.plots[index].transform;
            for (int item = 0; item < TY_Catalog.Count; item++) {
                var data = TY_Catalog.Items[item];
                if (data.kind != TY_ItemKind.Dropper || !TY_Catalog.Owns(v.state.purchased, item)) continue;
                v.dropTimers[item] += dt;
                if (v.dropTimers[item] < data.interval) continue;
                v.dropTimers[item] -= data.interval;
                if (v.ores.Count < maxBricksPerBelt) v.ores.Add(SpawnOre(plot, TY_Catalog.BeltDistance(data.itemPosition)));
            }
            float end = TY_Catalog.BeltEndX - TY_Catalog.BeltStartX + 1.5f;
            for (int i = v.ores.Count - 1; i >= 0; i--) {
                var ore = v.ores[i];
                ore.distance += TY_Catalog.BeltSpeed * dt;
                int stage = 0;
                for (int u = 0; u < upgraderDistances.Length; u++)
                    if (ore.distance >= upgraderDistances[u] && TY_Catalog.Owns(v.state.purchased, upgraderItems[u])) stage++;
                if (stage != ore.stage) {
                    ore.stage = stage;
                    if (map.oreMaterials != null && map.oreMaterials.Length > 0) ore.r.sharedMaterial = map.oreMaterials[Mathf.Min(stage, map.oreMaterials.Length - 1)];
                    ore.t.localScale = Vector3.one * (1f + 0.1f * stage);
                }
                if (ore.distance >= end) {
                    ore.t.gameObject.SetActive(false);
                    orePool.Push(ore);
                    v.ores.RemoveAt(i);
                    continue;
                }
                ore.t.position = plot.TransformPoint(new Vector3(TY_Catalog.BeltStartX + ore.distance, TY_Catalog.BeltTop + 0.2f, TY_Catalog.BeltZ));
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
            ore.t.position = plot.TransformPoint(new Vector3(TY_Catalog.BeltStartX + distance, TY_Catalog.BeltTop + 2.2f, TY_Catalog.BeltZ));
            return ore;
        }

        void ClearOre(PlotVisual v) {
            foreach (var ore in v.ores) { ore.t.gameObject.SetActive(false); orePool.Push(ore); }
            v.ores.Clear();
        }

        static IEnumerator PopIn(Transform t) {
            Vector3 target = t.localScale;
            for (float a = 0f; a < 1f && t != null; a += Time.deltaTime * 3f) {
                float s = 1f + Mathf.Sin(a * Mathf.PI) * 0.25f;
                t.localScale = target * Mathf.Lerp(0.2f, 1f, a) * s;
                yield return null;
            }
            if (t != null) t.localScale = target;
        }
    }
}
