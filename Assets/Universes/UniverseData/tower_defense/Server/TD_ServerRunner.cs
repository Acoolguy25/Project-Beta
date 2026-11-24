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

namespace Universes.UniverseData.tower_defense.Server {
    /// <summary>
    /// Co-op tower defense: players vote for a mode (Normal, Hard once they won Normal, Endless from its
    /// unlock level), then enemies roll down the lane wave after wave while players spend a shared purse
    /// on towers and upgrades, call in their abilities, and the base falls when it runs out of health.
    /// Everyone ready skips the build countdown for a cash bonus, and a majority vote runs waves at
    /// double speed. The game itself is a <see cref="TD_Simulation"/>; this runner feeds it requests,
    /// pays players through the retention kit (levels, unlocks, daily challenges, streak, summary
    /// card) and broadcasts what happens. Clients only draw.
    /// </summary>
    public sealed class TD_ServerRunner : ServerRunner {
        [Header("Pacing")]
        [SerializeField, Min(3)] int firstBuildSeconds = 30;
        [SerializeField, Min(3)] int buildSeconds = 12;
        [SerializeField, Min(3)] int resultsSeconds = 14;
        [Tooltip("Longest the mode vote runs before wave 1.")]
        [SerializeField, Min(3)] int voteSeconds = 15;
        [Tooltip("The vote cards stay up at least this long, even when everyone has voted (or only Normal is open).")]
        [SerializeField, Min(0)] int voteMinSeconds = 4;
        [Tooltip("Caps Normal and Hard games (shorter test games); Endless ignores it.")]
        [SerializeField, Range(1, TD_Rules.WaveCount)] int waveCount = TD_Rules.WaveCount;
        [SerializeField, Min(0.05f)] float snapshotInterval = 0.1f;
        [Tooltip("Players may vote to run waves at TD_Rules.FastSpeed.")]
        [SerializeField] bool allowFastForward = true;
        [Tooltip("Shortest time between two votes or ability calls from one player (seconds).")]
        [SerializeField, Min(0f)] float voteInterval = 0.25f;
        [Tooltip("Simulation seconds from an Airstrike call until its bombs land (the jet's flight; the target is led to match).")]
        [SerializeField, Range(0f, 3f)] float airstrikeDelay = 1.1f;
        [Tooltip("Every game of the day plays the day's modifier (TD_Daily); off = the plain game.")]
        [SerializeField] bool dailyModifier = true;

        [Header("Retention (levels, unlocks, daily challenges, streak)")]
        [SerializeField] RetentionTuning retentionTuning = new();
        [Header("Rewards (game XP, times the mode's XP scale; the kit also pays the same account XP)")]
        [SerializeField, Min(0)] int waveXp = 8;
        [SerializeField, Min(0)] int victoryXp = 150;
        [Tooltip("Account gold for a win (Hard pays the mode's XP scale more).")]
        [SerializeField, Min(0)] int victoryGold = 60;
        [Tooltip("Enemies destroyed per point of XP.")]
        [SerializeField, Min(1)] int killsPerXp = 5;
        [SerializeField, Min(0)] int bossXp = 25;
        [Tooltip("XP per star of a win.")]
        [SerializeField, Min(0)] int starXp = 20;
        [Tooltip("XP for beating your best wave (or best Endless wave).")]
        [SerializeField, Min(0)] int recordXp = 30;

        const string KillsColumn = "Kills";
        const string BuiltColumn = "Built";

        /// <summary>One player's part in the current game.</summary>
        sealed class Run {
            public int kills, bosses;
            /// <summary>Wave each ability was last used in (index = <see cref="TD_Ability"/>; 0 = not this game).</summary>
            public readonly int[] lastUsed = new int[TD_Abilities.Count];
            /// <summary>Mode voted for (-1 = none yet).</summary>
            public int vote = -1;
            /// <summary>Only Normal is open to this player: they sit the vote out.</summary>
            public bool abstains;
            /// <summary>In this game's match summary (BeginMatch was called).</summary>
            public bool playing;
            /// <summary>Told about a new personal best this game (the toast shows once; the summary repeats it).</summary>
            public bool newRecord, newEndlessRecord;
            public float lastVote = float.NegativeInfinity;
            /// <summary>The wave this player's game began on (1 from the vote; later for a late joiner), for <see cref="TD_Rules.WinCounts"/>.</summary>
            public int startWave = 1;
        }

        readonly TD_Simulation sim = new();
        readonly Dictionary<NetworkConnection, Run> runs = new();
        readonly string[] ownerNames = new string[TD_Rules.Slots.Length];
        readonly HashSet<NetworkConnection> ready = new();
        readonly HashSet<NetworkConnection> fastVotes = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly List<RewardLine> lines = new();
        RetentionService<TD_SaveData> retention;
        TD_Map map;
        TD_Phase phase;
        int wave, lastWave, secondsLeft, earlyBonus;
        /// <summary>The modifier the next game plays (today's), shown on the vote.</summary>
        TD_Modifier modifier;
        float snapshotTimer, speed = 1f;
        string headline = "";
        bool stateDirty, waveLeaked;

        public TD_Phase Phase => phase;
        public int Cash => sim.Cash;
        /// <summary>Probe hook: the running game (cash, base, enemies, towers).</summary>
        public TD_Simulation Simulation => sim;
        /// <summary>Probe hook: the retention service (e.g. AddXp to reach an unlock level in a test).</summary>
        public RetentionService<TD_SaveData> Retention => retention;

