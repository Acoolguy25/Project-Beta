using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Levels.Server;
using RyanAssets.Levels.Shared;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Progress;
using RyanAssets.Server.ServerCore;
using UnityEngine;
using FishNet.Transporting;
using Channel = FishNet.Transporting.Channel;

namespace RyanAssets.Retention.Server {
    /// <summary>
    /// Server side of the retention kit for one game: per-game XP and levels, daily challenges, the
    /// play streak and the end-of-match summary, kept in the <see cref="RetentionSave"/> block of the
    /// game's own <typeparamref name="TSave"/> document and announced to each player's HUD.
    /// <para>
    /// The game's server runner creates one in Awake and disposes it in OnDestroy, calls
    /// <see cref="PlayerJoined"/> from OnPlayerAdded, and reports what happens: <see cref="Count"/> /
    /// <see cref="Reach"/> for challenge stats, <see cref="AddXp"/> for XP earned during play,
    /// <see cref="BeginMatch"/> / <see cref="FinishMatch"/> around each match. Every call is
    /// server-side; the only client message (a snapshot request) is rate-limited.
    /// </para>
    /// <para>
    /// Changes go through <see cref="PlayerProgress.Modify{T}(PlayerData, Action{T})"/>, so they are
    /// saved with the game's document (and queued while it loads); a challenge completion or a streak
    /// claim also saves the document and the account at once. A player whose document could not be
    /// loaded plays on defaults and nothing is saved; their daily rewards pay no account XP or gold.
    /// Game XP also pays account XP (scaled by <see cref="RetentionTuning.accountXpScale"/>) and reward
    /// gold pays account gold through <see cref="LevelsServer"/>, so route rewards through the kit
    /// instead of also calling LevelsServer for them.
    /// </para>
    /// <para>
    /// <see cref="Count"/> and <see cref="Reach"/> are cheap enough for every kill or tick: a stat no
    /// unfinished challenge of the day counts returns at once (no allocation, no document change), and
    /// progress is applied and sent at most every <see cref="ProgressFlushInterval"/> seconds, at once
    /// when it completes a challenge.
    /// </para>
    /// </summary>
    /// <example><code>
    /// retention = new RetentionService&lt;TD_SaveData&gt;(TD_Retention.Rules(tuning), save =&gt; save.retention);
    /// retention.PlayerJoined(player);                       // OnPlayerAdded
    /// retention.Count(conn, "kills");                       // an enemy destroyed
    /// retention.FinishMatch(conn, "Victory!", new RewardLine("Victory", 100), new RewardLine("Waves held", 5 * wave));
    /// retention.Dispose();                                  // OnDestroy
    /// </code></example>
    public sealed class RetentionService<TSave> : IDisposable where TSave : ProgressData, new() {
        /// <summary>Seconds after UTC midnight before the rollover snapshot goes out (clock drift margin).</summary>
        const double RolloverMarginSeconds = 2d;
        /// <summary>Wait after an unexpected rollover failure before trying again.</summary>
        const double RolloverRetrySeconds = 10d;

        sealed class Player : RetentionPlayer {
            public readonly PlayerData Data;
            public readonly NetworkConnection Connection;
            /// <summary>Session-only document for a player whose progress is not tracked (no valid player id).</summary>
            public TSave Detached;
            public float NextRequestTime;
            public bool Left, IsReady, SaveDue, Queued;

            public Player(PlayerData data) {
                Data = data;
                Connection = data.Owner;
            }
        }

        /// <summary>Connects the ledger to FishNet, LevelsServer and PlayerProgress.</summary>
        sealed class Sink : IRetentionSink {
            readonly RetentionService<TSave> service;

            public Sink(RetentionService<TSave> service) => this.service = service;

            public void Send<T>(RetentionPlayer player, T message) where T : struct, IBroadcast {
                var state = (Player)player;
                NetworkConnection connection = state.Connection;
                if (state.Left || connection == null || !connection.IsActive || InstanceFinder.ServerManager == null) return;
                InstanceFinder.ServerManager.Broadcast(connection, message, true, Channel.Reliable);
            }

