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
    /// on towers and upgrades, and the base falls when it runs out of health. The whole simulation
    /// (movement, targeting, damage, money) runs here; clients get snapshots and draw them.
    /// </summary>
    public sealed class TD_ServerRunner : ServerRunner {
        [Header("Pacing")]
        [SerializeField, Min(3)] int firstBuildSeconds = 30;
        [SerializeField, Min(3)] int buildSeconds = 12;
        [SerializeField, Min(3)] int resultsSeconds = 12;
        [SerializeField, Range(1, TD_Rules.WaveCount)] int waveCount = TD_Rules.WaveCount;
        [SerializeField, Min(0.05f)] float snapshotInterval = 0.1f;
        [Header("Rewards (account XP/gold)")]
        [SerializeField] ulong waveXP = 6;
        [SerializeField] ulong victoryXP = 120;
        [SerializeField] ulong victoryGold = 60;

        const string KillsColumn = "Kills";
        const string BuiltColumn = "Built";

        sealed class Enemy {
            public ushort id;
            public TD_EnemyKind kind;
            public float distance, health, maxHealth, slowUntil, slowFactor = 1f;
            public bool Flying => TD_Rules.Enemy(kind).flying;
        }

        sealed class Tower {
            public byte slot;
            public TD_TowerKind kind;
            public int level = 1;
            public float cooldown;
            public NetworkConnection owner;
            public string ownerName;
        }

        readonly List<Enemy> enemies = new();
        readonly Dictionary<int, Tower> towers = new();
        readonly List<TD_Shot> shots = new();
        readonly List<ushort> killed = new();
        readonly HashSet<NetworkConnection> ready = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        TD_Map map;
        TD_Phase phase;
        int wave, cash, baseHealth, secondsLeft, spawnsPending;
        ushort nextId = 1;
        float snapshotTimer;
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
        }

        void OnPlayerLeft(PlayerData player) {
            ready.Remove(player.Owner);
            requestTimes.Remove(player.Owner);
            foreach (var tower in towers.Values) if (tower.owner == player.Owner) tower.owner = null;
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
            cash = TD_Rules.StartCash;
            baseHealth = TD_Rules.BaseHealth;
            wave = 0;
            headline = "";
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            MarkDirty();
            await WaitForPlayersAsync(1, token);

            for (wave = 1; wave <= waveCount; wave++) {
                phase = TD_Phase.Build;
                ready.Clear();
                headline = wave == 1 ? "Build towers on the pads beside the road!" : $"Wave {wave - 1} cleared! +${TD_Rules.WaveBonus(wave - 1)}";
                int seconds = wave == 1 ? firstBuildSeconds : buildSeconds;
                for (float left = seconds; left > 0f; left -= 0.25f) {
                    secondsLeft = Mathf.CeilToInt(left);
                    SetTopMessage($"Wave {wave}/{waveCount} in {secondsLeft}   ({ReadyText()})");
                    if (AllReady()) break;
                    BroadcastStateThrottled();
                    await AwaitTime(250, token);
                }

                phase = TD_Phase.Wave;
                headline = $"Wave {wave}: {DescribeWave(wave)}";
                MarkDirty();
                SpawnWave(wave, token).Forget(e => { if (e is not OperationCanceledException) Debug.LogException(e, this); });
                while (spawnsPending > 0 || enemies.Count > 0) {
                    token.ThrowIfCancellationRequested();
                    if (baseHealth <= 0) break;
                    SetTopMessage($"Wave {wave}/{waveCount}: {spawnsPending + enemies.Count} enemies left   Base {baseHealth}/{TD_Rules.BaseHealth}");
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
            foreach (var player in GetActivePlayers())
                Award(player.Owner, won ? victoryXP : (ulong)(4 * reached), won ? victoryGold : 0);
            enemies.Clear();
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
                    if (group.delay > 0f) await AwaitTime(Mathf.RoundToInt(group.delay * 1000f), token);
                    for (int i = 0; i < group.count; i++) {
                        if (phase != TD_Phase.Wave) return;
                        var stats = TD_Rules.Enemy(group.kind);
                        float hp = stats.health * TD_Rules.HealthScale(number);
                        enemies.Add(new Enemy { id = nextId++, kind = group.kind, health = hp, maxHealth = hp });
                        if (nextId == 0) nextId = 1;
                        spawnsPending--;
                        await AwaitTime(Mathf.RoundToInt(group.spacing * 1000f), token);
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

        string ReadyText() {
            var active = GetActivePlayers();
            return $"{active.Count(p => ready.Contains(p.Owner))}/{active.Length} ready, press R to skip";
        }

        // ---------------------------------------------------------------- simulation
        void Update() {
            if (map == null) return;
            float dt = Time.deltaTime, now = Time.time;
            if (phase == TD_Phase.Wave) {
                float end = TD_Rules.LaneTrack.Length;
                for (int i = enemies.Count - 1; i >= 0; i--) {
                    var e = enemies[i];
                    float slow = now < e.slowUntil ? e.slowFactor : 1f;
                    e.distance += TD_Rules.Enemy(e.kind).speed * slow * dt;
                    if (e.distance < end) continue;
                    baseHealth = Mathf.Max(0, baseHealth - TD_Rules.Enemy(e.kind).leakDamage);
                    killed.Add(e.id);
                    enemies.RemoveAt(i);
                    MarkDirty();
                }
                foreach (var tower in towers.Values) Fire(tower, dt, now);
            }
            snapshotTimer += dt;
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
            Enemy target = null;
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
                foreach (var e in enemies.ToList())
                    if (!e.Flying && (TD_Rules.LaneTrack.Evaluate(e.distance) - centre).sqrMagnitude <= stats.splash * stats.splash)
                        Hit(tower, e, e == target ? 1f : 0.6f);
            } else {
                Hit(tower, target, 1f);
            }
            if (stats.slow > 0f) {
                target.slowUntil = now + TD_Rules.SlowSeconds;
                target.slowFactor = 1f - stats.slow * (1f + 0.15f * (tower.level - 1));
            }
        }

        void Hit(Tower tower, Enemy e, float scale) {
            if (e.health <= 0f) return;
            e.health -= TD_Rules.HitDamage(tower.kind, tower.level, e.kind) * scale;
            if (e.health > 0f) return;
            enemies.Remove(e);
            killed.Add(e.id);
            cash += TD_Rules.Enemy(e.kind).reward;
            if (tower.owner != null && PlayerData.TryGetPlayerData(tower.owner, out var player)) RoundLeaderboard.Add(player, KillsColumn, 1);
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
                MarkDirty();
                return $"{TD_Rules.Tower(tower.kind).name} upgraded to level {tower.level}!";
            }
            var stats = TD_Rules.Tower(kind);
            if (cash < stats.cost) return $"{stats.name} costs ${stats.cost}. Team has ${cash}.";
            cash -= stats.cost;
            towers[slot] = new Tower { slot = (byte)slot, kind = kind, owner = connection, ownerName = name };
            accepted = true;
            Count(connection);
            MarkDirty();
            return $"Built a {stats.name}!";
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
            if (!connection.IsAuthenticated || phase != TD_Phase.Build) return;
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
            towers = towers.Values.Select(t => new TD_TowerSnap { slot = t.slot, kind = t.kind, level = (byte)t.level, owner = t.ownerName }).ToArray()
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
            if (enemies.Count == 0 && shots.Count == 0 && killed.Count == 0 && phase != TD_Phase.Wave) return;
            InstanceFinder.ServerManager.Broadcast(new TD_SnapshotBroadcast {
                time = Time.time,
                enemies = enemies.Select(e => new TD_EnemySnap {
                    id = e.id, kind = e.kind, distance = e.distance,
                    health = (byte)Mathf.Clamp(Mathf.CeilToInt(e.health / e.maxHealth * 255f), 1, 255),
                    slowed = Time.time < e.slowUntil
                }).ToArray(),
                shots = shots.ToArray(),
                killed = killed.ToArray()
            });
            shots.Clear();
            killed.Clear();
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
            }
            base.OnDestroy();
        }
    }
}