        protected override void Awake() {
            base.Awake();
            // Ground sits at y = 0; keep the kill plane well below it.
            GameCharacter.FallHeightOverride = -20f;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnPosition;
            ServerPlayerCharacter.RespawnTime = 2f;
            PlayerData.OnPlayerRemoved += OnPlayerLeft;
            retention = new RetentionService<TD_SaveData>(TD_Retention.Rules(retentionTuning), save => save.retention);
            retention.Unlocked += OnUnlocked;
            retention.Ready += OnRetentionReady;
            var server = InstanceFinder.ServerManager;
            server.RegisterBroadcast<TD_BuildRequest>(OnBuildRequest);
            server.RegisterBroadcast<TD_StateRequest>(OnStateRequest);
            server.RegisterBroadcast<TD_ReadyRequest>(OnReadyRequest);
            server.RegisterBroadcast<TD_SellRequest>(OnSellRequest);
            server.RegisterBroadcast<TD_SpeedRequest>(OnSpeedRequest);
            server.RegisterBroadcast<TD_ModeVoteRequest>(OnModeVoteRequest);
            server.RegisterBroadcast<TD_AbilityRequest>(OnAbilityRequest);
        }

        Vector3 SpawnPosition(NetworkConnection connection) => (map != null ? map.SpawnPoint() : Vector3.zero) + Vector3.up * 1.2f;

        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            player.cameraTypes.Clear();
            player.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
            player.walkSpeed.Value = 14f;
            player.sprintSpeed.Value = 24f;
            player.staminaMax.Value = 400f;
            player.staminaRegen.Value = 80f;
            var run = runs[player.Owner] = new Run();
            SendState(player.Owner);
            retention.PlayerJoined(player);
            // Joining a game already under way: the summary card covers the rest of it.
            if (phase is TD_Phase.Build or TD_Phase.Wave) BeginRun(player.Owner, run);
            LoadProfile(player).Forget(e => Debug.LogException(e, this));
        }

        async UniTask LoadProfile(PlayerData player) {
            var save = await PlayerProgress.Load<TD_SaveData>(player, TD_SaveData.Universe);
            if (player == null || player.Owner == null || !player.Owner.IsActive) return;
            SendProfile(player.Owner, save);
            SendAbilities(player.Owner);
        }

        void OnPlayerLeft(PlayerData player) {
            var owner = player.Owner;
            ready.Remove(owner);
            requestTimes.Remove(owner);
            runs.Remove(owner);
            fastVotes.Remove(owner);
            // Their towers stay and keep fighting; anyone may sell them now.
            foreach (var tower in sim.Towers)
                if (tower != null && owner != null && tower.owner == owner.ClientId) tower.owner = -1;
            UpdateSpeed();
        }

        /// <summary>The player's level is known: their shop locks, abilities and modes can be sent now.</summary>
        void OnRetentionReady(NetworkConnection connection, int level) {
            SendAbilities(connection);
            if (PlayerProgress.TryGet<TD_SaveData>(connection, out var save)) SendProfile(connection, save);
            if (phase == TD_Phase.Vote && runs.TryGetValue(connection, out var run) && run.vote < 0)
                run.abstains = !ModeUnlocked(connection, TD_Difficulty.Hard) && !ModeUnlocked(connection, TD_Difficulty.Endless);
        }

        /// <summary>A level-up unlocked something: abilities and modes may have opened (the kit shows the toast).</summary>
        void OnUnlocked(NetworkConnection connection, UnlockDefinition unlock) {
            SendAbilities(connection);
            if (PlayerProgress.TryGet<TD_SaveData>(connection, out var save)) SendProfile(connection, save);
        }

        // ---------------------------------------------------------------- game
        protected override async UniTask StartAsync(CancellationToken token) {
            phase = TD_Phase.Waiting;
            modifier = TodaysModifier();
            await WaitForSceneAsync("tower_defense_start", token);
            map = FindAnyObjectByType<TD_Map>();
            if (map == null || !map.IsConfigured) throw new InvalidOperationException("Tower defense requires a configured TD_Map in tower_defense_start.");
            SetGlobalInvul(true);
            SetTeamKillEnabled(false);
            SetLeaderboardEnabled(KillsColumn, true);
            SetLeaderboardEnabled(BuiltColumn, true);
            SetMusic(MusicSelection.GameMusic);
            sim.Reset(TD_Difficulty.Normal);
            System.Array.Clear(ownerNames, 0, ownerNames.Length);
            fastVotes.Clear();
            speed = 1f;
            wave = 0;
            earlyBonus = 0;
            headline = "";
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            MarkDirty();
            await WaitForPlayersAsync(1, token);

            var mode = await VoteAsync(token);
            StartGame(mode);
            lastWave = sim.Endless ? int.MaxValue : Mathf.Min(sim.WaveCount, waveCount);

            for (wave = 1; wave <= lastWave; wave++) {
                await BuildPhase(token);
                await FightWave(token);
                if (sim.Defeated) break;
                WaveCleared();
            }
            if (!sim.Defeated) wave = lastWave; // the loop leaves it one past the last wave (the HUD read "8 / 7")
            FinishGame(!sim.Defeated && !sim.Endless);
            for (int i = resultsSeconds; i > 0; i--) {
                secondsLeft = i;
                SetTopMessage($"{headline}  New game in {i}");
                MarkDirty();
                await AwaitTime(1000, token);
            }
        }