            public void PayAccount(RetentionPlayer player, int accountXp, int gold) {
                var state = (Player)player;
                PlayerData data = state.Data;
                if (data == null) return;
                // LevelsServer warns on zero amounts.
                if (gold > 0) LevelsServer.AwardPlayerGold(data, (ulong)gold);
                if (accountXp <= 0) return;
                state.NoteAccountLevel(LevelsCalc.GetRank(data.xp.Value));
                AddAccountXp(data, (ulong)accountXp);
                // One chat line per match: inside a match, the level is announced when it finishes.
                if (!state.InMatch) service.AnnounceLevel(state);
            }

            public void SaveSoon(RetentionPlayer player) => service.MarkSaveDue((Player)player);

            public void Unlocked(RetentionPlayer player, UnlockDefinition unlock) => service.RaiseUnlocked(((Player)player).Connection, unlock);
        }

        /// <summary>
        /// <see cref="LevelsServer.AwardPlayerXP(PlayerData, ulong)"/> without its chat line for every
        /// level crossed: a match pays account XP line by line, so the kit announces the final level once.
        /// </summary>
        static void AddAccountXp(PlayerData data, ulong xp) {
            try {
                data.xp.Value = checked(data.xp.Value + xp);
            } catch (OverflowException) {
                Debug.LogError($"Player {data.GetPlayerName()} would overflow their XP");
                data.xp.Value = ulong.MaxValue;
            }
            ServerPlayerSave.MarkDirty(data.Owner);
        }

        /// <summary>
        /// Chat line for an account level-up a payout brought: {0} = player name, {1} = account level.
        /// It says "account level" because games show their own level next to it.
        /// </summary>
        public string AccountLevelFormat { get; set; } = "{0} has reached account level {1}!";

        /// <summary>
        /// Seconds after joining during which account level-ups (from rewards paid as the player loads
        /// in: goals already met, a daily claim) move the baseline without a chat line. Joining never
        /// announces.
        /// </summary>
        public float JoinQuietSeconds { get; set; } = 15f;

        /// <summary>Says in chat that the player reached a new account level (the highest one only).</summary>
        void AnnounceLevel(Player state) {
            PlayerData data = state.Data;
            if (data == null) return;
            int level = LevelsCalc.GetRank(data.xp.Value);
            if (!state.TryAnnounceAccountLevel(level, utcNow())) return;
            if (InstanceFinder.ServerManager == null || !InstanceFinder.ServerManager.Started) return;
            string text;
            try {
                text = string.Format(AccountLevelFormat ?? string.Empty, data.GetPlayerName(), level);
            } catch (FormatException) {
                text = AccountLevelFormat;
            }
            ServerChat.SendSystemMessage(new(text, SystemMessageSource.PlayerLevelUp));
        }

        readonly Func<TSave, RetentionSave> blockOf;
        readonly Func<DateTime> utcNow;
        readonly RetentionLedger ledger;
        readonly Dictionary<NetworkConnection, Player> players = new();
        // Players with queued stats or a save due; drained by the tick loop.
        List<Player> queued = new();
        List<Player> draining = new();
        readonly Action<Player, RetentionSave> flushChange, snapshotChange;
        readonly CancellationTokenSource lifetime = new();
        bool disposed;

        /// <param name="rules">The game's rules; their universe id must be the one <typeparamref name="TSave"/> is loaded with.</param>
        /// <param name="blockOf">Reaches the retention block inside the game's document, e.g. <c>save =&gt; save.retention</c>.</param>
        /// <param name="utcNow">Clock override for tests; defaults to <see cref="DateTime.UtcNow"/>.</param>
        public RetentionService(RetentionRules rules, Func<TSave, RetentionSave> blockOf, Func<DateTime> utcNow = null) {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.blockOf = blockOf ?? throw new ArgumentNullException(nameof(blockOf));
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
            ledger = new RetentionLedger(rules, new Sink(this));
            flushChange = (state, block) => ledger.Flush(state, block, Today);
            snapshotChange = (state, block) => ledger.SendSnapshot(state, block, this.utcNow());
            PlayerData.OnPlayerRemoved += OnPlayerRemoved;
            if (InstanceFinder.ServerManager != null) {
                InstanceFinder.ServerManager.RegisterBroadcast<RetentionSnapshotRequest>(OnSnapshotRequest);
                InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            } else
                Debug.LogError("RetentionService was created before the ServerManager exists; clients cannot ask for snapshots. Create it in the runner's Awake.");
            RolloverLoop(lifetime.Token).Forget();
            TickLoop(lifetime.Token).Forget();
        }

