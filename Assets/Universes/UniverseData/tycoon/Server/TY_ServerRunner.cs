using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Levels.Server;
using RyanAssets.Rounds;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using UnityEngine;
using Channel = FishNet.Transporting.Channel;

namespace Universes.UniverseData.tycoon.Server {
    /// <summary>
    /// Brick Tycoon: players claim a plot, earn from droppers into a collector, step on buttons to buy
    /// the next piece, and rebirth for a permanent income bonus. Every rule (cash, prices, unlocks,
    /// claims) is decided here from replicated character positions; clients only draw.
    /// </summary>
    public sealed class TY_ServerRunner : ServerRunner {
        [Header("Economy")]
        [Tooltip("Multiplies all income. 1 is the tuned pace (about ten minutes to the first rebirth).")]
        [SerializeField, Min(0.01f)] float incomeScale = 1f;
        [Tooltip("Cash a new player starts with.")]
        [SerializeField, Min(0)] long startingCash = 0;
        [Header("Rewards (account XP/gold)")]
        [SerializeField] ulong purchaseXP = 4;
        [SerializeField] ulong rebirthXP = 150;
        [SerializeField] ulong rebirthGold = 75;
        [SerializeField, Min(0.2f)] float tooPoorCooldown = 2.5f;

        const string CashColumn = "Cash";
        const string RebirthColumn = "Rebirths";

        sealed class Plot {
            public NetworkConnection owner;
            public uint purchased;
            public double bank;
        }

        sealed class Tycooner {
            public double cash;
            public int rebirths;
            public int plot = -1;
            public readonly Dictionary<int, float> refusedAt = new();
            public bool onCollector;
        }

        readonly Dictionary<NetworkConnection, Tycooner> players = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        Plot[] plots = Array.Empty<Plot>();
        TY_Map map;
        bool plotsDirty;

        protected override void Awake() {
            base.Awake();
            // Ground sits at y = 0; keep the kill plane well below it.
            GameCharacter.FallHeightOverride = -20f;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnPosition;
            ServerPlayerCharacter.RespawnTime = 2f;
            PlayerData.OnPlayerRemoved += OnPlayerLeft;
            InstanceFinder.ServerManager.RegisterBroadcast<TY_StateRequest>(OnStateRequest);
        }