        /// <summary>
        /// Players pick the mode with 1-3. Players who only have Normal sit it out; the vote ends early
        /// once everyone else has voted (but stays up a few seconds so everyone sees what is coming).
        /// </summary>
        async UniTask<TD_Difficulty> VoteAsync(CancellationToken token) {
            phase = TD_Phase.Vote;
            modifier = TodaysModifier(); // a vote after midnight UTC shows the new day's
            foreach (var pair in runs) {
                pair.Value.vote = -1;
                pair.Value.abstains = !ModeUnlocked(pair.Key, TD_Difficulty.Hard) && !ModeUnlocked(pair.Key, TD_Difficulty.Endless);
            }
            headline = "Vote for a mode!";
            MarkDirty();
            for (float left = voteSeconds, shown = 0f; left > 0f; left -= 0.25f, shown += 0.25f) {
                token.ThrowIfCancellationRequested();
                secondsLeft = Mathf.CeilToInt(left);
                SetTopMessage($"Vote for a mode: {secondsLeft}s");
                if (shown >= voteMinSeconds && AllVoted()) break;
                BroadcastStateThrottled();
                await AwaitTime(250, token);
            }
            return TD_Modes.Winner(ModeVotes());
        }

        bool AllVoted() {
            var active = GetActivePlayers();
            return active.Length > 0 && active.All(p => !runs.TryGetValue(p.Owner, out var run) || run.abstains || run.vote >= 0);
        }

        int[] ModeVotes() {
            var votes = new int[TD_Modes.Count];
            foreach (var player in GetActivePlayers())
                if (runs.TryGetValue(player.Owner, out var run) && !run.abstains && run.vote >= 0) votes[run.vote]++;
            return votes;
        }

        TD_Modifier TodaysModifier() => dailyModifier ? TD_Daily.Today(DateTime.UtcNow) : TD_Modifier.None;

        /// <summary>A fresh game of <paramref name="mode"/> under today's modifier: War Chest and Supply Drop cash, every player's match opened.</summary>
        void StartGame(TD_Difficulty mode) {
            sim.Reset(mode, modifier);
            System.Array.Clear(ownerNames, 0, ownerNames.Length);
            int bonus = 0;
            foreach (var player in GetActivePlayers()) {
                var owner = player.Owner;
                bonus += TD_Retention.StartingCash(id => retention.IsUnlocked(owner, id));
                if (runs.TryGetValue(owner, out var run)) {
                    BeginRun(owner, run);
                    SendAbilities(owner);
                }
            }
            sim.Cash += bonus;
            string chest = bonus > 0 ? $"  Unlocks +${bonus}" : "";
            var day = sim.ModifierStats;
            string twist = day.name.Length > 0 ? $"  Today: {day.name}." : "";
            headline = $"{sim.ModeStats.name}: build towers on the pads beside the road!{chest}";
            ServerChat.SendSystemMessage(new($"Tower Defense: {sim.ModeStats.name} mode.{twist}{chest}", SystemMessageSource.CustomMessage));
            MarkDirty();
        }

        void BeginRun(NetworkConnection connection, Run run) {
            run.kills = run.bosses = 0;
            run.startWave = Mathf.Max(1, wave); // 0 while the game is being set up: it began on wave 1
            run.newRecord = run.newEndlessRecord = false;
            System.Array.Clear(run.lastUsed, 0, run.lastUsed.Length);
            run.playing = true;
            retention.BeginMatch(connection);
        }

        async UniTask BuildPhase(CancellationToken token) {
            phase = TD_Phase.Build;
            ready.Clear();
            earlyBonus = 0;
            if (wave > 1) headline = $"Wave {wave - 1} cleared! +${TD_Daily.WaveBonus(wave - 1, sim.Mode, sim.Modifier)}";
            foreach (var player in GetActivePlayers()) SendAbilities(player.Owner);
            int seconds = wave == 1 ? firstBuildSeconds : buildSeconds;
            for (float left = seconds; left > 0f; left -= 0.25f) {
                secondsLeft = Mathf.CeilToInt(left);
                SetTopMessage($"Build phase: wave {wave} in {secondsLeft}s");
                if (AllReady()) {
                    // Calling the wave early pays for the build time the team gave up.
                    earlyBonus = TD_Rules.EarlyStartBonus(wave, secondsLeft); // the same whole seconds the HUD promised
                    sim.Cash += earlyBonus;
                    if (earlyBonus > 0) foreach (var connection in ready) retention.Count(connection, TD_Retention.EarlyStat);
                    break;
                }
                BroadcastStateThrottled();
                await AwaitTime(250, token);
            }
        }

        async UniTask FightWave(CancellationToken token) {
            phase = TD_Phase.Wave;
            secondsLeft = 0;
            waveLeaked = false;
            sim.StartWave(wave);
            headline = earlyBonus > 0 ? $"Wave {wave} called early! +${earlyBonus}" : $"Wave {wave}: {DescribeWave(wave)}";
            RecordWave(wave);
            foreach (var player in GetActivePlayers()) SendAbilities(player.Owner);
            MarkDirty();
            while (!sim.WaveCleared && !sim.Defeated) {
                token.ThrowIfCancellationRequested();
                SetTopMessage(sim.Endless ? $"Endless wave {wave} in progress" : $"Wave {wave}/{lastWave} in progress");
                BroadcastStateThrottled();
                await AwaitTime(250, token);
            }
        }