        public RetentionRules Rules { get; }

        /// <summary>Shortest time between two snapshot requests from one client that are answered (seconds).</summary>
        public float SnapshotRequestInterval { get; set; } = 1f;

        /// <summary>
        /// Most seconds challenge progress from <see cref="Count"/> / <see cref="Reach"/> waits before it
        /// is applied and sent (a completion never waits).
        /// </summary>
        public float ProgressFlushInterval { get; set; } = 0.25f;

        /// <summary>Raised on the server when a player reaches an unlock (once per unlock, as it happens).</summary>
        public event Action<NetworkConnection, UnlockDefinition> Unlocked;

        /// <summary>
        /// Raised once per join when the player's retention progress is known (their document loaded, or
        /// they play on defaults), with their level: the moment to apply loadouts or perks gated on
        /// <see cref="IsUnlocked"/>. It can be raised during <see cref="PlayerJoined"/> when the document
        /// was already loaded.
        /// </summary>
        public event Action<NetworkConnection, int> Ready;

        int Today => RetentionDay.Of(utcNow());

        // ---------------------------------------------------------------- players

        /// <summary>
        /// Starts tracking a player (loading their document if the game has not yet) and sends their
        /// snapshot once it is loaded, then raises <see cref="Ready"/>. Call from the runner's OnPlayerAdded.
        /// </summary>
        public void PlayerJoined(PlayerData player) {
            if (player == null) throw new ArgumentNullException(nameof(player));
            Player state = Track(player);
            Change(state, snapshotChange);
        }

        /// <summary>Sends the player's full HUD state now (after the game changed something big, say).</summary>
        public void SendSnapshot(NetworkConnection connection) => Change(connection, snapshotChange);

        /// <summary>True once the player's progress is known (see <see cref="Ready"/>).</summary>
        public bool IsReady(NetworkConnection connection) =>
            connection != null && players.TryGetValue(connection, out Player state) && state.IsReady;

        /// <summary>The player's level, or 1 while their document is still loading (see <see cref="Ready"/>).</summary>
        public int LevelOf(NetworkConnection connection) =>
            TryRead(connection, out RetentionSave block) ? Rules.Track.LevelForXp(block.xp) : 1;

        /// <summary>
        /// Whether the player owns <paramref name="unlockId"/>. False while their document is still loading
        /// and for unknown ids, so gate content on it at use time or from <see cref="Ready"/>, not at join.
        /// </summary>
        public bool IsUnlocked(NetworkConnection connection, string unlockId) =>
            TryRead(connection, out RetentionSave block) && Rules.Track.IsUnlocked(Rules.Track.LevelForXp(block.xp), unlockId);

        // ---------------------------------------------------------------- rewards and stats

        /// <summary>
        /// Pays game XP (and account XP) now, for something done during play. Inside a match it also
        /// becomes a summary line; lines with the same <paramref name="reason"/> merge ("Kill x5").
        /// Each call sends one message, so batch per-tick XP in the game.
        /// </summary>
        /// <param name="announce">Show "+XP" by the level chip (the bar always moves).</param>
        public void AddXp(NetworkConnection connection, int xp, string reason, bool announce = true) {
            if (xp <= 0) return;
            Change(connection, (state, block) => ledger.AddXp(state, block, xp, reason, announce));
        }

