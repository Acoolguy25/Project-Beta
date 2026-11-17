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

namespace Universes.UniverseData.tower_defense.Server {
    /// <summary>
    /// Co-op tower defense: enemies roll down the lane wave after wave, players spend a shared purse
    /// on towers and upgrades (and sell towers for a partial refund), and the base falls when it runs
    /// out of health. Everyone ready skips the build countdown for a cash bonus, and a majority vote
    /// runs waves at double speed. The whole simulation (movement, targeting, damage, money) runs
    /// here; clients get snapshots and draw them.
    /// </summary>
    public sealed class TD_ServerRunner : ServerRunner {
        [Header("Pacing")]
        [SerializeField, Min(3)] int firstBuildSeconds = 30;
        [SerializeField, Min(3)] int buildSeconds = 12;
        [SerializeField, Min(3)] int resultsSeconds = 12;
        [SerializeField, Range(1, TD_Rules.WaveCount)] int waveCount = TD_Rules.WaveCount;
        [SerializeField, Min(0.05f)] float snapshotInterval = 0.1f;
        [Tooltip("Players may vote to run waves at TD_Rules.FastSpeed.")]
        [SerializeField] bool allowFastForward = true;
        [Header("Rewards (account XP/gold)")]
        [SerializeField] ulong waveXP = 6;
        [SerializeField] ulong victoryXP = 120;
        [SerializeField] ulong victoryGold = 60;

        const string KillsColumn = "Kills";
        const string BuiltColumn = "Built";

        sealed class Tower {
            public byte slot;
            public TD_TowerKind kind;
            public int level = 1;
            public float cooldown;
            public NetworkConnection owner;
            public string ownerName;
        }

        readonly List<TD_SimEnemy> enemies = new();
        readonly Dictionary<int, Tower> towers = new();
        readonly List<TD_Shot> shots = new();
        readonly List<ushort> killed = new();
        readonly List<ushort> leaked = new();
        readonly HashSet<NetworkConnection> ready = new();
        readonly HashSet<NetworkConnection> fastVotes = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly Dictionary<NetworkConnection, float> voteTimes = new();
        /// <summary>Players told about a new personal best this game (the toast shows once; the result card repeats it).</summary>
        readonly HashSet<NetworkConnection> newRecord = new();
        [Tooltip("Shortest time between two ready or fast-forward votes from one player (seconds).")]
        [SerializeField, Min(0f)] float voteInterval = 0.25f;
        TD_Map map;
        TD_Phase phase;
        int wave, cash, baseHealth, secondsLeft, spawnsPending, earlyBonus;
        ushort nextId = 1;
        float snapshotTimer, speed = 1f;
        /// <summary>Simulation clock: advances <see cref="speed"/> times faster than real time while fast forward runs.</summary>
        float simTime;
        string headline = "";
        bool stateDirty;

        public TD_Phase Phase => phase;
        public int Cash => cash;

        protected override void Awake() {
            base.Awake();
            // Ground sits at y = 0; keep the kill plane well below it.
            GameCharacter.FallHeightOverride = -20f;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnPosition;
            ServerPlayerCharacter.RespawnTime = 2f;
            PlayerData.OnPlayerRemoved += OnPlayerLeft;
            var server = InstanceFinder.ServerManager;
            server.RegisterBroadcast<TD_BuildRequest>(OnBuildRequest);
            server.RegisterBroadcast<TD_StateRequest>(OnStateRequest);
            server.RegisterBroadcast<TD_ReadyRequest>(OnReadyRequest);
            server.RegisterBroadcast<TD_SellRequest>(OnSellRequest);
            server.RegisterBroadcast<TD_SpeedRequest>(OnSpeedRequest);
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
            SendState(player.Owner);
            LoadProfile(player).Forget(e => Debug.LogException(e, this));
        }

        async UniTask LoadProfile(PlayerData player) {
            var save = await PlayerProgress.Load<TD_SaveData>(player, TD_SaveData.Universe);
            if (player != null && player.Owner != null && player.Owner.IsActive) SendProfile(player.Owner, save, false);
        }

        void OnPlayerLeft(PlayerData player) {
            ready.Remove(player.Owner);
            requestTimes.Remove(player.Owner);
            voteTimes.Remove(player.Owner);
            newRecord.Remove(player.Owner);
            fastVotes.Remove(player.Owner);
            foreach (var tower in towers.Values) if (tower.owner == player.Owner) tower.owner = null;
            UpdateSpeed();
        }