        void WaveCleared() {
            sim.PayWaveBonus();
            int xp = Xp(waveXp);
            foreach (var player in GetActivePlayers()) {
                if (!runs.TryGetValue(player.Owner, out var run) || !run.playing) continue;
                retention.AddXp(player.Owner, xp, "Waves held");
                if (!waveLeaked) retention.Count(player.Owner, TD_Retention.CleanWaveStat);
            }
            MarkDirty();
        }

        /// <summary>Game XP scaled by the mode (Hard pays more).</summary>
        int Xp(int amount) => Mathf.RoundToInt(amount * sim.ModeStats.xpScale);

        void FinishGame(bool won) {
            ProcessTallies(); // the last kills of the game count too
            int reached = sim.Endless ? wave : Mathf.Min(wave, lastWave);
            int stars = won ? TD_Rules.Stars(sim.BaseHealth) : 0;
            int waveHeld = won ? lastWave : Mathf.Max(0, reached - 1);
            var mode = sim.Mode;
            phase = won ? TD_Phase.Victory : TD_Phase.Defeat;
            headline = won ? $"VICTORY! The base held all {lastWave} waves on {sim.ModeStats.name}."
                : sim.Endless ? $"Endless: the base fell on wave {reached}." : $"The base fell on wave {reached}.";
            // The title names the mode, so the card needs no "Mode" line.
            string title = won ? $"Victory on {sim.ModeStats.name}!" : sim.Endless ? $"Endless: wave {reached}" : $"{sim.ModeStats.name}: the base fell on wave {reached}";
            SetMusic(won ? MusicSelection.VictoryMusic : MusicSelection.GameMusic);
            string today = sim.ModifierStats.name.Length > 0 ? $"  ·  {sim.ModifierStats.name}" : "";
            foreach (var player in GetActivePlayers()) {
                var owner = player.Owner;
                if (!runs.TryGetValue(owner, out var run)) continue;
                bool record = run.newRecord || run.newEndlessRecord;
                // A win counts (Hard unlock, wins, stars, win challenges) only for a player who held enough of it.
                bool credited = won && TD_Rules.WinCounts(run.startWave, lastWave);
                PlayerProgress.Modify<TD_SaveData>(player, save => {
                    save.gamesPlayed++;
                    if (credited) save.RecordWin(mode, stars);
                    SendProfile(owner, save, run.newRecord, run.newEndlessRecord);
                });
                if (credited) {
                    retention.Count(owner, TD_Retention.WinStat);
                    retention.Reach(owner, TD_Retention.StarsStat, stars);
                    if (mode == TD_Difficulty.Hard) retention.Count(owner, TD_Retention.HardWinStat);
                }
                lines.Clear();
                // Detail lines first (the card keeps 8 lines and folds extra rewards, not details, into the last):
                // what this game was, so the card reads as a result as well as a payout. "Waves" is its own
                // line: merged into the per-wave XP line ("Waves held x7  7 / 7") the label ran out of room.
                // Endless counts the wave reached, as its record does ("2 (best 3)" read as a contradiction).
                lines.Add(sim.Endless ? RewardLine.Info("Reached", $"wave {reached} (best {BestEndless(player)}){today}") : RewardLine.Info("Waves", $"{waveHeld} / {lastWave}{today}"));
                if (won && !credited) lines.Add(RewardLine.Info("Joined", $"wave {run.startWave}: a win needs waves 1-{TD_Rules.LastWinJoinWave(lastWave)}"));
                if (credited) lines.Add(RewardLine.Info("Rating", $"{stars} / {TD_Rules.MaxStars} stars (base {sim.BaseHealth}%)"));
                if (credited) lines.Add(new RewardLine($"Victory ({sim.ModeStats.name})", Xp(victoryXp), Mathf.RoundToInt(victoryGold * sim.ModeStats.xpScale)));
                if (credited && stars > 0) lines.Add(new RewardLine($"{stars}-star defence", Xp(starXp * stars)));
                if (record) lines.Add(new RewardLine(run.newEndlessRecord ? "New best Endless wave" : "New best wave", Xp(recordXp)));
                if (run.bosses > 0) lines.Add(new RewardLine("Heavy Tanks destroyed", Xp(bossXp * run.bosses), 0, run.bosses));
                if (run.kills > 0) lines.Add(new RewardLine("Enemies destroyed", Xp(run.kills / killsPerXp), 0, run.kills));
                retention.FinishMatch(owner, title, lines);
                run.playing = false;
            }
            fastVotes.Clear();
            speed = 1f;
            ServerChat.SendSystemMessage(new(headline, SystemMessageSource.CustomMessage));
            MarkDirty();
        }

        static int BestEndless(PlayerData player) =>
            PlayerProgress.TryGet<TD_SaveData>(player.Owner, out var save) ? save.bestEndlessWave : 0;

        static string DescribeWave(int number) {
            var counts = TD_Rules.Wave(number).GroupBy(g => g.kind).Select(g => $"{g.Sum(x => x.count)} {TD_Rules.Enemy(g.Key).name}");
            return string.Join(", ", counts);
        }

        bool AllReady() {
            var active = GetActivePlayers();
            return active.Length > 0 && active.All(p => ready.Contains(p.Owner));
        }

        int ReadyCount() => GetActivePlayers().Count(p => ready.Contains(p.Owner));

