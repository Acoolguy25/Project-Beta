using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Retention;
using RyanAssets.Retention.Server;
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
    /// token for the perk shop; rebirths open landmarks that stay through every rebirth after. A pet
    /// crate hatches collectible pets that follow their owner and boost income for good (Pets partial). A goal
    /// chain, a daily chest, offline earnings and the shared retention kit (levels with unlocks, daily
    /// challenges, streak, a summary card at each rebirth) give a next goal at every moment (see the
    /// Retention and Progress partials). Every rule (cash, prices, unlocks, claims, pickups) is decided
    /// here from replicated character positions; clients only draw.
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
        [SerializeField] TY_GoldCoins goldCoins = new();

        [Header("Rush hour")]
        [Tooltip("Timed server event: every few minutes every tycoon earns more for a short while.")]
        [SerializeField] TY_RushHour rushHour = new();
        [Tooltip("Test servers only: registers the StartRushHour and RushMultiplier commands (see TY_Commands). The command "
            + "service has no permissions, so any player could run them, and rush earnings are saved. Keep it off when shipping.")]
        [SerializeField] bool rushCommands;

        [Header("Pet crate")]
        [Tooltip("Brick Buddies: the crate's price, rarity odds and pity counter (see TY_Pets).")]
        [SerializeField] TY_PetCrate petCrate = new();

        [Header("Rewards (game XP through the retention kit, which also pays account XP and gold)")]
        [SerializeField] TY_RewardTuning rewards = new();

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
            /// <summary>Current goal of <see cref="TY_Goals"/>; lifetime coin and collection counts (saved).</summary>
            public int goalIndex, coins, collects;
            /// <summary>Day the daily chest was last opened (saved).</summary>
            public int chestDay = RetentionDay.None;
            /// <summary>The save has been applied: the chest and offline earnings wait for it, so they never pay twice.</summary>
            public bool loaded;
            // The time the save was last written, kept until offline earnings can be paid (-1: nothing pending).
            public long offlineSince = -1;
            public ulong offlineBuilt;
            /// <summary>The "welcome back" event, held until the client listens (it is sent once, on join).</summary>
            public readonly TY_HeldMessage<TY_EventBroadcast> welcome = new();
            /// <summary>Investment levels bought (saved; each adds income, kept through rebirths).</summary>
            public int invested;
            /// <summary>The best income (rush hours left out) this player's plot has reached, saved: the daily chest pays from it.</summary>
            public double peakIncome;
            /// <summary>Level of every pet (saved, kept through rebirths), crates opened, and crates since the last Epic or better.</summary>
            public byte[] pets = new byte[TY_Pets.Count];
            public int hatches, sinceEpic;
            public bool onCrate;
            /// <summary>Goal stats of this frame (<see cref="statsFrame"/>), so a tick computes them once; -1 = stale.</summary>
            public TY_GoalStats stats;
            public int statsFrame = -1;
            /// <summary>Seconds on a claimed plot this session, and whole minutes of it already reported.</summary>
            public float plotSeconds;
            public int minutesReported;
            public bool streakClaimed;
            /// <summary>Cash earned since the last report to the daily challenges (reported once a second).</summary>
            public long unreportedEarnings;
            public int plot = -1;
            public bool onCollector;
            /// <summary>Lanes whose pad the player stood on last tick: a pad buys once per step onto it.</summary>
            public int padsUnderFeet;
            public float fullRemindedAt = float.NegativeInfinity;
            public readonly Dictionary<int, float> refusedAt = new();
        }

        readonly Dictionary<NetworkConnection, Tycooner> players = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        /// <summary>Clients that asked for the state, so their handlers are registered and events are not lost.</summary>
        readonly HashSet<NetworkConnection> listeners = new();
        /// <summary>Reused every tick to walk the players while the dictionary may change.</summary>
        readonly List<NetworkConnection> connectionBuffer = new();
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
            petCrate.Validate();
            ValidateRetentionTuning();
            retention = new RetentionService<TY_SaveData>(TY_Retention.Rules(retentionTuning, unlockTuning, offline), save => save.retention);
            retention.Ready += OnRetentionReady;
        }

        void OnValidate() {
            rushHour?.Validate();
            petCrate?.Validate();
            ValidateRetentionTuning();
        }

        Tycooner For(NetworkConnection connection) {
            if (!players.TryGetValue(connection, out var t)) {
                players[connection] = t = new Tycooner { cash = startingCash };
                // The client may have asked for the state before the server added the player.
                if (listeners.Contains(connection)) t.welcome.MarkListening();
            }
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
            // After the tycoon's own load started: the kit finds the document loading and waits for it.
            retention.PlayerJoined(player);
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
            listeners.Remove(player.Owner);
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

        /// <summary>Cash per second the plot earns right now (rush hour included).</summary>
        double IncomeOf(Plot plot, Tycooner t) => BaseIncomeOf(plot.purchased, t, plot.owner) * RushMultiplierFor(plot.owner);

        /// <summary>Income of a build without rush hours: the catalog's, the tuning scale, investments, pets and the player's level unlocks.</summary>
        double BaseIncomeOf(ulong purchased, Tycooner t, NetworkConnection owner) =>
            TY_Catalog.Income(purchased, t.rebirths, t.perks) * BoostsOf(t, owner);

        /// <summary>The player's own multipliers on top of the catalog (tuning scale, investments, pets, level unlocks).</summary>
        double BoostsOf(Tycooner t, NetworkConnection owner) =>
            incomeScale * TY_Catalog.InvestMultiplier(t.invested) * TY_Pets.Multiplier(t.pets) * LevelIncomeMultiplier(owner);

        /// <summary>Everything on top of dropper income, as the wallet shows it.</summary>
        double MultiplierOf(ulong purchased, Tycooner t, NetworkConnection owner) =>
            TY_Catalog.Multiplier(purchased, t.rebirths, t.perks) * BoostsOf(t, owner) * RushMultiplierFor(owner);

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
                connectionBuffer.Clear();
                connectionBuffer.AddRange(players.Keys);
                foreach (var connection in connectionBuffer)
                    if (players.TryGetValue(connection, out var t)) TickPlayer(connection, t, dt);
                BroadcastPlots();
                foreach (var connection in connectionBuffer) SendWallet(connection);
                await AwaitTime(1000, token);
            }
        }

        /// <summary>The shared top line: the running rush hour, or the countdown to the next one (something to wait for).</summary>
        string TopMessage(float now) {
            if (rushHour.IsActive(now))
                return $"RUSH HOUR: every tycoon earns x{rushHour.multiplier:0.#} for {Mathf.CeilToInt(rushHour.Remaining(now))} s!";
            int free = plots.Count(p => p.owner == null);
            string plotsLine = free > 0 ? $"{free} free plot{(free == 1 ? "" : "s")}" : "every plot is taken";
            float untilRush = rushHour.NextAt - now;
            if (float.IsInfinity(untilRush) || untilRush < 0f) return $"Brick Tycoon: {plotsLine}";
            int seconds = Mathf.CeilToInt(untilRush);
            return $"Next RUSH HOUR in {seconds / 60}:{seconds % 60:00}  ·  {plotsLine}";
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
            foreach (var pair in players) {
                Event(pair.Key, kind, pair.Value.plot, -1, seconds, text);
                if (!started && pair.Value.plot >= 0) retention.Count(pair.Key, TY_Retention.StatRush);
            }
            plotsDirty = true;
        }

        void Earn(Plot plot, float dt, float now) {
            if (plot.owner == null || !players.TryGetValue(plot.owner, out var t)) return;
            long capacity = CapacityOf(plot, t);
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
                plot.goldenUntil = now + goldCoins.lifetime;
                plot.nextGolden = now + GoldenWait(plot.owner);
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
                if (ChestReady(t) && map.ChestZone(t.plot).ContainsFootprint(feet)) OpenChest(pair.Key, t, plot);
                // Like the collector: one crate per step onto its pad.
                bool onCrate = map.CrateZone(t.plot).ContainsFootprint(feet);
                if (onCrate && !t.onCrate) TryHatch(pair.Key, t, plot);
                t.onCrate = onCrate;
                int pads = 0;
                foreach (TY_ItemKind lane in TY_Lanes.PadLanes) {
                    int item = TY_Catalog.PadItem(plot.purchased, lane, t.rebirths);
                    if (item < 0) continue;
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
                nextGolden = Time.time + GoldenWait(connection) * 0.5f
            };
            t.plot = index;
            RestoreBuild(connection, index);
            plotsDirty = true;
            // A tycoon's "match" runs from claiming a plot (or the last rebirth) to the next rebirth.
            retention.BeginMatch(connection);
            Event(connection, TY_EventKind.Claimed, index, -1, 0, "You claimed a tycoon! Step on the glowing pads to build.");
            ServerChat.SendSystemMessage(new($"{PlayerData.GetPlayerName(connection)} claimed tycoon #{index + 1}.", SystemMessageSource.CustomMessage));
            SendWallet(connection);
        }

        void Release(int index) {
            plots[index] = new Plot();
            plotsDirty = true;
        }

        void Pay(NetworkConnection connection, Tycooner t, long amount) {
            t.statsFrame = -1;
            t.cash += amount;
            t.lifetime += amount;
            t.unreportedEarnings += amount;
            plotsDirty = true;
            if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
        }

        void Collect(NetworkConnection connection, Tycooner t, Plot plot) {
            long amount = (long)Math.Floor(plot.bank);
            if (amount <= 0) return;
            plot.bank -= amount;
            Pay(connection, t, amount);
            t.collects++;
            retention.Count(connection, TY_Retention.StatCollect);
            Event(connection, TY_EventKind.Collected, t.plot, -1, amount, $"+${TY_Catalog.Money(amount)}");
            SendWallet(connection);
        }

        void GrabGolden(NetworkConnection connection, Tycooner t, Plot plot) {
            plot.golden = false;
            float lucky = Has(connection, TY_Retention.LuckyCoins) ? unlockTuning.luckyCoinsMultiplier : 1f;
            long reward = goldCoins.Reward(IncomeOf(plot, t), lucky);
            Pay(connection, t, reward);
            t.coins++;
            retention.Count(connection, TY_Retention.StatCoins);
            retention.AddXp(connection, rewards.coinXp, "Gold coins");
            Event(connection, TY_EventKind.GoldenBrick, t.plot, -1, reward, $"Gold coin! +${TY_Catalog.Money(reward)}" + (lucky > 1f ? " (Lucky Coins)" : ""));
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
            if (!TY_Catalog.Available(plot.purchased, index, t.rebirths)) {
                if (!Refuse(connection, t, index)) return;
                string reason = TY_Catalog.LockReason(plot.purchased, index, t.rebirths);
                if (!string.IsNullOrEmpty(reason)) Event(connection, TY_EventKind.TooPoor, t.plot, index, 0, $"{item.name}: {reason}.");
                return;
            }
            long price = TY_Catalog.Price(index, t.rebirths, t.invested);
            if (t.cash < price) {
                if (!Refuse(connection, t, index)) return;
                long missing = (long)Math.Ceiling(price - t.cash);
                Event(connection, TY_EventKind.TooPoor, t.plot, index, missing, $"You need ${TY_Catalog.Money(missing)} more for {item.name}.");
                return;
            }
            t.cash -= price;
            t.statsFrame = -1;
            if (item.kind == TY_ItemKind.Rebirth) Rebirth(connection, t, plot);
            else if (item.kind == TY_ItemKind.Invest) {
                t.invested++;
                Event(connection, TY_EventKind.Bought, t.plot, index, price,
                    $"Invested ${TY_Catalog.Money(price)}! Income x{TY_Catalog.InvestMultiplier(t.invested):0.##} for good.");
            } else {
                plot.purchased |= 1ul << index;
                string text = item.kind == TY_ItemKind.Landmark
                    ? $"LANDMARK! {item.name} built: {item.Effect}, through every rebirth."
                    : $"Built {item.name}! {item.Effect}";
                Event(connection, TY_EventKind.Bought, t.plot, index, price, text);
                retention.Count(connection, TY_Retention.StatBuilds);
            }
            NotePeakIncome(connection, t, plot);
            plotsDirty = true;
            if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
            SendWallet(connection);
        }

        /// <summary>Raises the player's saved best income to what their plot earns now (after every purchase), and returns it.</summary>
        double NotePeakIncome(NetworkConnection connection, Tycooner t, Plot plot) =>
            t.peakIncome = Math.Max(t.peakIncome, BaseIncomeOf(plot.purchased, t, connection));

        // ---------------------------------------------------------------- messages
        /// <summary>
        /// Resets the plot for a permanent multiplier and perk tokens. Landmarks (and, with Head Office,
        /// the collector lane) stay; the run's summary card closes the "match" and a new one begins.
        /// </summary>
        void Rebirth(NetworkConnection connection, Tycooner t, Plot plot) {
            int extraTokens = Has(connection, TY_Retention.TokenPress) ? unlockTuning.tokenPressTokens : 0;
            TY_RebirthResult result = rewards.Rebirth(t.rebirths, plot.purchased, Has(connection, TY_Retention.HeadOffice), extraTokens);
            t.rebirths = result.rebirths;
            t.tokens += result.tokens;
            t.cash = startingCash + TY_Perks.StartingCash(t.perks);
            plot.purchased = result.kept;
            plot.bank = 0;
            t.statsFrame = -1;
            string opened = result.landmark >= 0 ? $" The {TY_Catalog.Items[result.landmark].name} landmark is now for sale!" : "";
            Event(connection, TY_EventKind.Rebirth, t.plot, TY_Catalog.RebirthIndex, t.rebirths,
                $"REBIRTH {t.rebirths}! Income x{TY_Catalog.RebirthMultiplier(t.rebirths):0.#} and +{result.tokens} perk token{(result.tokens == 1 ? "" : "s")}.{opened}");
            ServerChat.SendSystemMessage(new($"{PlayerData.GetPlayerName(connection)} rebirthed! (x{t.rebirths})", SystemMessageSource.CustomMessage));
            retention.Count(connection, TY_Retention.StatRebirth);
            // Goals the rebirth meets ("Rebirth 2 times") are paid in this run, so the card counts their XP
            // and ends on the level the chip shows after it; their toasts wait for the card on the client.
            rebirthLines.Clear();
            rebirthLines.Add(RewardLine.Headline(result.Label, result.xp, result.gold, result.detail));
            CompleteMetGoals(connection, t, MaxGoalsAtRebirth, rebirthLines);
            retention.FinishMatch(connection, $"Rebirth {t.rebirths}!", rebirthLines);
            retention.BeginMatch(connection);
        }

        /// <summary>Most goals a rebirth completes at once (a veteran's tail goals can line up).</summary>
        const int MaxGoalsAtRebirth = 4;
        /// <summary>The rebirth card's pinned lines: the rebirth first, then the goals it met (reused).</summary>
        readonly List<RewardLine> rebirthLines = new();

        void OnStateRequest(NetworkConnection connection, TY_StateRequest request, Channel channel) {
            if (!connection.IsAuthenticated) return;
            // The client asks once its handlers are registered (possibly before the server added the
            // player): from now on events reach it, so a held welcome-back event can go out.
            if (listeners.Add(connection)) {
                // The welcome first: the client holds the card's cash back from the wallet that follows.
                if (players.TryGetValue(connection, out Tycooner listener)) {
                    listener.welcome.MarkListening();
                    SendWelcome(connection, listener);
                }
                SendState(connection);
                return;
            }
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
                int pet = t != null ? TY_Pets.Best(t.pets) : -1;
                return new TY_PlotState {
                    plot = (byte)i,
                    ownerId = p.owner != null ? p.owner.ClientId : -1,
                    ownerName = p.owner != null ? PlayerData.GetPlayerName(p.owner) : "",
                    purchased = p.purchased,
                    bank = (long)Math.Floor(p.bank),
                    capacity = t != null ? CapacityOf(p, t) : TY_Catalog.BaseCapacity,
                    rebirths = t?.rebirths ?? 0,
                    income = t != null ? (float)IncomeOf(p, t) : 0f,
                    golden = p.golden,
                    goldenPosition = p.goldenPosition,
                    pet = (sbyte)pet,
                    petLevel = pet >= 0 ? t.pets[pet] : (byte)0,
                };
            }).ToArray()
        };

        void BroadcastPlots() {
            plotsDirty = false;
            if (map != null) InstanceFinder.ServerManager.Broadcast(MakePlots());
        }

        void SendWallet(NetworkConnection connection) {
            if (connection == null || !connection.IsActive || !players.TryGetValue(connection, out var t)) return;
            bool hasPlot = OwnsPlot(connection, t);
            ulong purchased = hasPlot ? plots[t.plot].purchased : 0;
            TY_Goal goal = TY_Goals.Get(t.goalIndex);
            DateTime now = DateTime.UtcNow;
            bool chestReady = ChestReady(t);
            int chestDay = ChestStreakDay(connection, t, now);
            InstanceFinder.ServerManager.Broadcast(connection, new TY_WalletBroadcast {
                cash = (long)Math.Floor(t.cash), plot = t.plot, rebirths = t.rebirths,
                income = hasPlot ? (float)IncomeOf(plots[t.plot], t) : 0f,
                multiplier = (float)MultiplierOf(purchased, t, connection),
                tokens = t.tokens, perks = t.perks, lifetime = (long)t.lifetime,
                rushSeconds = rushHour.Remaining(Time.time),
                rushMultiplier = rushHour.IsActive(Time.time) ? RushMultiplierFor(connection) : rushHour.multiplier,
                goalIndex = t.goalIndex, goalProgress = GoalStats(connection, t).Get(goal.stat), goalReward = GoalCash(connection, t, goal),
                chestReady = chestReady, chestSeconds = chestReady ? 0f : RetentionDay.SecondsUntilReset(now),
                chestStreak = chestDay, invested = t.invested, chestMinutes = chest.Minutes(chestDay), chestTokens = chest.Tokens(chestDay),
                pets = t.pets, cratePrice = CratePrice(connection, t), crateUntilPity = petCrate.UntilPity(t.sinceEpic),
            });
        }

        bool OwnsPlot(NetworkConnection connection, Tycooner t) => t.plot >= 0 && t.plot < plots.Length && plots[t.plot].owner == connection;

        static void Event(NetworkConnection connection, TY_EventKind kind, int plot, int item, long amount, string text, long extra = 0) {
            if (connection == null || !connection.IsActive) return;
            InstanceFinder.ServerManager.Broadcast(connection, new TY_EventBroadcast { kind = kind, plot = plot, item = item, amount = amount, text = text, extra = extra });
        }

        void WriteLeaderboard(PlayerData player) {
            if (!players.TryGetValue(player.Owner, out var t)) return;
            RoundLeaderboard.Set(player, CashColumn, (int)Math.Min(int.MaxValue, Math.Floor(t.cash)));
            RoundLeaderboard.Set(player, RebirthColumn, t.rebirths);
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
            if (retention != null) retention.Ready -= OnRetentionReady;
            retention?.Dispose();
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