        // ---------------------------------------------------------------- round
        protected override async UniTask StartAsync(CancellationToken token) {
            phase = TD_Phase.Waiting;
            await WaitForSceneAsync("tower_defense_start", token);
            map = FindAnyObjectByType<TD_Map>();
            if (map == null || !map.IsConfigured) throw new InvalidOperationException("Tower defense requires a configured TD_Map in tower_defense_start.");
            SetGlobalInvul(true);
            SetTeamKillEnabled(false);
            SetLeaderboardEnabled(KillsColumn, true);
            SetLeaderboardEnabled(BuiltColumn, true);
            SetMusic(MusicSelection.GameMusic);
            enemies.Clear();
            towers.Clear();
            fastVotes.Clear();
            newRecord.Clear();
            speed = 1f;
            cash = TD_Rules.StartCash;
            baseHealth = TD_Rules.BaseHealth;
            wave = 0;
            earlyBonus = 0;
            headline = "";
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            MarkDirty();
            await WaitForPlayersAsync(1, token);

            for (wave = 1; wave <= waveCount; wave++) {
                phase = TD_Phase.Build;
                ready.Clear();
                earlyBonus = 0;
                headline = wave == 1 ? "Build towers on the pads beside the road!" : $"Wave {wave - 1} cleared! +${TD_Rules.WaveBonus(wave - 1)}";
                int seconds = wave == 1 ? firstBuildSeconds : buildSeconds;
                for (float left = seconds; left > 0f; left -= 0.25f) {
                    secondsLeft = Mathf.CeilToInt(left);
                    SetTopMessage($"Build phase: wave {wave} in {secondsLeft}s");
                    if (AllReady()) {
                        // Calling the wave early pays for the build time the team gave up.
                        earlyBonus = TD_Rules.EarlyStartBonus(wave, secondsLeft); // the same whole seconds the HUD promised
                        cash += earlyBonus;
                        break;
                    }
                    BroadcastStateThrottled();
                    await AwaitTime(250, token);
                }

                phase = TD_Phase.Wave;
                secondsLeft = 0;
                headline = earlyBonus > 0 ? $"Wave {wave} called early! +${earlyBonus}" : $"Wave {wave}: {DescribeWave(wave)}";
                RecordWave(wave);
                MarkDirty();
                SpawnWave(wave, token).Forget(e => { if (e is not OperationCanceledException) Debug.LogException(e, this); });
                while (spawnsPending > 0 || enemies.Count > 0) {
                    token.ThrowIfCancellationRequested();
                    if (baseHealth <= 0) break;
                    SetTopMessage($"Wave {wave}/{waveCount} in progress");
                    BroadcastStateThrottled();
                    await AwaitTime(250, token);
                }
                if (baseHealth <= 0) break;
                cash += TD_Rules.WaveBonus(wave);
                foreach (var player in GetActivePlayers()) Award(player.Owner, waveXP, 0);
                MarkDirty();
            }

            bool won = baseHealth > 0;
            phase = won ? TD_Phase.Victory : TD_Phase.Defeat;
            int reached = Mathf.Min(wave, waveCount);
            headline = won ? $"VICTORY! The base held all {waveCount} waves." : $"The base fell on wave {reached}.";
            SetMusic(won ? MusicSelection.VictoryMusic : MusicSelection.GameMusic);
            foreach (var player in GetActivePlayers()) {
                Award(player.Owner, won ? victoryXP : (ulong)(4 * reached), won ? victoryGold : 0);
                var owner = player.Owner;
                PlayerProgress.Modify<TD_SaveData>(player, save => {
                    save.gamesPlayed++;
                    if (won) save.gamesWon++;
                    SendProfile(owner, save, newRecord.Contains(owner));
                });
            }
            enemies.Clear();
            fastVotes.Clear();
            speed = 1f;
            ServerChat.SendSystemMessage(new(headline, SystemMessageSource.CustomMessage));
            for (int i = resultsSeconds; i > 0; i--) {
                secondsLeft = i;
                SetTopMessage($"{headline}  New game in {i}");
                MarkDirty();
                await AwaitTime(1000, token);
            }
        }

        async UniTask SpawnWave(int number, CancellationToken token) {
            var groups = TD_Rules.Wave(number);
            spawnsPending = groups.Sum(g => g.count);
            try {
                foreach (var group in groups) {
                    // speed is read once per wait: a fast-forward vote during a long gap (8 s between
                    // bosses) takes effect at the next spawn, which is fine for pacing.
                    if (group.delay > 0f) await AwaitTime(Mathf.RoundToInt(group.delay / speed * 1000f), token);
                    for (int i = 0; i < group.count; i++) {
                        if (phase != TD_Phase.Wave) return;
                        var stats = TD_Rules.Enemy(group.kind);
                        float hp = stats.health * TD_Rules.HealthScale(number);
                        enemies.Add(new TD_SimEnemy { id = nextId++, kind = group.kind, health = hp, maxHealth = hp });
                        if (nextId == 0) nextId = 1;
                        spawnsPending--;
                        await AwaitTime(Mathf.RoundToInt(group.spacing / speed * 1000f), token);
                    }
                }
            } finally {
                spawnsPending = 0;
            }
        }

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