        int FastCount() => GetActivePlayers().Count(p => fastVotes.Contains(p.Owner));

        /// <summary>Saves the wave as fought in for every player in the game and tells those who just beat their record.</summary>
        void RecordWave(int number) {
            bool endless = sim.Endless;
            foreach (var player in PlayerData.Players.Values) {
                var owner = player.Owner;
                if (!runs.TryGetValue(owner, out var run)) continue;
                retention.Reach(owner, TD_Retention.WaveStat, number);
                PlayerProgress.Modify<TD_SaveData>(player, save => {
                    // A first game sets the record quietly; beating an earlier best is celebrated once per
                    // game (the toast), and the summary card says it again at the end.
                    bool hadRecord = save.bestWave > 0, hadEndless = save.bestEndlessWave > 0;
                    bool best = save.RecordWave(number), endlessBest = endless && save.RecordEndlessWave(number);
                    if (!best && !endlessBest) return;
                    bool announce = best && hadRecord && !run.newRecord, announceEndless = endlessBest && hadEndless && !run.newEndlessRecord;
                    if (announce) run.newRecord = true;
                    if (announceEndless) run.newEndlessRecord = true;
                    SendProfile(owner, save, announce, announceEndless);
                });
            }
        }

        // ---------------------------------------------------------------- simulation
        void Update() {
            if (map == null) return;
            if (phase == TD_Phase.Wave) {
                sim.Tick(Time.deltaTime * speed);
                if (sim.Events.baseDamage > 0) {
                    waveLeaked = true;
                    MarkDirty();
                }
                if (sim.Events.cashEarned > 0) MarkDirty();
            }
            snapshotTimer += Time.deltaTime;
            if (snapshotTimer >= snapshotInterval) {
                snapshotTimer = 0f;
                BroadcastSnapshot();
                ProcessTallies();
                sim.Events.Clear();
            }
            if (stateDirty) BroadcastState();
        }

        /// <summary>Kills and shield breaks since the last snapshot: leaderboard, saved totals and daily challenges for whoever did them.</summary>
        void ProcessTallies() {
            foreach (var kill in sim.Events.kills) {
                if (!TryOwner(kill.owner, out var connection, out var player)) continue;
                RoundLeaderboard.Add(player, KillsColumn, 1);
                bool boss = TD_Rules.IsBoss(kill.kind);
                if (runs.TryGetValue(connection, out var run)) {
                    run.kills++;
                    if (boss) run.bosses++;
                }
                PlayerProgress.Modify<TD_SaveData>(player, save => {
                    save.totalKills++;
                    if (boss) save.bossesDestroyed++;
                });
                retention.Count(connection, TD_Retention.KillsStat);
                if (boss) retention.Count(connection, TD_Retention.BossStat);
                if (kill.kind == TD_EnemyKind.Drone) retention.Count(connection, TD_Retention.DronesStat);
                if (kill.kind == TD_EnemyKind.Medic) retention.Count(connection, TD_Retention.MedicsStat);
            }
            foreach (var shield in sim.Events.shieldBreaks) {
                if (!TryOwner(shield.owner, out var connection, out var player)) continue;
                PlayerProgress.Modify<TD_SaveData>(player, save => save.shieldsBroken++);
                retention.Count(connection, TD_Retention.ShieldsStat);
            }
            sim.Events.kills.Clear();
            sim.Events.shieldBreaks.Clear();
        }

        /// <summary>The player behind a tower owner or ability caster id (a client id), while still connected.</summary>
        static bool TryOwner(int clientId, out NetworkConnection connection, out PlayerData player) {
            player = null;
            connection = null;
            return clientId >= 0 && InstanceFinder.ServerManager.Clients.TryGetValue(clientId, out connection)
                && connection.IsActive && PlayerData.TryGetPlayerData(connection, out player);
        }

        // ---------------------------------------------------------------- requests
        void OnBuildRequest(NetworkConnection connection, TD_BuildRequest request, Channel channel) {
            if (!connection.IsAuthenticated || map == null) return;
            string message = TryBuild(connection, request.slot, request.kind, out bool accepted);
            Reply(connection, accepted, message);
        }