        /// <summary>Adds <paramref name="amount"/> to today's challenges counting <paramref name="statKey"/>; a completed one pays at once.</summary>
        public void Count(NetworkConnection connection, string statKey, int amount = 1) {
            if (amount > 0) Stat(connection, statKey, amount, true);
        }

        /// <summary>Raises today's challenges on <paramref name="statKey"/> to at least <paramref name="value"/> (a best, e.g. the wave reached).</summary>
        public void Reach(NetworkConnection connection, string statKey, int value) {
            if (value > 0) Stat(connection, statKey, value, false);
        }

        /// <summary>
        /// Pays today's streak bonus now if it is unpaid, for games without matches (a tycoon after some
        /// minutes of play). Games with matches get it from <see cref="FinishMatch"/>.
        /// </summary>
        public void ClaimDailyStreak(NetworkConnection connection) =>
            Change(connection, (state, block) => ledger.ClaimDailyStreak(state, block, Today));

        // ---------------------------------------------------------------- matches

        /// <summary>
        /// Opens the player's match: the summary's "before" bar is taken now and XP from
        /// <see cref="AddXp"/> and completed challenges are listed on it. Call when a match starts (or a
        /// player joins one in progress). Calling it again restarts the summary.
        /// </summary>
        public void BeginMatch(NetworkConnection connection) =>
            Change(connection, (state, block) => ledger.BeginMatch(state, block));

        /// <summary>
        /// Ends the player's match: pays <paramref name="rewards"/> and, for the first finished match of
        /// the UTC day, the streak bonus; then sends the summary card and a fresh snapshot. Works without
        /// <see cref="BeginMatch"/> (the card then starts from the current bar). Empty lines are skipped;
        /// <see cref="RewardLine.Info"/> lines show on the card without paying.
        /// </summary>
        public void FinishMatch(NetworkConnection connection, string title, params RewardLine[] rewards) =>
            FinishMatch(connection, title, (IReadOnlyList<RewardLine>)rewards);

        /// <inheritdoc cref="FinishMatch(NetworkConnection, string, RewardLine[])"/>
        public void FinishMatch(NetworkConnection connection, string title, IReadOnlyList<RewardLine> rewards) {
            // Copy now: the change may run later (while the document loads) and the caller may reuse the list.
            RewardLine[] lines = rewards == null ? Array.Empty<RewardLine>() : new List<RewardLine>(rewards).ToArray();
            Change(connection, (state, block) => {
                ledger.FinishMatch(state, block, Today, utcNow(), title, lines);
                AnnounceLevel(state);
            });
        }

        // ---------------------------------------------------------------- internals

        void Stat(NetworkConnection connection, string statKey, int value, bool accumulate) {
            // Free first test: no challenge of the pool counts it (most per-tick stats).
            if (connection == null || disposed || !Rules.UsesStat(statKey)) return;
            if (!players.TryGetValue(connection, out Player state) || !TryBlock(state, out RetentionSave block)) {
                // Not tracked yet, or still loading: apply it as one queued change (rare).
                if (!Rules.CountsToday(Today, statKey)) return;
                Change(connection, (s, b) => ledger.Apply(s, b, Today, statKey, value, accumulate));
                return;
            }
            switch (ledger.Queue(state, block, Today, statKey, value, accumulate)) {
                case StatEffect.Completes:
                    Change(state, flushChange);
                    break;
                case StatEffect.Progress:
                    Enqueue(state);
                    break;
            }
        }

        void Enqueue(Player state) {
            if (state.Queued) return;
            state.Queued = true;
            queued.Add(state);
        }

        void MarkSaveDue(Player state) {
            state.SaveDue = true;
            Enqueue(state);
        }

        void SaveNow(Player state) {
            state.SaveDue = false;
            if (state.Left || !IsSaved(state)) return;
            SaveAsync(state.Data, state.Connection).Forget();
        }

        static async UniTaskVoid SaveAsync(PlayerData data, NetworkConnection connection) {
            try {
                // The document first: a lost account save then loses a reward instead of paying it twice.
                await PlayerProgress.Save(data);
                await ServerPlayerSave.Save(connection);
            } catch (Exception e) {
                Debug.LogException(e);
            }
        }