        Tycooner For(NetworkConnection connection) {
            if (!players.TryGetValue(connection, out var t)) players[connection] = t = new Tycooner { cash = startingCash };
            return t;
        }

        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            For(player.Owner);
            player.cameraTypes.Clear();
            player.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
            player.walkSpeed.Value = 14f;
            player.sprintSpeed.Value = 22f;
            WriteLeaderboard(player);
            SendState(player.Owner);
        }

        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            character.CanSpectate.Value = false;
        }

        void OnPlayerLeft(PlayerData player) {
            if (players.TryGetValue(player.Owner, out var t) && t.plot >= 0) Release(t.plot);
            players.Remove(player.Owner);
            requestTimes.Remove(player.Owner);
        }

        Vector3 SpawnPosition(NetworkConnection connection) {
            if (map == null) return Vector3.up * 3f;
            Vector3 p = players.TryGetValue(connection, out var t) && t.plot >= 0 ? map.PlotSpawn(t.plot) : map.PlazaSpawn();
            return p + Vector3.up * 1.2f;
        }

        // ---------------------------------------------------------------- lifecycle
        protected override async UniTask StartAsync(CancellationToken token) {
            await WaitForSceneAsync("tycoon_start", token);
            map = FindAnyObjectByType<TY_Map>();
            if (map == null || !map.IsConfigured) throw new InvalidOperationException("Tycoon requires a configured TY_Map in tycoon_start.");
            if (plots.Length != map.PlotCount) plots = Enumerable.Range(0, map.PlotCount).Select(_ => new Plot()).ToArray();
            SetGlobalInvul(true);
            SetTeamKillEnabled(false);
            SetLeaderboardEnabled(CashColumn, true);
            SetLeaderboardEnabled(RebirthColumn, true);
            if (SharedGlobalEvents.Instance != null) SharedGlobalEvents.Instance.MusicTrack.Value = MusicSelection.GameMusic;
            foreach (var player in PlayerData.Players.Values) { For(player.Owner); WriteLeaderboard(player); }
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();

            // A tycoon has no rounds: earn and report forever, until the server restarts the runner.
            float last = Time.time;
            while (!token.IsCancellationRequested) {
                float now = Time.time, dt = now - last;
                last = now;
                for (int i = 0; i < plots.Length; i++) {
                    Plot plot = plots[i];
                    if (plot.owner == null || !players.TryGetValue(plot.owner, out var t)) continue;
                    plot.bank += TY_Catalog.Income(plot.purchased, t.rebirths) * incomeScale * dt;
                }
                int free = plots.Count(p => p.owner == null);
                SetTopMessage(free > 0 ? $"Brick Tycoon: claim a free plot! ({free} left)" : "Brick Tycoon: every plot is taken");
                BroadcastPlots();
                foreach (var connection in players.Keys.ToList()) SendWallet(connection);
                await AwaitTime(1000, token);
            }
        }

        // ---------------------------------------------------------------- rules
        void FixedUpdate() {
            if (map == null) return;
            foreach (var pair in LocalCharacter.Characters) {
                LocalCharacter character = pair.Value;
                if (character == null || character.IsDead || !character.IsSpawned) continue;
                if (!players.TryGetValue(pair.Key, out var t)) continue;
                Vector3 feet = character.transform.position;
                if (t.plot < 0) {
                    for (int i = 0; i < plots.Length; i++)
                        if (plots[i].owner == null && map.ClaimZone(i).ContainsFootprint(feet)) { Claim(pair.Key, t, i); break; }
                    continue;
                }
                Plot plot = plots[t.plot];
                bool onCollector = map.CollectZone(t.plot).ContainsFootprint(feet);
                if (onCollector && !t.onCollector) Collect(pair.Key, t, plot);
                t.onCollector = onCollector;
                foreach (int item in TY_Catalog.AvailableItems(plot.purchased).ToList())
                    if (map.ButtonZone(t.plot, item).ContainsFootprint(feet)) TryBuy(pair.Key, t, plot, item);
            }
            if (plotsDirty) BroadcastPlots();
        }

        void Claim(NetworkConnection connection, Tycooner t, int index) {
            plots[index].owner = connection;
            plots[index].purchased = TY_Catalog.StarterMask;
            plots[index].bank = 0;
            t.plot = index;
            plotsDirty = true;
            Event(connection, TY_EventKind.Claimed, index, -1, 0, "You claimed a tycoon! Step on green buttons to build.");
            ServerChat.SendSystemMessage(new($"{PlayerData.GetPlayerName(connection)} claimed tycoon #{index + 1}.", SystemMessageSource.CustomMessage));
            SendWallet(connection);
        }

        void Release(int index) {
            plots[index] = new Plot();
            plotsDirty = true;
        }

        void Collect(NetworkConnection connection, Tycooner t, Plot plot) {
            long amount = (long)Math.Floor(plot.bank);
            if (amount <= 0) return;
            plot.bank -= amount;
            t.cash += amount;
            plotsDirty = true;
            Event(connection, TY_EventKind.Collected, t.plot, -1, amount, $"+${RoundFormat.Money(amount)}");
            if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
            SendWallet(connection);
        }

        void TryBuy(NetworkConnection connection, Tycooner t, Plot plot, int index) {
            var item = TY_Catalog.Items[index];
            if (t.cash < item.price) {
                if (t.refusedAt.TryGetValue(index, out float at) && Time.time - at < tooPoorCooldown) return;
                t.refusedAt[index] = Time.time;
                long missing = (long)Math.Ceiling(item.price - t.cash);
                Event(connection, TY_EventKind.TooPoor, t.plot, index, missing, $"You need ${RoundFormat.Money(missing)} more for {item.name}. Collect at the furnace!");
                return;
            }
            t.cash -= item.price;
            if (item.kind == TY_ItemKind.Rebirth) {
                t.rebirths++;
                t.cash = startingCash;
                plot.purchased = TY_Catalog.StarterMask;
                plot.bank = 0;
                Event(connection, TY_EventKind.Rebirth, t.plot, index, t.rebirths,
                    $"REBIRTH {t.rebirths}! All income is now x{TY_Catalog.RebirthMultiplier(t.rebirths):0.#}.");
                ServerChat.SendSystemMessage(new($"{PlayerData.GetPlayerName(connection)} rebirthed! (x{t.rebirths})", SystemMessageSource.CustomMessage));
                Award(connection, rebirthXP, rebirthGold);
            } else {
                plot.purchased |= 1u << index;
                Event(connection, TY_EventKind.Bought, t.plot, index, item.price, $"Built {item.name}!");
                Award(connection, purchaseXP, 0);
            }
            plotsDirty = true;
            if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
            SendWallet(connection);
        }

        // ---------------------------------------------------------------- messages
        void OnStateRequest(NetworkConnection connection, TY_StateRequest request, Channel channel) {
            if (!connection.IsAuthenticated) return;
            if (requestTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < 1f) return;
            requestTimes[connection] = Time.unscaledTime;
            SendState(connection);
        }

        void SendState(NetworkConnection connection) {
            if (map == null || connection == null || !connection.IsActive) return;
            InstanceFinder.ServerManager.Broadcast(connection, MakePlots());
            SendWallet(connection);
        }

        TY_PlotsBroadcast MakePlots() => new() {
            plots = plots.Select((p, i) => new TY_PlotState {
                plot = (byte)i,
                ownerId = p.owner != null ? p.owner.ClientId : -1,
                ownerName = p.owner != null ? PlayerData.GetPlayerName(p.owner) : "",
                purchased = p.purchased,
                bank = (long)Math.Floor(p.bank),
                rebirths = p.owner != null && players.TryGetValue(p.owner, out var t) ? t.rebirths : 0,
                income = p.owner != null && players.TryGetValue(p.owner, out var u) ? (float)(TY_Catalog.Income(p.purchased, u.rebirths) * incomeScale) : 0f
            }).ToArray()
        };

        void BroadcastPlots() {
            plotsDirty = false;
            if (map != null) InstanceFinder.ServerManager.Broadcast(MakePlots());
        }

        void SendWallet(NetworkConnection connection) {
            if (connection == null || !connection.IsActive || !players.TryGetValue(connection, out var t)) return;
            InstanceFinder.ServerManager.Broadcast(connection, new TY_WalletBroadcast {
                cash = (long)Math.Floor(t.cash), plot = t.plot, rebirths = t.rebirths,
                income = t.plot >= 0 ? (float)(TY_Catalog.Income(plots[t.plot].purchased, t.rebirths) * incomeScale) : 0f
            });
        }

        static void Event(NetworkConnection connection, TY_EventKind kind, int plot, int item, long amount, string text) {
            if (connection == null || !connection.IsActive) return;
            InstanceFinder.ServerManager.Broadcast(connection, new TY_EventBroadcast { kind = kind, plot = plot, item = item, amount = amount, text = text });
        }

        void WriteLeaderboard(PlayerData player) {
            if (!players.TryGetValue(player.Owner, out var t)) return;
            RoundLeaderboard.Set(player, CashColumn, (int)Math.Min(int.MaxValue, Math.Floor(t.cash)));
            RoundLeaderboard.Set(player, RebirthColumn, t.rebirths);
        }

        static void Award(NetworkConnection connection, ulong xp, ulong gold) {
            if ((xp != 0 || gold != 0) && !LevelsServer.AwardPlayerXPAndGold(connection, xp, gold))
                Debug.LogWarning($"Tycoon could not award rewards to connection {connection.ClientId}.");
        }

        // ---------------------------------------------------------------- debugging hooks
        /// <summary>Gives a player cash (server console and probes).</summary>
        public void GrantCash(NetworkConnection connection, long amount) {
            For(connection).cash += amount;
            if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
            SendWallet(connection);
        }

        protected override void OnDestroy() {
            GameCharacter.FallHeightOverride = null;
            PlayerData.OnPlayerRemoved -= OnPlayerLeft;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnPosition) ServerPlayerCharacter.SpawnLocationFunction = null;
            if (InstanceFinder.ServerManager != null) InstanceFinder.ServerManager.UnregisterBroadcast<TY_StateRequest>(OnStateRequest);
            base.OnDestroy();
        }
    }
}