        string TryBuild(NetworkConnection connection, int slot, TD_TowerKind kind, out bool accepted) {
            accepted = false;
            if (phase is TD_Phase.Victory or TD_Phase.Defeat) return "The game is over. New game soon!";
            if (phase is TD_Phase.Vote or TD_Phase.Waiting) return "Vote for a mode first: the game starts in a moment.";
            if (!TD_Simulation.IsSlot(slot) || (int)kind >= TD_Rules.Towers.Length) return "That is not a tower pad.";
            if (!LocalCharacter.Characters.TryGetValue(connection, out var character) || character == null || character.IsDead) return "Respawn first.";
            if (!FocusPicker.InReach(character.transform.position, TD_Rules.Slots[slot], TD_Rules.SlotReach, 2.5f)) return "Walk closer to the pad.";
            var existing = sim.Towers[slot];
            var stats = TD_Rules.Tower(existing != null ? existing.kind : kind);
            if (existing == null && stats.unlockId.Length > 0 && !retention.IsUnlocked(connection, stats.unlockId))
                return $"Reach level {TD_Retention.LevelOf(stats.unlockId)} to unlock the {stats.name}.";
            var perks = TD_Retention.PerksFor(id => retention.IsUnlocked(connection, id));
            switch (sim.Build(slot, kind, connection.ClientId, perks, out int cost)) {
                case TD_BuildResult.MaxLevel:
                    return $"{stats.name} is fully upgraded.";
                case TD_BuildResult.NoCash:
                    return existing != null ? $"Upgrade costs ${cost}. Team has ${sim.Cash}." : $"{stats.name} costs ${cost}. Team has ${sim.Cash}.";
                case TD_BuildResult.Upgraded: {
                    accepted = true;
                    var tower = sim.Towers[slot];
                    CountBuild(connection);
                    PlayerProgress.Modify<TD_SaveData>(connection, save => save.towersUpgraded++);
                    if (tower.level >= TD_Rules.MaxLevel) retention.Count(connection, TD_Retention.MaxedStat);
                    BroadcastTowerEvent(TD_TowerEvent.Upgraded, tower, cost);
                    MarkDirty();
                    return $"{stats.name} upgraded to level {tower.level}!";
                }
                case TD_BuildResult.Built: {
                    accepted = true;
                    ownerNames[slot] = PlayerData.GetPlayerName(connection);
                    CountBuild(connection);
                    PlayerProgress.Modify<TD_SaveData>(connection, save => save.towersBuilt++);
                    retention.Count(connection, TD_Retention.BuildsStat);
                    BroadcastTowerEvent(TD_TowerEvent.Built, sim.Towers[slot], cost);
                    MarkDirty();
                    return $"Built a {stats.name}!";
                }
                default:
                    return "That is not a tower pad.";
            }
        }

        void OnSellRequest(NetworkConnection connection, TD_SellRequest request, Channel channel) {
            if (!connection.IsAuthenticated || map == null) return;
            string message = TrySell(connection, request.slot, out bool accepted);
            Reply(connection, accepted, message);
        }

        string TrySell(NetworkConnection connection, int slot, out bool accepted) {
            accepted = false;
            if (phase is TD_Phase.Victory or TD_Phase.Defeat) return "The game is over. New game soon!";
            var tower = TD_Simulation.IsSlot(slot) ? sim.Towers[slot] : null;
            if (tower == null) return "There is no tower to sell there.";
            if (!LocalCharacter.Characters.TryGetValue(connection, out var character) || character == null || character.IsDead) return "Respawn first.";
            if (!FocusPicker.InReach(character.transform.position, TD_Rules.Slots[slot], TD_Rules.SlotReach, 2.5f)) return "Walk closer to the tower.";
            if (tower.owner >= 0 && tower.owner != connection.ClientId && TryOwner(tower.owner, out _, out _))
                return $"Only {ownerNames[slot]} can sell this {TD_Rules.Tower(tower.kind).name}.";
            sim.Sell(slot, out var sold);
            accepted = true;
            BroadcastTowerEvent(TD_TowerEvent.Sold, sold, sold.Refund);
            ownerNames[slot] = null;
            MarkDirty();
            return $"Sold the {TD_Rules.Tower(sold.kind).name} for ${sold.Refund}.";
        }

        /// <summary>False when <paramref name="connection"/> voted or called an ability less than voteInterval ago (spam guard).</summary>
        bool VoteAllowed(NetworkConnection connection) {
            if (!runs.TryGetValue(connection, out var run)) return false;
            if (Time.unscaledTime - run.lastVote < voteInterval) return false;
            run.lastVote = Time.unscaledTime;
            return true;
        }

        void OnSpeedRequest(NetworkConnection connection, TD_SpeedRequest request, Channel channel) {
            if (!connection.IsAuthenticated || !allowFastForward || !VoteAllowed(connection)) return;
            if (request.fast) fastVotes.Add(connection); else fastVotes.Remove(connection);
            UpdateSpeed();
        }

        /// <summary>Fast forward runs while it has the vote of most players (server-authoritative).</summary>
        void UpdateSpeed() {
            speed = allowFastForward && TD_Rules.FastVotePasses(FastCount(), GetActivePlayersCount()) ? TD_Rules.FastSpeed : 1f;
            MarkDirty(); // vote counts changed even when the speed did not
        }

        void OnModeVoteRequest(NetworkConnection connection, TD_ModeVoteRequest request, Channel channel) {
            if (!connection.IsAuthenticated || phase != TD_Phase.Vote || !TD_Modes.IsValid(request.mode) || !VoteAllowed(connection)) return;
            if (!runs.TryGetValue(connection, out var run)) return;
            var mode = TD_Modes.Get(request.mode);
            if (!ModeUnlocked(connection, request.mode)) {
                Reply(connection, false, $"{mode.name} is locked: {mode.lockedHint}.");
                return;
            }
            run.vote = (int)request.mode;
            run.abstains = false;
            MarkDirty();
        }

        /// <summary>Normal is always open; Hard after a Normal win; Endless from its unlock level.</summary>
        bool ModeUnlocked(NetworkConnection connection, TD_Difficulty mode) => mode switch {
            TD_Difficulty.Normal => true,
            TD_Difficulty.Hard => PlayerProgress.TryGet<TD_SaveData>(connection, out var save) && save.HardUnlocked,
            TD_Difficulty.Endless => retention.IsUnlocked(connection, TD_Retention.EndlessUnlock),
            _ => false,
        };