        void RaiseUnlocked(NetworkConnection connection, UnlockDefinition unlock) {
            try {
                Unlocked?.Invoke(connection, unlock);
            } catch (Exception e) {
                Debug.LogException(e);
            }
        }

        bool IsSaved(Player state) => state.Detached == null && PlayerProgress.GetState<TSave>(state.Data) == ProgressState.Loaded;

        Player Track(PlayerData data) {
            if (players.TryGetValue(data.Owner, out Player state) && state.Data == data) return state;
            state = new Player(data);
            players[data.Owner] = state;
            // The account level at join is the baseline: joining never announces, and rewards paid in
            // the first moments (as the game loads the player in) only move it.
            state.NoteAccountLevel(LevelsCalc.GetRank(data.xp.Value));
            state.QuietAccountLevelUntil(utcNow().AddSeconds(Math.Max(0f, JoinQuietSeconds)));
            string playerId = data.player_id.Value;
            if (!ProgressLimits.IsValidPlayerId(playerId)) {
                // PlayerProgress keeps nothing for such players; keep a session-only document here.
                state.Detached = new TSave();
                Debug.LogWarning($"Retention: player '{data.GetPlayerName()}' has no valid player id ('{playerId}'); their {Rules.UniverseId} levels and challenges are not saved.");
                return state;
            }
            if (PlayerProgress.GetState<TSave>(data) == null) {
                try {
                    PlayerProgress.Load<TSave>(data, Rules.UniverseId).Forget();
                } catch (Exception e) {
                    // Loaded under another universe id (a wiring mistake): keep the player playing.
                    Debug.LogException(e);
                    state.Detached = new TSave();
                }
            }
            return state;
        }

        void Change(NetworkConnection connection, Action<Player, RetentionSave> change) {
            if (connection == null || disposed) return;
            if (!players.TryGetValue(connection, out Player state)) {
                if (!PlayerData.TryGetPlayerData(connection, out PlayerData data)) return;
                state = Track(data);
            }
            Change(state, change);
        }

        void Change(Player state, Action<Player, RetentionSave> change) {
            if (state.Detached != null) Apply(state, state.Detached, change);
            // Runs now, or right after the document loads; PlayerProgress logs exceptions thrown inside.
            else PlayerProgress.Modify<TSave>(state.Data, save => Apply(state, save, change));
            // Saved here, after Modify has marked the document changed (a save from inside the change would miss it).
            if (state.SaveDue && TryBlock(state, out _)) SaveNow(state);
        }

        void Apply(Player state, TSave save, Action<Player, RetentionSave> change) {
            if (state.Left || disposed) return;
            RetentionSave block = blockOf(save) ?? throw new InvalidOperationException(
                $"{typeof(TSave).Name} has no retention block; repair it in Validate: retention = RetentionSave.Repair(retention).");
            state.Saved = IsSaved(state);
            change(state, block);
            if (state.IsReady) return;
            state.IsReady = true;
            int level = Rules.Track.LevelForXp(block.xp);
            try {
                Ready?.Invoke(state.Connection, level);
            } catch (Exception e) {
                Debug.LogException(e);
            }
        }

        /// <summary>The player's block when it can be read now (loaded, or session-only).</summary>
        bool TryBlock(Player state, out RetentionSave block) {
            block = null;
            if (state.Detached != null) block = blockOf(state.Detached);
            else if (PlayerProgress.TryGet(state.Data, out TSave save)) block = blockOf(save);
            return block != null;
        }

        bool TryRead(NetworkConnection connection, out RetentionSave block) {
            block = null;
            return connection != null && players.TryGetValue(connection, out Player state) && TryBlock(state, out block);
        }