        /// <summary>Saves the wave as fought in for every player on the server and tells those who just beat their record.</summary>
        void RecordWave(int number) {
            foreach (var player in PlayerData.Players.Values) {
                var owner = player.Owner;
                PlayerProgress.Modify<TD_SaveData>(player, save => {
                    // A first game sets the record quietly; beating an earlier best is celebrated once per
                    // game (the toast), and the result card says it again at the end.
                    bool hadRecord = save.bestWave > 0;
                    if (!save.RecordWave(number)) return;
                    bool announce = hadRecord && newRecord.Add(owner);
                    SendProfile(owner, save, announce);
                });
            }
        }

        // ---------------------------------------------------------------- simulation
        void Update() {
            if (map == null) return;
            float dt = Time.deltaTime;
            if (phase == TD_Phase.Wave) {
                dt *= speed;
                simTime += dt;
                float now = simTime;
                int leakDamage = TD_Battle.Advance(enemies, dt, now, leaked);
                if (leakDamage > 0) {
                    baseHealth = Mathf.Max(0, baseHealth - leakDamage);
                    MarkDirty();
                }
                foreach (var tower in towers.Values) Fire(tower, dt, now);
            }
            snapshotTimer += Time.deltaTime;
            if (snapshotTimer >= snapshotInterval) {
                snapshotTimer = 0f;
                BroadcastSnapshot();
            }
            if (stateDirty) BroadcastState();
        }

        void Fire(Tower tower, float dt, float now) {
            tower.cooldown -= dt;
            if (tower.cooldown > 0f) return;
            Vector3 at = TD_Rules.Slots[tower.slot];
            float range = TD_Rules.Range(tower.kind, tower.level);
            TD_SimEnemy target = null;
            foreach (var e in enemies) {
                if (!TD_Rules.CanHit(tower.kind, e.kind)) continue;
                Vector3 p = TD_Rules.LaneTrack.Evaluate(e.distance);
                if ((new Vector2(p.x - at.x, p.z - at.z)).sqrMagnitude > range * range) continue;
                // Frost prefers enemies that are not slowed yet; everyone shoots the one furthest along.
                bool better = target == null;
                if (!better && tower.kind == TD_TowerKind.Frost && (now >= e.slowUntil) != (now >= target.slowUntil)) better = now >= e.slowUntil;
                else if (!better) better = e.distance > target.distance;
                if (better) target = e;
            }
            if (target == null) { tower.cooldown = 0f; return; }
            tower.cooldown = 1f / TD_Rules.FireRate(tower.kind, tower.level);
            shots.Add(new TD_Shot { slot = tower.slot, enemy = target.id });
            var stats = TD_Rules.Tower(tower.kind);
            if (stats.splash > 0f) {
                Vector3 centre = TD_Rules.LaneTrack.Evaluate(target.distance);
                for (int i = enemies.Count - 1; i >= 0; i--) { // backwards: a kill removes the enemy
                    if (i >= enemies.Count) continue;
                    var e = enemies[i];
                    if (!e.Flying && (TD_Rules.LaneTrack.Evaluate(e.distance) - centre).sqrMagnitude <= stats.splash * stats.splash)
                        Hit(tower, e, e == target ? 1f : 0.6f);
                }
            } else {
                Hit(tower, target, 1f);
            }
            if (stats.slow > 0f) {
                target.slowUntil = now + TD_Rules.SlowSeconds;
                target.slowFactor = TD_Rules.SlowFactor(tower.kind, tower.level);
            }
        }

        void Hit(Tower tower, TD_SimEnemy e, float scale) {
            int reward = TD_Battle.Damage(enemies, e, TD_Rules.HitDamage(tower.kind, tower.level, e.kind) * scale, killed);
            if (reward <= 0) return;
            cash += reward;
            if (tower.owner != null && PlayerData.TryGetPlayerData(tower.owner, out var player)) {
                RoundLeaderboard.Add(player, KillsColumn, 1);
                bool boss = TD_Rules.IsBoss(e.kind);
                PlayerProgress.Modify<TD_SaveData>(player, save => {
                    save.totalKills++;
                    if (boss) save.bossesDestroyed++;
                });
            }
            MarkDirty();
        }