        void OnAbilityRequest(NetworkConnection connection, TD_AbilityRequest request, Channel channel) {
            if (!connection.IsAuthenticated || !TD_Abilities.IsValid(request.ability) || !VoteAllowed(connection)) return;
            if (!runs.TryGetValue(connection, out var run)) return;
            var stats = TD_Abilities.Get(request.ability);
            if (!retention.IsUnlocked(connection, stats.unlockId)) {
                Reply(connection, false, $"Reach level {TD_Retention.LevelOf(stats.unlockId)} to unlock {stats.name}.");
                return;
            }
            if (phase != TD_Phase.Wave) {
                Reply(connection, false, $"{stats.name} works during a wave.");
                return;
            }
            int index = (int)request.ability;
            int left = TD_Abilities.WavesLeft(request.ability, run.lastUsed[index], wave);
            if (left > 0) {
                Reply(connection, false, left == 1 ? $"{stats.name} is ready again next wave." : $"{stats.name} is ready again in {left} waves.");
                return;
            }
            float distance = 0f, delay = 0f;
            int hits;
            // A second call in the same wave (by anyone) is weaker: abilities are per player, the army is not.
            float power = TD_Abilities.RepeatScale(request.ability == TD_Ability.Airstrike ? sim.StrikesThisWave : sim.EmpsThisWave);
            if (request.ability == TD_Ability.Airstrike) {
                if (!sim.CallAirstrike(connection.ClientId, airstrikeDelay, out distance, out hits)) {
                    Reply(connection, false, "No enemies on the road to strike.");
                    return;
                }
                delay = airstrikeDelay;
            } else {
                hits = sim.Emp(connection.ClientId);
                if (hits == 0) {
                    Reply(connection, false, "No enemies on the road.");
                    return;
                }
            }
            run.lastUsed[index] = wave;
            PlayerProgress.Modify<TD_SaveData>(connection, save => save.abilitiesUsed++);
            InstanceFinder.ServerManager.Broadcast(new TD_AbilityBroadcast {
                ability = request.ability, distance = distance, caster = PlayerData.GetPlayerName(connection), hits = hits, delay = delay, power = power,
            });
            SendAbilities(connection);
            MarkDirty();
        }

        static void CountBuild(NetworkConnection connection) {
            if (PlayerData.TryGetPlayerData(connection, out var player)) RoundLeaderboard.Add(player, BuiltColumn, 1);
        }

        void OnStateRequest(NetworkConnection connection, TD_StateRequest request, Channel channel) {
            if (!connection.IsAuthenticated) return;
            if (requestTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < 1f) return;
            requestTimes[connection] = Time.unscaledTime;
            SendState(connection);
            // The client asks once its handlers are registered: the profile and abilities sent at join may
            // have arrived before that (a rejoin then showed Hard and Endless locked on the vote cards).
            SendAbilities(connection);
            if (retention.IsReady(connection) && PlayerProgress.TryGet<TD_SaveData>(connection, out var save)) SendProfile(connection, save);
        }

        void OnReadyRequest(NetworkConnection connection, TD_ReadyRequest request, Channel channel) {
            if (!connection.IsAuthenticated || phase != TD_Phase.Build || !VoteAllowed(connection)) return;
            if (request.ready) ready.Add(connection); else ready.Remove(connection);
            MarkDirty();
        }

        // ---------------------------------------------------------------- broadcasts
        void MarkDirty() => stateDirty = true;

        float lastState;
        void BroadcastStateThrottled() {
            if (stateDirty || Time.time - lastState >= 1f) BroadcastState();
        }

        TD_StateBroadcast MakeState() {
            var towers = new List<TD_TowerSnap>();
            foreach (var t in sim.Towers) {
                if (t == null) continue;
                towers.Add(new TD_TowerSnap {
                    slot = t.slot, kind = t.kind, level = (byte)t.level, owner = ownerNames[t.slot] ?? "",
                    ownerId = TryOwner(t.owner, out _, out _) ? t.owner : -1,
                    refund = t.Refund, gold = t.perks.gold, veteran = t.perks.damageScale > 1f,
                });
            }
            var votes = ModeVotes();
            return new TD_StateBroadcast {
                phase = phase, wave = Mathf.Max(1, wave), secondsLeft = secondsLeft, cash = sim.Cash, baseHealth = sim.BaseHealth,
                enemiesLeft = sim.EnemiesLeft, headline = headline,
                readyPlayers = (byte)Mathf.Min(255, ReadyCount()), activePlayers = (byte)Mathf.Min(255, GetActivePlayers().Length),
                towers = towers.ToArray(),
                earlyBonus = earlyBonus, speed = speed, fastPlayers = (byte)Mathf.Min(255, FastCount()), fastForwardAllowed = allowFastForward,
                mode = phase == TD_Phase.Vote ? TD_Modes.Winner(votes) : sim.Mode,
                modeVotes = votes.Select(v => (byte)Mathf.Min(255, v)).ToArray(),
                waveCount = sim.Endless ? 0 : Mathf.Min(sim.WaveCount, waveCount),
                modifier = phase is TD_Phase.Waiting or TD_Phase.Vote ? modifier : sim.Modifier,
            };
        }

        void BroadcastState() {
            stateDirty = false;
            lastState = Time.time;
            if (map != null) InstanceFinder.ServerManager.Broadcast(MakeState());
        }

        void SendState(NetworkConnection connection) {
            if (map == null || connection == null || !connection.IsActive) return;
            InstanceFinder.ServerManager.Broadcast(connection, MakeState());
        }

