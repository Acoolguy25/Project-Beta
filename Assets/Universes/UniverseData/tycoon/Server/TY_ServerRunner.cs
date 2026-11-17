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
using Random = UnityEngine.Random;

namespace Universes.UniverseData.tycoon.Server {
    /// <summary>
    /// Brick Tycoon: players claim a plot, earn from droppers into a collector with a limited size,
    /// step on lane pads to build, grab gold coins, and rebirth for a permanent multiplier plus a
    /// token for the perk shop. Every rule (cash, prices, unlocks, claims, pickups) is decided here
    /// from replicated character positions; clients only draw.
    /// </summary>
    public sealed partial class TY_ServerRunner : ServerRunner {
        [Header("Economy")]
        [Tooltip("Multiplies all income. 1 is the tuned pace (about ten minutes to the first rebirth).")]
        [SerializeField, Min(0.01f)] float incomeScale = 1f;
        [Tooltip("Cash a new player starts with (the Head Start perk adds to it).")]
        [SerializeField, Min(0)] long startingCash = 0;
        [Tooltip("Base walk / sprint speed; the Sprint Boots perk multiplies both.")]
        [SerializeField, Min(1f)] float walkSpeed = 14f, sprintSpeed = 22f;

        [Header("Gold coins")]
        [Tooltip("Seconds between gold coins on a plot (random in this range).")]
        [SerializeField] Vector2 goldenInterval = new(45f, 90f);
        [Tooltip("Seconds a gold coin waits before it vanishes.")]
        [SerializeField, Min(5f)] float goldenLifetime = 30f;
        [Tooltip("A gold coin pays this many seconds of the plot's income.")]
        [SerializeField, Min(1f)] float goldenIncomeSeconds = 25f;
        [SerializeField, Min(0)] long goldenMinimum = 50;

        [Header("Rush hour")]
        [Tooltip("Timed server event: every few minutes every tycoon earns more for a short while.")]
        [SerializeField] TY_RushHour rushHour = new();
        [Tooltip("Test servers only: registers the StartRushHour and RushMultiplier commands (see TY_Commands). The command "
            + "service has no permissions, so any player could run them, and rush earnings are saved. Keep it off when shipping.")]
        [SerializeField] bool rushCommands;

        [Header("Rewards (account XP/gold)")]
        [SerializeField] ulong purchaseXP = 4;
        [SerializeField] ulong rebirthXP = 150;
        [SerializeField] ulong rebirthGold = 75;

        [Header("Feedback")]
        [SerializeField, Min(0.2f)] float refusalCooldown = 2.5f;
        [Tooltip("Seconds between 'collector full' reminders.")]
        [SerializeField, Min(1f)] float fullReminder = 25f;

        const string CashColumn = "Cash";
        // Short titles: the shared player list gives every stat column the same narrow fixed width.
        const string RebirthColumn = "Reb.";

        sealed class Plot {
            public NetworkConnection owner;
            public ulong purchased;
            public double bank;
            public bool golden;
            public Vector3 goldenPosition;
            public float goldenUntil, nextGolden;
        }

        sealed class Tycooner {
            public double cash, lifetime;
            public int rebirths, tokens;
            public TY_PerkLevels perks;
            public int plot = -1;
            public bool onCollector;
            /// <summary>Lanes whose pad the player stood on last tick: a pad buys once per step onto it.</summary>
            public int padsUnderFeet;
            public float fullRemindedAt = float.NegativeInfinity;
            public readonly Dictionary<int, float> refusedAt = new();
        }

        static readonly TY_ItemKind[] Lanes = { TY_ItemKind.Dropper, TY_ItemKind.Upgrader, TY_ItemKind.Building, TY_ItemKind.Utility, TY_ItemKind.Rebirth };

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
            InstanceFinder.ServerManager.RegisterBroadcast<TY_PerkRequest>(OnPerkRequest);
            rushHour.Validate();
        }

        void OnValidate() => rushHour?.Validate();

