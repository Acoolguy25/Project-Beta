using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Client;
using FishNet.Transporting;
using RyanAssets.Rounds;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tycoon.Client {
    /// <summary>
    /// Draws Brick Tycoon from server state: who owns each plot, bought items, buy buttons, collector
    /// totals and the ore that rides each belt. Ore is cosmetic; cash is counted on the server.
    /// </summary>
    public sealed class TY_ClientController : MonoBehaviour {
        public TMP_Text cashLabel, incomeLabel, plotLabel, hintLabel, toastLabel;
        public AudioSource sfx;
        public AudioClip buyClip, collectClip, deniedClip, rebirthClip;

        sealed class Ore { public Transform t; public Renderer r; public float distance; public int stage; }

        sealed class PlotVisual {
            public TY_PlotState state;
            public readonly GameObject[] items = new GameObject[TY_Catalog.Count];
            public readonly TY_ButtonView[] buttons = new TY_ButtonView[TY_Catalog.Count];
            public readonly float[] dropTimers = new float[TY_Catalog.Count];
            public readonly List<Ore> ores = new();
            public bool hasState;
        }

        TY_Map map;
        ClientManager manager;
        PlotVisual[] visuals;
        readonly Stack<Ore> orePool = new();
        TY_WalletBroadcast wallet;
        bool hasWallet, hasPlots;
        float toastUntil, nextRequest;
        float[] upgraderDistances;

        void Awake() {
            map = GetComponentInParent<TY_Map>();
            toastLabel.text = "";
            cashLabel.text = "$0";
            incomeLabel.text = "";
            plotLabel.text = "BRICK TYCOON";
            hintLabel.text = "Connecting...";
            var upgraders = new List<float>();
            foreach (var item in TY_Catalog.Items)
                if (item.kind == TY_ItemKind.Upgrader) upgraders.Add(TY_Catalog.BeltDistance(item.itemPosition));
            upgraderDistances = upgraders.ToArray();
        }

        void Start() {
            visuals = new PlotVisual[map.PlotCount];
            for (int i = 0; i < visuals.Length; i++) {
                var v = visuals[i] = new PlotVisual();
                var view = map.plots[i];
                for (int item = 0; item < TY_Catalog.Count; item++) {
                    var go = Instantiate(map.buttonPrefab, view.itemRoot);
                    go.transform.localPosition = TY_Catalog.Items[item].buttonPosition;
                    go.transform.localRotation = Quaternion.identity;
                    go.name = $"Button {TY_Catalog.Items[item].name}";
                    v.buttons[item] = go.GetComponent<TY_ButtonView>();
                    go.SetActive(false);
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
            if (toastLabel.text.Length > 0 && Time.unscaledTime > toastUntil) toastLabel.text = "";
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
            RefreshButtons();
            RefreshHud();
        }

        void OnEvent(TY_EventBroadcast message, Channel channel) {
            AudioClip clip = message.kind switch {
                TY_EventKind.Bought or TY_EventKind.Claimed => buyClip,
                TY_EventKind.Collected => collectClip,
                TY_EventKind.TooPoor => deniedClip,
                TY_EventKind.Rebirth => rebirthClip,
                _ => null
            };
            Toast(message.text, clip, message.kind == TY_EventKind.Rebirth ? 5f : 2.5f);
        }

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
                view.ownerLabel.text = owned ? $"{state.ownerName}'s Tycoon" + (state.rebirths > 0 ? $"\n<size=60%>Rebirth {state.rebirths}</size>" : "") : "FREE PLOT\n<size=60%>Step on the pad to claim</size>";
            if (view.bankLabel != null)
                view.bankLabel.text = owned ? $"${RoundFormat.Money(state.bank)}\n<size=55%>step here to collect</size>" : "";
            for (int item = 0; item < TY_Catalog.Count; item++) {
                bool has = owned && TY_Catalog.Owns(state.purchased, item) && map.itemPrefabs[item] != null;
                if (has && v.items[item] == null) {
                    var go = Instantiate(map.itemPrefabs[item], view.itemRoot);
                    go.transform.localPosition = TY_Catalog.Items[item].itemPosition;
                    go.transform.localRotation = Quaternion.identity;
                    go.name = TY_Catalog.Items[item].name;
                    v.items[item] = go;
                    if (!ownerChanged) StartCoroutine(PopIn(go.transform));
                } else if (!has && v.items[item] != null) {
                    Destroy(v.items[item]);
                    v.items[item] = null;
                }
            }
            if (!owned) ClearOre(v);
            RefreshButtons(index);
        }

        void RefreshButtons() {
            if (visuals != null) for (int i = 0; i < visuals.Length; i++) RefreshButtons(i);
        }

        void RefreshButtons(int index) {
            var v = visuals[index];
            bool owned = v.state.ownerId >= 0;
            bool mine = owned && v.state.ownerId == LocalClientId;
            for (int item = 0; item < TY_Catalog.Count; item++) {
                bool show = owned && TY_Catalog.Available(v.state.purchased, item);
                var button = v.buttons[item];
                if (button == null) continue;
                if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show);
                if (!show) continue;
                var data = TY_Catalog.Items[item];
                int look = !mine ? 2 : hasWallet && wallet.cash >= data.price ? 0 : 1;
                button.Show(data.name, data.price, look);
            }
        }

        void RefreshHud() {
            if (!hasWallet) { hintLabel.text = "Loading tycoon..."; return; }
            cashLabel.text = $"${RoundFormat.Money(wallet.cash)}";
            if (wallet.plot < 0) {
                plotLabel.text = "BRICK TYCOON";
                incomeLabel.text = "";
                hintLabel.text = "Find a FREE PLOT and step on its glowing pad to claim it.";
                return;
            }
            var v = visuals[wallet.plot];
            plotLabel.text = $"TYCOON #{wallet.plot + 1}" + (wallet.rebirths > 0 ? $"  <color=#FF9CF2>REBIRTH {wallet.rebirths}</color>" : "");
            incomeLabel.text = $"+${RoundFormat.Money(wallet.income)}/s   collector ${RoundFormat.Money(v.state.bank)}";
            int next = -1;
            long cheapest = long.MaxValue;
            foreach (int item in TY_Catalog.AvailableItems(v.state.purchased))
                if (TY_Catalog.Items[item].price < cheapest) { cheapest = TY_Catalog.Items[item].price; next = item; }
            hintLabel.text = next < 0 ? "Everything is built!"
                : wallet.cash >= cheapest ? $"Step on the green {TY_Catalog.Items[next].name} button to build it."
                : v.state.bank > 0 ? "Step on the furnace's collector pad to bank your cash."
                : $"Next: {TY_Catalog.Items[next].name} for ${RoundFormat.Money(cheapest)}. Your droppers are earning.";
        }

        // ---------------------------------------------------------------- ore
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
                if (v.ores.Count < 80) v.ores.Add(SpawnOre(plot, TY_Catalog.BeltDistance(data.itemPosition), data.value));
            }
            float end = TY_Catalog.BeltEndX - TY_Catalog.BeltStartX + 1.5f;
            for (int i = v.ores.Count - 1; i >= 0; i--) {
                var ore = v.ores[i];
                ore.distance += TY_Catalog.BeltSpeed * dt;
                int stage = 0;
                foreach (float d in upgraderDistances) if (ore.distance >= d && UpgraderOwned(v, d)) stage++;
                if (stage != ore.stage) {
                    ore.stage = stage;
                    if (map.oreMaterials != null && map.oreMaterials.Length > 0) ore.r.sharedMaterial = map.oreMaterials[Mathf.Min(stage, map.oreMaterials.Length - 1)];
                    ore.t.localScale = Vector3.one * (0.55f + 0.12f * stage);
                }
                if (ore.distance >= end) {
                    ore.t.gameObject.SetActive(false);
                    orePool.Push(ore);
                    v.ores.RemoveAt(i);
                    continue;
                }
                ore.t.position = plot.TransformPoint(new Vector3(TY_Catalog.BeltStartX + ore.distance, TY_Catalog.BeltTop + 0.3f, TY_Catalog.BeltZ));
                ore.t.Rotate(0f, 90f * dt, 0f, Space.Self);
            }
        }

        static bool UpgraderOwned(PlotVisual v, float distance) {
            for (int i = 0; i < TY_Catalog.Count; i++) {
                var item = TY_Catalog.Items[i];
                if (item.kind == TY_ItemKind.Upgrader && Mathf.Approximately(TY_Catalog.BeltDistance(item.itemPosition), distance))
                    return TY_Catalog.Owns(v.state.purchased, i);
            }
            return false;
        }

        Ore SpawnOre(Transform plot, float distance, double value) {
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

        System.Collections.IEnumerator PopIn(Transform t) {
            Vector3 target = t.localScale;
            for (float a = 0f; a < 1f && t != null; a += Time.deltaTime * 3f) {
                float s = 1f + Mathf.Sin(a * Mathf.PI) * 0.25f;
                t.localScale = target * Mathf.Lerp(0.2f, 1f, a) * s;
                yield return null;
            }
            if (t != null) t.localScale = target;
        }

        void Toast(string message, AudioClip clip, float seconds) {
            toastLabel.text = message;
            toastUntil = Time.unscaledTime + seconds;
            if (sfx != null && clip != null) sfx.PlayOneShot(clip);
        }
    }
}