        void BroadcastSnapshot() {
            var events = sim.Events;
            if (sim.Enemies.Count == 0 && !events.Any && events.shieldBreaks.Count == 0 && phase != TD_Phase.Wave) return;
            InstanceFinder.ServerManager.Broadcast(new TD_SnapshotBroadcast {
                time = sim.Time,
                enemies = sim.Enemies.Select(e => new TD_EnemySnap {
                    id = e.id, kind = e.kind, distance = e.distance,
                    health = (byte)Mathf.Clamp(Mathf.CeilToInt(e.health / e.maxHealth * 255f), 1, 255),
                    slowed = e.Slow(sim.Time) < 1f,
                    slow = (byte)Mathf.RoundToInt(e.Slow(sim.Time) * 255f),
                    shield = (byte)(e.shield > 0f ? Mathf.Clamp(Mathf.CeilToInt(e.shield / e.maxHealth * 255f), 1, 255) : 0),
                }).ToArray(),
                shots = events.shots.ToArray(),
                killed = events.killed.ToArray(),
                leaked = events.leaked.ToArray(),
                healed = events.healed.ToArray(),
                shieldsBroken = events.shieldBreaks.Select(s => s.enemy).ToArray(),
            });
        }

        void BroadcastTowerEvent(TD_TowerEvent kind, TD_SimTower tower, int amount) =>
            InstanceFinder.ServerManager.Broadcast(new TD_TowerEventBroadcast {
                kind = kind, slot = tower.slot, tower = tower.kind, level = (byte)tower.level, amount = amount
            });

        static void Reply(NetworkConnection connection, bool accepted, string message) =>
            InstanceFinder.ServerManager.Broadcast(connection, new TD_ResultBroadcast { accepted = accepted, message = message });

        void SendProfile(NetworkConnection connection, TD_SaveData save, bool newBestWave = false, bool newBestEndless = false) {
            if (connection == null || !connection.IsActive) return;
            var stars = new byte[TD_Modes.Count];
            var unlocked = new bool[TD_Modes.Count];
            for (int i = 0; i < TD_Modes.Count; i++) {
                stars[i] = (byte)save.BestStars((TD_Difficulty)i);
                unlocked[i] = ModeUnlocked(connection, (TD_Difficulty)i);
            }
            InstanceFinder.ServerManager.Broadcast(connection, new TD_ProfileBroadcast {
                bestWave = save.bestWave, totalKills = save.totalKills, gamesWon = save.gamesWon, gamesPlayed = save.gamesPlayed, newBestWave = newBestWave,
                bestStars = stars, bestEndlessWave = save.bestEndlessWave, modeUnlocked = unlocked, newBestEndless = newBestEndless,
            });
        }

        /// <summary>The player's abilities: which they own, and waves until each is ready in the current game.</summary>
        void SendAbilities(NetworkConnection connection) {
            if (connection == null || !connection.IsActive || !runs.TryGetValue(connection, out var run)) return;
            var unlocked = new bool[TD_Abilities.Count];
            var left = new byte[TD_Abilities.Count];
            for (int i = 0; i < TD_Abilities.Count; i++) {
                var ability = (TD_Ability)i;
                unlocked[i] = retention.IsUnlocked(connection, TD_Abilities.Get(ability).unlockId);
                left[i] = (byte)Mathf.Clamp(TD_Abilities.WavesLeft(ability, run.lastUsed[i], Mathf.Max(1, wave)), 0, 255);
            }
            InstanceFinder.ServerManager.Broadcast(connection, new TD_AbilityStateBroadcast { unlocked = unlocked, wavesLeft = left });
        }

        static void SetMusic(MusicSelection selection) {
            if (SharedGlobalEvents.Instance != null && SharedGlobalEvents.Instance.MusicTrack.Value != selection)
                SharedGlobalEvents.Instance.MusicTrack.Value = selection;
        }

        /// <summary>Debug/probe hook: add team cash.</summary>
        public void GrantCash(int amount) { sim.Cash += amount; MarkDirty(); }

        protected override void Reset() {
            if (!Application.isPlaying) return;
            phase = TD_Phase.Waiting;
            sim.Reset(TD_Difficulty.Normal, modifier);
            fastVotes.Clear();
            speed = 1f;
            base.Reset();
        }

        protected override void OnDestroy() {
            GameCharacter.FallHeightOverride = null;
            PlayerData.OnPlayerRemoved -= OnPlayerLeft;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnPosition) ServerPlayerCharacter.SpawnLocationFunction = null;
            if (retention != null) {
                retention.Unlocked -= OnUnlocked;
                retention.Ready -= OnRetentionReady;
                retention.Dispose();
            }
            var server = InstanceFinder.ServerManager;
            if (server != null) {
                server.UnregisterBroadcast<TD_BuildRequest>(OnBuildRequest);
                server.UnregisterBroadcast<TD_StateRequest>(OnStateRequest);
                server.UnregisterBroadcast<TD_ReadyRequest>(OnReadyRequest);
                server.UnregisterBroadcast<TD_SellRequest>(OnSellRequest);
                server.UnregisterBroadcast<TD_SpeedRequest>(OnSpeedRequest);
                server.UnregisterBroadcast<TD_ModeVoteRequest>(OnModeVoteRequest);
                server.UnregisterBroadcast<TD_AbilityRequest>(OnAbilityRequest);
            }
            base.OnDestroy();
        }
    }
}