        // ---------------------------------------------------------------- requests
        void OnBuildRequest(NetworkConnection connection, TD_BuildRequest request, Channel channel) {
            if (!connection.IsAuthenticated || map == null) return;
            string message = TryBuild(connection, request.slot, request.kind, out bool accepted);
            InstanceFinder.ServerManager.Broadcast(connection, new TD_ResultBroadcast { accepted = accepted, message = message });
        }

        string TryBuild(NetworkConnection connection, int slot, TD_TowerKind kind, out bool accepted) {
            accepted = false;
            if (phase is TD_Phase.Victory or TD_Phase.Defeat) return "The game is over. New game soon!";
            if (slot < 0 || slot >= TD_Rules.Slots.Length || (int)kind < 0 || (int)kind >= TD_Rules.Towers.Length) return "That is not a tower pad.";
            if (!LocalCharacter.Characters.TryGetValue(connection, out var character) || character == null || character.IsDead) return "Respawn first.";
            if (!FocusPicker.InReach(character.transform.position, TD_Rules.Slots[slot], TD_Rules.SlotReach, 2.5f)) return "Walk closer to the pad.";
            string name = PlayerData.GetPlayerName(connection);
            if (towers.TryGetValue(slot, out var tower)) {
                if (tower.level >= TD_Rules.MaxLevel) return $"{TD_Rules.Tower(tower.kind).name} is fully upgraded.";
                int cost = TD_Rules.UpgradeCost(tower.kind, tower.level);
                if (cash < cost) return $"Upgrade costs ${cost}. Team has ${cash}.";
                cash -= cost;
                tower.level++;
                accepted = true;
                Count(connection);
                PlayerProgress.Modify<TD_SaveData>(connection, save => save.towersUpgraded++);
                BroadcastTowerEvent(TD_TowerEvent.Upgraded, tower, cost);
                MarkDirty();
                return $"{TD_Rules.Tower(tower.kind).name} upgraded to level {tower.level}!";
            }
            var stats = TD_Rules.Tower(kind);
            if (cash < stats.cost) return $"{stats.name} costs ${stats.cost}. Team has ${cash}.";
            cash -= stats.cost;
            var built = towers[slot] = new Tower { slot = (byte)slot, kind = kind, owner = connection, ownerName = name };
            accepted = true;
            Count(connection);
            PlayerProgress.Modify<TD_SaveData>(connection, save => save.towersBuilt++);
            BroadcastTowerEvent(TD_TowerEvent.Built, built, stats.cost);
            MarkDirty();
            return $"Built a {stats.name}!";
        }

        void OnSellRequest(NetworkConnection connection, TD_SellRequest request, Channel channel) {
            if (!connection.IsAuthenticated || map == null) return;
            string message = TrySell(connection, request.slot, out bool accepted);
            InstanceFinder.ServerManager.Broadcast(connection, new TD_ResultBroadcast { accepted = accepted, message = message });
        }

        string TrySell(NetworkConnection connection, int slot, out bool accepted) {
            accepted = false;
            if (phase is TD_Phase.Victory or TD_Phase.Defeat) return "The game is over. New game soon!";
            if (slot < 0 || slot >= TD_Rules.Slots.Length || !towers.TryGetValue(slot, out var tower)) return "There is no tower to sell there.";
            if (!LocalCharacter.Characters.TryGetValue(connection, out var character) || character == null || character.IsDead) return "Respawn first.";
            if (!FocusPicker.InReach(character.transform.position, TD_Rules.Slots[slot], TD_Rules.SlotReach, 2.5f)) return "Walk closer to the tower.";
            if (tower.owner != null && tower.owner != connection && tower.owner.IsActive) return $"Only {tower.ownerName} can sell this {TD_Rules.Tower(tower.kind).name}.";
            int refund = TD_Rules.SellRefund(tower.kind, tower.level);
            cash += refund;
            towers.Remove(slot);
            accepted = true;
            BroadcastTowerEvent(TD_TowerEvent.Sold, tower, refund);
            MarkDirty();
            return $"Sold the {TD_Rules.Tower(tower.kind).name} for ${refund}.";
        }