        /// <summary>Applies queued stats and due saves of every queued player.</summary>
        void FlushQueued() {
            if (queued.Count == 0) return;
            // Swap first: flushing can queue the same players again (a completion asks for a save).
            List<Player> batch = queued;
            queued = draining;
            draining = batch;
            foreach (Player state in batch) {
                state.Queued = false;
                if (state.Left) continue;
                if (state.HasPendingStats) Change(state, flushChange);
                if (state.SaveDue && TryBlock(state, out _)) SaveNow(state);
                else if (state.SaveDue) Enqueue(state); // still loading: try again next tick
            }
            batch.Clear();
        }

        void OnSnapshotRequest(NetworkConnection connection, RetentionSnapshotRequest request, Channel channel) {
            if (!players.TryGetValue(connection, out Player state) || Time.unscaledTime < state.NextRequestTime) return;
            state.NextRequestTime = Time.unscaledTime + Mathf.Max(0.1f, SnapshotRequestInterval);
            Change(state, snapshotChange);
        }

        /// <summary>
        /// A disconnect is reported here before FishNet despawns the player's objects, so before
        /// <see cref="PlayerData.OnPlayerRemoved"/> and so before PlayerProgress releases (and saves)
        /// the document: the last safe moment for queued progress, whatever order handlers run in.
        /// </summary>
        void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args) {
            if (args.ConnectionState != RemoteConnectionState.Stopped || conn == null || !players.TryGetValue(conn, out Player state)) return;
            FlushOnLeave(state);
        }

        void OnPlayerRemoved(PlayerData data) {
            if (data == null || data.Owner == null || !players.TryGetValue(data.Owner, out Player state) || state.Data != data) return;
            // Normally already flushed at the disconnect. A player removed some other way is flushed here,
            // but only if PlayerProgress's own OnPlayerRemoved has not run first and released the document
            // (handler order is not guaranteed); then at most ProgressFlushInterval of progress that
            // completed nothing is lost.
            FlushOnLeave(state);
            state.Left = true;
            players.Remove(data.Owner);
        }

        void FlushOnLeave(Player state) {
            if (state.HasPendingStats && (state.Detached != null || PlayerProgress.GetState<TSave>(state.Data) != null)) Change(state, flushChange);
        }

        /// <summary>Applies queued progress every <see cref="ProgressFlushInterval"/> seconds.</summary>
        async UniTaskVoid TickLoop(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                try {
                    await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Clamp(ProgressFlushInterval, 0.05f, 5f)), ignoreTimeScale: true, cancellationToken: token);
                    FlushQueued();
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception e) {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>At every UTC midnight, gives everyone on the server the new day's challenges and countdown.</summary>
        async UniTaskVoid RolloverLoop(CancellationToken token) {
            var online = new List<Player>();
            while (!token.IsCancellationRequested) {
                try {
                    double wait = RetentionDay.SecondsUntilReset(utcNow()) + RolloverMarginSeconds;
                    await UniTask.Delay(TimeSpan.FromSeconds(wait), ignoreTimeScale: true, cancellationToken: token);
                    online.Clear();
                    online.AddRange(players.Values);
                    foreach (Player state in online) Change(state, snapshotChange);
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception e) {
                    Debug.LogException(e);
                    // Never spin: a failure before the first await would otherwise retry at once, forever.
                    try {
                        await UniTask.Delay(TimeSpan.FromSeconds(RolloverRetrySeconds), ignoreTimeScale: true, cancellationToken: token);
                    } catch (OperationCanceledException) {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Applies queued progress, stops the timers and unhooks from the network and player events. Call
        /// from the runner's OnDestroy: a service that is never disposed keeps its snapshot-request handler
        /// and its <see cref="PlayerData.OnPlayerRemoved"/> handler alive after the game is gone.
        /// </summary>
        public void Dispose() {
            if (disposed) return;
            FlushQueued();
            disposed = true;
            PlayerData.OnPlayerRemoved -= OnPlayerRemoved;
            if (InstanceFinder.ServerManager != null) {
                InstanceFinder.ServerManager.UnregisterBroadcast<RetentionSnapshotRequest>(OnSnapshotRequest);
                InstanceFinder.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }
            lifetime.Cancel();
            lifetime.Dispose();
            players.Clear();
            queued.Clear();
        }
    }
}