        Tycooner For(NetworkConnection connection) {
            if (!players.TryGetValue(connection, out var t)) players[connection] = t = new Tycooner { cash = startingCash };
            return t;
        }

        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            var t = For(player.Owner);
            player.cameraTypes.Clear();
            player.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
            ApplySpeed(player, t);
            WriteLeaderboard(player);
            SendState(player.Owner);
            LoadProgressAsync(player).Forget();
        }

        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            character.CanSpectate.Value = false;
        }

        void OnPlayerLeft(PlayerData player) {
            if (players.TryGetValue(player.Owner, out var t)) {
                CaptureProgressOnLeave(player, t);
                if (t.plot >= 0) Release(t.plot);
            }
            players.Remove(player.Owner);
            requestTimes.Remove(player.Owner);
        }

        Vector3 SpawnPosition(NetworkConnection connection) {
            if (map == null) return Vector3.up * 3f;
            Vector3 p = players.TryGetValue(connection, out var t) && t.plot >= 0 ? map.PlotSpawn(t.plot) : map.PlazaSpawn();
            return p + Vector3.up * 1.2f;
        }

        void ApplySpeed(PlayerData player, Tycooner t) {
            float m = TY_Perks.SpeedMultiplier(t.perks);
            player.walkSpeed.Value = walkSpeed * m;
            player.sprintSpeed.Value = sprintSpeed * m;
        }

        /// <summary>Everything on top of the catalog's own multipliers: the tuning scale and a running rush hour.</summary>
        double EventScale => rushHour.Scale(incomeScale, Time.time);

        double IncomeOf(Plot plot, Tycooner t) => TY_Catalog.Income(plot.purchased, t.rebirths, t.perks) * EventScale;

        // ---------------------------------------------------------------- lifecycle
        protected override async UniTask StartAsync(CancellationToken token) {
            await WaitForSceneAsync("tycoon_start", token);
            map = FindAnyObjectByType<TY_Map>();
            if (map == null || !map.IsConfigured) throw new InvalidOperationException("Tycoon requires a configured TY_Map in tycoon_start.");
            if (plots.Length != map.PlotCount) plots = Enumerable.Range(0, map.PlotCount).Select(_ => new Plot()).ToArray();
            SetGlobalInvul(true);
            SetTeamKillEnabled(false);
            if (rushCommands) TY_Commands.Register(this);
            SetLeaderboardEnabled(CashColumn, true);
            SetLeaderboardEnabled(RebirthColumn, true);
            if (SharedGlobalEvents.Instance != null) SharedGlobalEvents.Instance.MusicTrack.Value = MusicSelection.GameMusic;
            foreach (var player in PlayerData.Players.Values) { For(player.Owner); WriteLeaderboard(player); }
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();

            // A tycoon has no rounds: earn and report forever, until the server restarts the runner.
            float last = Time.time;
            rushHour.Schedule(last, Random.value);
            while (!token.IsCancellationRequested) {
                float now = Time.time, dt = now - last;
                last = now;
                UpdateRushHour(now);
                for (int i = 0; i < plots.Length; i++) Earn(plots[i], dt, now);
                SetTopMessage(TopMessage(now));
                BroadcastPlots();
                foreach (var connection in players.Keys.ToList()) SendWallet(connection);
                await AwaitTime(1000, token);
            }
        }

        string TopMessage(float now) {
            if (rushHour.IsActive(now))
                return $"RUSH HOUR: every tycoon earns x{rushHour.multiplier:0.#} for {Mathf.CeilToInt(rushHour.Remaining(now))} s!";
            int free = plots.Count(p => p.owner == null);
            return free > 0 ? $"Brick Tycoon: claim a free plot! ({free} left)" : "Brick Tycoon: every plot is taken";
        }

        // ---------------------------------------------------------------- rush hour
        void UpdateRushHour(float now) {
            bool anyOwner = false;
            foreach (Plot plot in plots) anyOwner |= plot.owner != null;
            switch (rushHour.Tick(now, Random.value, anyOwner)) {
                case TY_RushChange.Started: AnnounceRush(true); break;
                case TY_RushChange.Ended: AnnounceRush(false); break;
            }
        }

        /// <summary>Tells the whole server (chat and every player's HUD) that a rush hour started or ended.</summary>
        void AnnounceRush(bool started) {
            string text = started
                ? $"RUSH HOUR! Every tycoon earns x{rushHour.multiplier:0.#} for {rushHour.duration:0} seconds. Keep emptying your collector!"
                : "Rush hour is over. Income is back to normal.";
            ServerChat.SendSystemMessage(new(text, SystemMessageSource.CustomMessage));
            // Wallets first: the HUD's timer and the banner's multiplier come from the server's tuning.
            foreach (var connection in players.Keys) SendWallet(connection);
            TY_EventKind kind = started ? TY_EventKind.RushHour : TY_EventKind.RushHourOver;
            long seconds = started ? (long)Math.Ceiling(rushHour.duration) : 0;
            foreach (var pair in players) Event(pair.Key, kind, pair.Value.plot, -1, seconds, text);
            plotsDirty = true;
        }

        void Earn(Plot plot, float dt, float now) {
            if (plot.owner == null || !players.TryGetValue(plot.owner, out var t)) return;
            long capacity = TY_Catalog.Capacity(plot.purchased, t.perks);
            bool wasFull = plot.bank >= capacity;
            plot.bank = Math.Min(capacity, plot.bank + IncomeOf(plot, t) * dt);
            if (TY_Catalog.AutoCollects(plot.purchased)) {
                long amount = (long)Math.Floor(plot.bank);
                if (amount > 0) { plot.bank -= amount; Pay(plot.owner, t, amount); }
            } else if (!wasFull && plot.bank >= capacity && now - t.fullRemindedAt > fullReminder) {
                t.fullRemindedAt = now;
                Event(plot.owner, TY_EventKind.CollectorFull, t.plot, -1, capacity, "Your collector is full! Step on the blue pad to bank it.");
            }

            if (plot.golden && now > plot.goldenUntil) { plot.golden = false; plotsDirty = true; }
            if (!plot.golden && now >= plot.nextGolden) {
                Rect area = TY_Catalog.GoldenArea;
                plot.goldenPosition = new Vector3(Random.Range(area.xMin, area.xMax), 0f, Random.Range(area.yMin, area.yMax));
                plot.golden = true;
                plot.goldenUntil = now + goldenLifetime;
                plot.nextGolden = now + Random.Range(goldenInterval.x, goldenInterval.y);
                plotsDirty = true;
                Event(plot.owner, TY_EventKind.Info, t.plot, -1, 0, "A gold coin appeared on your plot! Grab it before it vanishes.");
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
                if (plot.golden && map.GoldenZone(t.plot, plot.goldenPosition).ContainsFootprint(feet)) GrabGolden(pair.Key, t, plot);
                int pads = 0;
                foreach (TY_ItemKind lane in Lanes) {
                    int item = lane == TY_ItemKind.Rebirth ? TY_Catalog.RebirthIndex : TY_Catalog.NextInLane(plot.purchased, lane);
                    if (item < 0 || (lane == TY_ItemKind.Rebirth && !TY_Catalog.Available(plot.purchased, item))) continue;
                    if (!map.ButtonZone(t.plot, item).ContainsFootprint(feet)) continue;
                    int bit = 1 << (int)lane;
                    pads |= bit;
                    // Only on stepping onto the pad, so standing still never buys the whole lane in a row.
                    if ((t.padsUnderFeet & bit) == 0) TryBuy(pair.Key, t, plot, item);
                }
                t.padsUnderFeet = pads;
            }
            if (plotsDirty) BroadcastPlots();
        }

        void Claim(NetworkConnection connection, Tycooner t, int index) {
            plots[index] = new Plot {
                owner = connection, purchased = TY_Catalog.StarterMask,
                nextGolden = Time.time + Random.Range(goldenInterval.x, goldenInterval.y) * 0.5f
            };
            t.plot = index;
            RestoreBuild(connection, index);
            plotsDirty = true;
            Event(connection, TY_EventKind.Claimed, index, -1, 0, "You claimed a tycoon! Step on the glowing pads to build.");
            ServerChat.SendSystemMessage(new($"{PlayerData.GetPlayerName(connection)} claimed tycoon #{index + 1}.", SystemMessageSource.CustomMessage));
            SendWallet(connection);
        }

        void Release(int index) {
            plots[index] = new Plot();
            plotsDirty = true;
        }

        void Pay(NetworkConnection connection, Tycooner t, long amount) {
            t.cash += amount;
            t.lifetime += amount;
            plotsDirty = true;
            if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
        }

        void Collect(NetworkConnection connection, Tycooner t, Plot plot) {
            long amount = (long)Math.Floor(plot.bank);
            if (amount <= 0) return;
            plot.bank -= amount;
            Pay(connection, t, amount);
            Event(connection, TY_EventKind.Collected, t.plot, -1, amount, $"+${TY_Catalog.Money(amount)}");
            SendWallet(connection);
        }

        void GrabGolden(NetworkConnection connection, Tycooner t, Plot plot) {
            plot.golden = false;
            long reward = Math.Max(goldenMinimum, (long)(IncomeOf(plot, t) * goldenIncomeSeconds));
            Pay(connection, t, reward);
            Event(connection, TY_EventKind.GoldenBrick, t.plot, -1, reward, $"Gold coin! +${TY_Catalog.Money(reward)}");
            SendWallet(connection);
        }

        /// <summary>Rate-limits refusal messages per item (players often hop on and off a pad).</summary>
        bool Refuse(NetworkConnection connection, Tycooner t, int index) {
            if (t.refusedAt.TryGetValue(index, out float at) && Time.time - at < refusalCooldown) return false;
            t.refusedAt[index] = Time.time;
            return true;
        }

        void TryBuy(NetworkConnection connection, Tycooner t, Plot plot, int index) {
            var item = TY_Catalog.Items[index];
            if (!TY_Catalog.Available(plot.purchased, index)) {
                if (Refuse(connection, t, index) && item.prereq >= 0)
                    Event(connection, TY_EventKind.TooPoor, t.plot, index, 0, $"Build {TY_Catalog.Items[item.prereq].name} first to unlock {item.name}.");
                return;
            }
            if (t.cash < item.price) {
                if (!Refuse(connection, t, index)) return;
                long missing = (long)Math.Ceiling(item.price - t.cash);
                Event(connection, TY_EventKind.TooPoor, t.plot, index, missing, $"You need ${TY_Catalog.Money(missing)} more for {item.name}.");
                return;
            }
            t.cash -= item.price;
            if (item.kind == TY_ItemKind.Rebirth) {
                t.rebirths++;
                t.tokens += TY_Perks.TokensPerRebirth;
                t.cash = startingCash + TY_Perks.StartingCash(t.perks);
                plot.purchased = TY_Catalog.StarterMask;
                plot.bank = 0;
                Event(connection, TY_EventKind.Rebirth, t.plot, index, t.rebirths,
                    $"REBIRTH {t.rebirths}! Income x{TY_Catalog.RebirthMultiplier(t.rebirths):0.#} and +{TY_Perks.TokensPerRebirth} perk token.");
                ServerChat.SendSystemMessage(new($"{PlayerData.GetPlayerName(connection)} rebirthed! (x{t.rebirths})", SystemMessageSource.CustomMessage));
                Award(connection, rebirthXP, rebirthGold);
            } else {
                plot.purchased |= 1ul << index;
                Event(connection, TY_EventKind.Bought, t.plot, index, item.price, $"Built {item.name}! {item.Effect}");
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

        void OnPerkRequest(NetworkConnection connection, TY_PerkRequest request, Channel channel) {
            if (!connection.IsAuthenticated || !players.TryGetValue(connection, out var t)) return;
            if ((int)request.perk >= TY_Perks.All.Length) return;
            var perk = TY_Perks.Get(request.perk);
            int level = t.perks.Level(request.perk);
            if (level >= perk.maxLevel) return;
            int cost = perk.Cost(level);
            if (t.tokens < cost) {
                Event(connection, TY_EventKind.TooPoor, t.plot, -1, cost - t.tokens, $"{perk.name} needs {cost} tokens. Rebirth to earn more.");
                return;
            }
            t.tokens -= cost;
            t.perks = t.perks.WithLevel(request.perk, level + 1);
            if (PlayerData.TryGetPlayerData(connection, out var player)) ApplySpeed(player, t);
            Event(connection, TY_EventKind.Perk, t.plot, (int)request.perk, level + 1, $"{perk.name} level {level + 1}: {perk.Effect(level + 1)}");
            plotsDirty = true;
            SendWallet(connection);
        }

        void SendState(NetworkConnection connection) {
            if (map == null || connection == null || !connection.IsActive) return;
            InstanceFinder.ServerManager.Broadcast(connection, MakePlots());
            SendWallet(connection);
        }

        TY_PlotsBroadcast MakePlots() => new() {
            plots = plots.Select((p, i) => {
                Tycooner t = p.owner != null && players.TryGetValue(p.owner, out var owner) ? owner : null;
                return new TY_PlotState {
                    plot = (byte)i,
                    ownerId = p.owner != null ? p.owner.ClientId : -1,
                    ownerName = p.owner != null ? PlayerData.GetPlayerName(p.owner) : "",
                    purchased = p.purchased,
                    bank = (long)Math.Floor(p.bank),
                    capacity = t != null ? TY_Catalog.Capacity(p.purchased, t.perks) : TY_Catalog.BaseCapacity,
                    rebirths = t?.rebirths ?? 0,
                    income = t != null ? (float)IncomeOf(p, t) : 0f,
                    golden = p.golden,
                    goldenPosition = p.goldenPosition,
                };
            }).ToArray()
        };

        void BroadcastPlots() {
            plotsDirty = false;
            if (map != null) InstanceFinder.ServerManager.Broadcast(MakePlots());
        }

        void SendWallet(NetworkConnection connection) {
            if (connection == null || !connection.IsActive || !players.TryGetValue(connection, out var t)) return;
            ulong purchased = t.plot >= 0 ? plots[t.plot].purchased : 0;
            InstanceFinder.ServerManager.Broadcast(connection, new TY_WalletBroadcast {
                cash = (long)Math.Floor(t.cash), plot = t.plot, rebirths = t.rebirths,
                income = t.plot >= 0 ? (float)IncomeOf(plots[t.plot], t) : 0f,
                multiplier = (float)(TY_Catalog.Multiplier(purchased, t.rebirths, t.perks) * EventScale),
                tokens = t.tokens, perks = t.perks, lifetime = (long)t.lifetime,
                rushSeconds = rushHour.Remaining(Time.time), rushMultiplier = rushHour.multiplier,
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
        /// <summary>Gives a player cash (test probes only: no command, so players cannot cheat a saved economy).</summary>
        public void GrantCash(NetworkConnection connection, long amount) {
            Pay(connection, For(connection), amount);
            SendWallet(connection);
        }

        /// <summary>Gives a player rebirth tokens (test probes only, like <see cref="GrantCash"/>).</summary>
        public void GrantTokens(NetworkConnection connection, int amount) {
            For(connection).tokens += amount;
            SendWallet(connection);
        }

        /// <summary>Starts a rush hour now (the StartRushHour command, see <see cref="TY_Commands"/>).</summary>
        public void StartRushHour() {
            rushHour.Begin(Time.time);
            AnnounceRush(true);
        }

        /// <summary>Income multiplier of rush hours (the RushMultiplier command).</summary>
        public float RushMultiplier => rushHour.multiplier;

        public bool RushHourActive => rushHour.IsActive(Time.time);

        /// <summary>Changes the rush-hour multiplier; a running rush hour pays the new rate at once.</summary>
        public void SetRushMultiplier(float value) {
            rushHour.multiplier = value;
            rushHour.Validate();
            foreach (var connection in players.Keys) SendWallet(connection);
        }

        protected override void OnDestroy() {
            TY_Commands.Unregister(this);
            GameCharacter.FallHeightOverride = null;
            PlayerData.OnPlayerRemoved -= OnPlayerLeft;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnPosition) ServerPlayerCharacter.SpawnLocationFunction = null;
            if (InstanceFinder.ServerManager != null) {
                InstanceFinder.ServerManager.UnregisterBroadcast<TY_StateRequest>(OnStateRequest);
                InstanceFinder.ServerManager.UnregisterBroadcast<TY_PerkRequest>(OnPerkRequest);
            }
            base.OnDestroy();
        }
    }
}