        /// <summary>False when <paramref name="connection"/> voted less than voteInterval ago (spam guard).</summary>
        bool VoteAllowed(NetworkConnection connection) {
            if (voteTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < voteInterval) return false;
            voteTimes[connection] = Time.unscaledTime;
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

        static void Count(NetworkConnection connection) {
            if (PlayerData.TryGetPlayerData(connection, out var player)) RoundLeaderboard.Add(player, BuiltColumn, 1);
        }

        void OnStateRequest(NetworkConnection connection, TD_StateRequest request, Channel channel) {
            if (!connection.IsAuthenticated) return;
            if (requestTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < 1f) return;
            requestTimes[connection] = Time.unscaledTime;
            SendState(connection);
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

        TD_StateBroadcast MakeState() => new() {
            phase = phase, wave = Mathf.Max(1, Mathf.Min(wave, waveCount)), secondsLeft = secondsLeft, cash = cash, baseHealth = baseHealth,
            enemiesLeft = spawnsPending + enemies.Count, headline = headline,
            readyPlayers = (byte)Mathf.Min(255, ReadyCount()), activePlayers = (byte)Mathf.Min(255, GetActivePlayers().Length),
            towers = towers.Values.Select(t => new TD_TowerSnap { slot = t.slot, kind = t.kind, level = (byte)t.level, owner = t.ownerName }).ToArray(),
            earlyBonus = earlyBonus, speed = speed, fastPlayers = (byte)Mathf.Min(255, FastCount()), fastForwardAllowed = allowFastForward
        };

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
            if (enemies.Count == 0 && shots.Count == 0 && killed.Count == 0 && leaked.Count == 0 && phase != TD_Phase.Wave) return;
            InstanceFinder.ServerManager.Broadcast(new TD_SnapshotBroadcast {
                time = Time.time,
                enemies = enemies.Select(e => new TD_EnemySnap {
                    id = e.id, kind = e.kind, distance = e.distance,
                    health = (byte)Mathf.Clamp(Mathf.CeilToInt(e.health / e.maxHealth * 255f), 1, 255),
                    slowed = simTime < e.slowUntil,
                    slow = (byte)Mathf.RoundToInt(e.Slow(simTime) * 255f)
                }).ToArray(),
                shots = shots.ToArray(),
                killed = killed.ToArray(),
                leaked = leaked.ToArray()
            });
            shots.Clear();
            killed.Clear();
            leaked.Clear();
        }

        void BroadcastTowerEvent(TD_TowerEvent kind, Tower tower, int amount) =>
            InstanceFinder.ServerManager.Broadcast(new TD_TowerEventBroadcast {
                kind = kind, slot = tower.slot, tower = tower.kind, level = (byte)tower.level, amount = amount
            });

        void SendProfile(NetworkConnection connection, TD_SaveData save, bool newBestWave) {
            if (connection == null || !connection.IsActive) return;
            InstanceFinder.ServerManager.Broadcast(connection, new TD_ProfileBroadcast {
                bestWave = save.bestWave, totalKills = save.totalKills, gamesWon = save.gamesWon, gamesPlayed = save.gamesPlayed, newBestWave = newBestWave
            });
        }

        static void Award(NetworkConnection connection, ulong xp, ulong gold) {
            if ((xp != 0 || gold != 0) && !LevelsServer.AwardPlayerXPAndGold(connection, xp, gold))
                Debug.LogWarning($"Tower defense could not award rewards to connection {connection.ClientId}.");
        }

        static void SetMusic(MusicSelection selection) {
            if (SharedGlobalEvents.Instance != null && SharedGlobalEvents.Instance.MusicTrack.Value != selection)
                SharedGlobalEvents.Instance.MusicTrack.Value = selection;
        }

        /// <summary>Debug/probe hook: add team cash.</summary>
        public void GrantCash(int amount) { cash += amount; MarkDirty(); }

        protected override void Reset() {
            if (!Application.isPlaying) return;
            phase = TD_Phase.Waiting;
            enemies.Clear();
            leaked.Clear();
            fastVotes.Clear();
            speed = 1f;
            base.Reset();
        }

        protected override void OnDestroy() {
            GameCharacter.FallHeightOverride = null;
            PlayerData.OnPlayerRemoved -= OnPlayerLeft;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnPosition) ServerPlayerCharacter.SpawnLocationFunction = null;
            var server = InstanceFinder.ServerManager;
            if (server != null) {
                server.UnregisterBroadcast<TD_BuildRequest>(OnBuildRequest);
                server.UnregisterBroadcast<TD_StateRequest>(OnStateRequest);
                server.UnregisterBroadcast<TD_ReadyRequest>(OnReadyRequest);
                server.UnregisterBroadcast<TD_SellRequest>(OnSellRequest);
                server.UnregisterBroadcast<TD_SpeedRequest>(OnSpeedRequest);
            }
            base.OnDestroy();
        }
    }
}
