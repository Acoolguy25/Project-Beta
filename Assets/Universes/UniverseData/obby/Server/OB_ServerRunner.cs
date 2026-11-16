using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Levels.Server;
using RyanAssets.Platformer;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using UnityEngine;
using Channel = FishNet.Transporting.Channel;

namespace Universes.UniverseData.obby.Server {
    /// <summary>
    /// Runs obby races: picks a seed, has every peer build the same course, and decides checkpoints,
    /// coins, hazards, falls and finishes from the replicated character positions.
    /// </summary>
    public sealed class OB_ServerRunner : ServerRunner {
        [Header("Race")]
        [SerializeField, Range(1, OB_CourseGenerator.MaxStages)] int stageCount = 30;
        [SerializeField, Min(30)] int raceSeconds = 1500;
        [Tooltip("Once the first player finishes, everyone else has this long to finish too.")]
        [SerializeField, Min(5)] int closingSeconds = 45;
        [SerializeField, Min(1)] int countdownSeconds = 5;
        [SerializeField, Min(3)] int resultsSeconds = 10;
        [Tooltip("Zero makes a fresh course each race. Nonzero reproduces one course for testing.")]
        [SerializeField] int fixedSeed;
        [Tooltip("Respawn delay after a reset or other death. Falls and hazards teleport instead.")]
        [SerializeField, Min(0.1f)] float respawnSeconds = 1.5f;
        [Tooltip("Hazard and fall checks are skipped this long after a teleport, while the new position arrives.")]
        [SerializeField, Min(0f)] float teleportGrace = 0.6f;
        [Tooltip("Seconds between touching a hazard and the respawn, while the client shows the character knocked limp.")]
        [SerializeField, Min(0f)] float hazardRespawnDelay = 1.2f;
        [Header("Rewards")]
        [SerializeField] ulong checkpointXP = 5;
        [SerializeField] ulong coinGold = 3;
        [SerializeField] ulong finishXP = 60;
        [SerializeField] ulong finishGold = 25;
        [Tooltip("Extra gold for the top three of the final results.")]
        [SerializeField] ulong[] podiumGold = { 30, 15, 5 };
        [Header("Scoring")]
        [Tooltip("Seconds each collected coin takes off a finish time. Results are ranked by this scored time.")]
        [SerializeField, Min(0f)] float coinTimeBonus = 0.5f;
        [Tooltip("Scored seconds per stage for each medal.")]
        [SerializeField] OB_MedalTimes medalTimes = OB_MedalTimes.Default;
        [Tooltip("Shortest gap between standings updates (the race strip on every client).")]
        [SerializeField, Min(0.05f)] float standingsInterval = 0.5f;

        const string StageColumn = "Stage";
        const string CoinColumn = "Coins";
        const float CoinRadius = 1.7f;
        const float HazardSkin = 0.05f;

        sealed class Runner {
            public int checkpoint;
            public ulong coins;
            public bool finished;
            public float finishTime;
            /// <summary>Finish time minus the coin bonus.</summary>
            public float scoredTime;
            public OB_Medal medal;
            public bool personalBest;
            /// <summary>Provisional rank among finishers when this runner finished.</summary>
            public int place;
            /// <summary>Race time of the last checkpoint, coin or finish (the split clients compare).</summary>
            public float lastEventTime;
            public float graceUntil;
            /// <summary>When a pending hazard respawn happens (0 when none is pending).</summary>
            public float respawnAt;
        }

        readonly Dictionary<NetworkConnection, Runner> runners = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly List<HazardVolume> hazards = new();
        OB_Course course;
        OB_CourseLayout layout;
        OB_Phase phase;
        int round, finishCount, lastSeed, secondsLeft;
        float raceStart, raceDeadline;
        string headline = "";
        float previousRespawnTime;
        bool standingsDirty;
        float nextStandings;

        public OB_CourseLayout Layout => layout;
        public OB_Phase Phase => phase;

        protected override void Awake() {
            base.Awake();
            previousRespawnTime = ServerPlayerCharacter.RespawnTime;
            ServerPlayerCharacter.RespawnTime = respawnSeconds;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnPosition;
            PlayerData.OnPlayerRemoved += OnPlayerLeft;
            InstanceFinder.ServerManager.RegisterBroadcast<OB_CourseRequest>(OnCourseRequest);
        }

        // ---------------------------------------------------------------- players
        Runner RunnerFor(NetworkConnection connection) {
            if (!runners.TryGetValue(connection, out Runner runner)) runners[connection] = runner = new Runner();
            return runner;
        }

        void PreparePlayer(PlayerData player) {
            player.cameraTypes.Clear();
            // Platforming needs to see the character's feet; the camera picks the highest available type,
            // so offering first person too would make it the default.
            player.LockCamera(GameCameraType.ThirdPersonCamera);
            if (course != null) {
                player.walkSpeed.Value = course.movement.walkSpeed;
                player.sprintSpeed.Value = course.sprintSpeed;
                player.jumpHeight.Value = course.movement.jumpHeight;
                player.gravity.Value = -course.movement.gravity;
            }
            player.staminaMax.Value = 250f;
            player.staminaRegen.Value = 60f;
        }

        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            RunnerFor(player.Owner);
            PreparePlayer(player);
            SendCourse(player.Owner);
            standingsDirty = true;
            PlayerProgress.Load<OB_SaveData>(player, OB_SaveData.Universe).Forget();
        }

        protected override void OnCharacterAdded(LocalCharacter character) {
            base.OnCharacterAdded(character);
            character.CanSpectate.Value = true;
            RunnerFor(character.Owner).graceUntil = Time.time + teleportGrace;
            if (PlayerData.TryGetPlayerData(character.Owner, out var player)) PreparePlayer(player);
        }

        void OnPlayerLeft(PlayerData player) {
            runners.Remove(player.Owner);
            requestTimes.Remove(player.Owner);
            standingsDirty = true;
        }

        Vector3 SpawnPosition(NetworkConnection connection) {
            Vector3 top = course != null ? course.LobbySpawn : Vector3.up * 101f;
            if (layout != null && runners.TryGetValue(connection, out Runner runner)) {
                int stage = Mathf.Clamp(runner.checkpoint, 0, layout.StageCount - 1);
                top = runner.finished ? layout.finishTop : layout.stages[stage].spawn;
            }
            Vector2 scatter = UnityEngine.Random.insideUnitCircle * 1.5f;
            return top + new Vector3(scatter.x, 1.2f, scatter.y);
        }

        // ---------------------------------------------------------------- round
        protected override async UniTask StartAsync(CancellationToken token) {
            phase = OB_Phase.Waiting;
            await WaitForSceneAsync("obby_start", token);
            course = FindAnyObjectByType<OB_Course>();
            if (course == null || !course.IsConfigured)
                throw new InvalidOperationException("Obby requires a configured OB_Course in obby_start.");
            SetGlobalInvul(true);
            SetTeamKillEnabled(false);
            SetLeaderboardEnabled(StageColumn, true);
            SetLeaderboardEnabled(CoinColumn, true);
            SetMusic(MusicSelection.GameMusic);
            foreach (var player in PlayerData.Players.Values) PreparePlayer(player);
            await WaitForPlayersAsync(1, token);

            int seed;
            do { seed = fixedSeed != 0 ? fixedSeed : Guid.NewGuid().GetHashCode(); } while (fixedSeed == 0 && (seed == lastSeed || seed == 0));
            lastSeed = seed;
            round++;
            layout = course.Build(seed, stageCount);
            course.SetGateClosed(true);
            finishCount = 0;
            headline = "";
            foreach (var runner in runners.Values) ResetRunner(runner);
            foreach (var player in PlayerData.Players.Values) { RunnerFor(player.Owner); WriteLeaderboard(player); }
            InstanceFinder.ServerManager.Broadcast(MakeCourse());
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            foreach (var connection in runners.Keys) SendProgress(connection, OB_ProgressReason.Sync);
            SendStandings();

            phase = OB_Phase.Countdown;
            for (int i = countdownSeconds; i > 0; i--) {
                secondsLeft = i;
                SetTopMessage($"Course #{round}: {layout.StageCount} stages. Starting in {i}");
                BroadcastRound();
                await AwaitTime(1000, token);
            }

            phase = OB_Phase.Racing;
            course.SetGateClosed(false);
            raceStart = Time.time;
            raceDeadline = raceStart + raceSeconds;
            headline = "GO!";
            while (phase is OB_Phase.Racing or OB_Phase.Closing) {
                token.ThrowIfCancellationRequested();
                secondsLeft = Mathf.Max(0, Mathf.CeilToInt(raceDeadline - Time.time));
                if (secondsLeft == 0 || AllActiveFinished()) break;
                SetTopMessage(phase == OB_Phase.Closing
                    ? $"{finishCount} finished. Course closes in {OB_Scoring.Clock(secondsLeft)}"
                    : $"Course #{round}: {OB_Scoring.Clock(secondsLeft)} left");
                BroadcastRound();
                await AwaitTime(1000, token);
            }

            phase = OB_Phase.Results;
            course.SetGateClosed(true);
            SetMusic(MusicSelection.VictoryMusic);
            var final = FinalOrder();
            AwardPodium(final);
            InstanceFinder.ServerManager.Broadcast(MakeResults(final));
            var winner = final.Count > 0 && final[0].Value.finished ? final[0] : default;
            headline = winner.Key != null
                ? $"{PlayerData.GetPlayerName(winner.Key)} wins in {OB_Scoring.Clock(winner.Value.scoredTime, true)}!"
                : "Nobody finished this one. New course next!";
            for (int i = resultsSeconds; i > 0; i--) {
                secondsLeft = i;
                SetTopMessage($"{headline}  Next course in {i}");
                BroadcastRound();
                await AwaitTime(1000, token);
            }
            SetMusic(MusicSelection.GameMusic);
        }

        bool AllActiveFinished() {
            var active = GetActivePlayers();
            return active.Length > 0 && active.All(p => runners.TryGetValue(p.Owner, out Runner r) && r.finished);
        }

        // ---------------------------------------------------------------- authority checks
        void FixedUpdate() {
            if (layout == null) return;
            if (standingsDirty && Time.unscaledTime >= nextStandings) SendStandings();
            PhysicsScene scene = course.gameObject.scene.GetPhysicsScene();
            foreach (var pair in LocalCharacter.Characters) {
                LocalCharacter character = pair.Value;
                if (character == null || character.IsDead || !character.IsSpawned) continue;
                if (!runners.TryGetValue(pair.Key, out Runner runner)) continue;
                if (runner.respawnAt > 0f) {
                    if (Time.time >= runner.respawnAt) Respawn(character, runner);
                    continue;
                }
                if (Time.time < runner.graceUntil) continue;
                if (!character.TryGetComponent(out Collider body)) continue;
                Bounds bounds = body.bounds;
                Vector3 centre = bounds.center;
                bool racing = phase is OB_Phase.Racing or OB_Phase.Closing;

                if (racing && !runner.finished) {
                    for (int k = layout.StageCount - 1; k > runner.checkpoint; k--) {
                        if (!layout.stages[k].checkpoint.Contains(centre)) continue;
                        ReachCheckpoint(pair.Key, runner, k);
                        break;
                    }
                    if (layout.finish.Contains(centre)) Finish(pair.Key, runner);
                    for (int s = 0; s < layout.StageCount && s < 64; s++) {
                        ulong bit = 1UL << s;
                        if ((runner.coins & bit) != 0 || (centre - layout.stages[s].coin).sqrMagnitude > CoinRadius * CoinRadius) continue;
                        runner.coins |= bit;
                        runner.lastEventTime = RaceTime;
                        Award(pair.Key, 0, coinGold, "a coin");
                        PlayerProgress.Modify<OB_SaveData>(pair.Key, save => save.totalCoins++);
                        if (PlayerData.TryGetPlayerData(pair.Key, out var player)) WriteLeaderboard(player);
                        SendProgress(pair.Key, OB_ProgressReason.Coin);
                    }
                }

                PlatformerQuery.GetBox(body, HazardSkin, out Vector3 boxCenter, out Vector3 halfExtents, out Quaternion orientation);
                if (PlatformerQuery.Overlap(scene, boxCenter, halfExtents, orientation, hazards) > 0) {
                    SendBack(character, runner, OB_ProgressReason.Hazard);
                    continue;
                }
                float killY = runner.finished ? layout.finishTop.y - OB_CourseGenerator.KillDepth
                    : layout.stages[Mathf.Clamp(runner.checkpoint, 0, layout.StageCount - 1)].killY;
                if (bounds.min.y < killY) SendBack(character, runner, OB_ProgressReason.Fell);
            }
        }

        void ReachCheckpoint(NetworkConnection connection, Runner runner, int checkpoint) {
            int gained = checkpoint - runner.checkpoint;
            runner.checkpoint = checkpoint;
            runner.lastEventTime = RaceTime;
            standingsDirty = true;
            Award(connection, checkpointXP * (ulong)gained, 0, "a checkpoint");
            PlayerProgress.Modify<OB_SaveData>(connection, save => save.RecordStage(checkpoint));
            if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
            SendProgress(connection, OB_ProgressReason.Checkpoint);
        }

        void Finish(NetworkConnection connection, Runner runner) {
            runner.finished = true;
            runner.finishTime = RaceTime;
            runner.lastEventTime = runner.finishTime;
            runner.checkpoint = layout.StageCount;
            int stages = layout.StageCount;
            runner.scoredTime = OB_Scoring.ScoredTime(runner.finishTime, OB_Scoring.CountCoins(runner.coins), coinTimeBonus);
            runner.medal = medalTimes.Award(runner.scoredTime, stages);
            // Provisional: a later finisher with more coins can still beat this scored time.
            runner.place = 1 + runners.Values.Count(r => r != runner && r.finished && r.scoredTime < runner.scoredTime);
            bool firstFinisher = ++finishCount == 1;
            Award(connection, finishXP, finishGold, "finishing");
            float finishTime = runner.finishTime, scoredTime = runner.scoredTime;
            OB_Medal medal = runner.medal;
            bool personalBest = false;
            PlayerProgress.Modify<OB_SaveData>(connection, save => personalBest = save.RecordFinish(finishTime, scoredTime, medal, stages));
            runner.personalBest = personalBest;
            string name = PlayerData.GetPlayerName(connection);
            headline = $"{name} finished in {OB_Scoring.Clock(runner.scoredTime, true)}" + (runner.place == 1 ? ", the best time so far!" : "!");
            standingsDirty = true;
            if (firstFinisher && phase == OB_Phase.Racing) {
                phase = OB_Phase.Closing;
                raceDeadline = Mathf.Min(raceDeadline, Time.time + closingSeconds);
            }
            if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
            SendProgress(connection, OB_ProgressReason.Finish);
            BroadcastRound();
        }

        /// <summary>
        /// Returns a player to their checkpoint. Falls teleport at once; after a hazard the client first
        /// shows the character knocked limp, so the teleport waits <see cref="hazardRespawnDelay"/>.
        /// </summary>
        void SendBack(LocalCharacter character, Runner runner, OB_ProgressReason reason) {
            if (reason == OB_ProgressReason.Hazard && hazardRespawnDelay > 0f) runner.respawnAt = Time.time + hazardRespawnDelay;
            else Respawn(character, runner);
            SendProgress(character.Owner, reason);
        }

        void Respawn(LocalCharacter character, Runner runner) {
            runner.respawnAt = 0f;
            runner.graceUntil = Time.time + teleportGrace;
            character.TeleportTo(SpawnPosition(character.Owner));
        }

        // ---------------------------------------------------------------- scoring
        float RaceTime => phase is OB_Phase.Racing or OB_Phase.Closing ? Time.time - raceStart : 0f;

        static void ResetRunner(Runner runner) {
            runner.checkpoint = 0;
            runner.coins = 0;
            runner.finished = false;
            runner.finishTime = runner.scoredTime = runner.lastEventTime = 0f;
            runner.medal = OB_Medal.None;
            runner.personalBest = false;
            runner.place = 0;
        }

        OB_Standing StandingOf(NetworkConnection connection, Runner runner) => new() {
            clientId = connection.ClientId, name = PlayerData.GetPlayerName(connection), stage = runner.checkpoint,
            finished = runner.finished, scoredTime = runner.scoredTime
        };

        /// <summary>Active racers ranked for the results: finishers by scored time, then the furthest checkpoint.</summary>
        List<KeyValuePair<NetworkConnection, Runner>> FinalOrder() {
            var order = runners.Where(r => r.Key.IsActive).ToList();
            order.Sort((a, b) => OB_Scoring.Compare(StandingOf(a.Key, a.Value), StandingOf(b.Key, b.Value)));
            return order;
        }

        /// <summary>Podium gold, wins and podiums go by the final results, after every coin bonus is known.</summary>
        void AwardPodium(List<KeyValuePair<NetworkConnection, Runner>> final) {
            for (int i = 0; i < final.Count; i++) {
                if (!final[i].Value.finished) break;
                int place = i + 1;
                final[i].Value.place = place;
                if (i < podiumGold.Length) Award(final[i].Key, 0, podiumGold[i], "a podium place");
                PlayerProgress.Modify<OB_SaveData>(final[i].Key, save => save.RecordPlace(place));
            }
        }

        // ---------------------------------------------------------------- messages
        OB_CourseBroadcast MakeCourse() => new() {
            seed = layout?.seed ?? 0, stageCount = layout?.StageCount ?? 0, round = round, coinBonus = coinTimeBonus
        };

        void SendCourse(NetworkConnection connection) {
            if (layout == null || connection == null || !connection.IsActive) return;
            InstanceFinder.ServerManager.Broadcast(connection, MakeCourse());
            SendProgress(connection, OB_ProgressReason.Sync);
        }

        void OnCourseRequest(NetworkConnection connection, OB_CourseRequest request, Channel channel) {
            if (!connection.IsAuthenticated || layout == null) return;
            if (requestTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < 1f) return;
            requestTimes[connection] = Time.unscaledTime;
            SendCourse(connection);
        }

        void SendProgress(NetworkConnection connection, OB_ProgressReason reason) {
            if (layout == null || !runners.TryGetValue(connection, out Runner runner) || !connection.IsActive) return;
            InstanceFinder.ServerManager.Broadcast(connection, new OB_ProgressBroadcast {
                seed = layout.seed, checkpoint = runner.checkpoint, coins = runner.coins, finished = runner.finished,
                finishTime = runner.finishTime, place = runner.place, reason = reason,
                raceTime = runner.lastEventTime, scoredTime = runner.scoredTime, medal = runner.medal,
                personalBest = runner.personalBest
            });
        }

        void SendStandings() {
            standingsDirty = false;
            nextStandings = Time.unscaledTime + standingsInterval;
            if (layout == null) return;
            InstanceFinder.ServerManager.Broadcast(new OB_StandingsBroadcast {
                seed = layout.seed,
                racers = runners.Where(r => r.Key.IsActive).Select(r => StandingOf(r.Key, r.Value)).ToArray()
            });
        }

        void BroadcastRound() {
            if (layout == null) return;
            var leader = FinalOrder().FirstOrDefault();
            InstanceFinder.ServerManager.Broadcast(new OB_RoundBroadcast {
                seed = layout.seed, phase = phase, secondsLeft = secondsLeft, raceTime = RaceTime,
                leaderName = leader.Key != null ? PlayerData.GetPlayerName(leader.Key) : "",
                leaderStage = leader.Value?.checkpoint ?? 0, finishedCount = finishCount,
                playerCount = PlayerData.Players.Count, headline = headline
            });
        }

        OB_ResultsBroadcast MakeResults(List<KeyValuePair<NetworkConnection, Runner>> final) => new() {
            seed = layout.seed,
            entries = final.Select(r => new OB_ResultEntry {
                name = PlayerData.GetPlayerName(r.Key), finished = r.Value.finished, time = r.Value.finishTime,
                stage = r.Value.checkpoint, coins = OB_Scoring.CountCoins(r.Value.coins),
                scoredTime = r.Value.scoredTime, medal = r.Value.medal
            }).ToArray(),
            goldTime = medalTimes.Target(OB_Medal.Gold, layout.StageCount),
            silverTime = medalTimes.Target(OB_Medal.Silver, layout.StageCount),
            bronzeTime = medalTimes.Target(OB_Medal.Bronze, layout.StageCount),
            coinBonus = coinTimeBonus
        };

        void WriteLeaderboard(PlayerData player) {
            if (!runners.TryGetValue(player.Owner, out Runner runner)) return;
            SetLeaderboardValue(player, StageColumn, runner.checkpoint);
            SetLeaderboardValue(player, CoinColumn, OB_Scoring.CountCoins(runner.coins));
        }

        static void SetLeaderboardValue(PlayerData player, string column, int value) {
            int index = SharedGlobalEvents.GetLeaderboardIndex(column);
            if (index >= 0 && index < player.leaderboard.Count && player.leaderboard[index] != value)
                player.leaderboard[index] = value;
        }

        static void Award(NetworkConnection connection, ulong xp, ulong gold, string reason) {
            if (xp == 0 && gold == 0) return;
            if (!LevelsServer.AwardPlayerXPAndGold(connection, xp, gold))
                Debug.LogWarning($"Obby could not award {reason} rewards to connection {connection.ClientId}.");
        }

        static void SetMusic(MusicSelection selection) {
            if (SharedGlobalEvents.Instance != null && SharedGlobalEvents.Instance.MusicTrack.Value != selection)
                SharedGlobalEvents.Instance.MusicTrack.Value = selection;
        }

        protected override void Reset() {
            if (!Application.isPlaying) return;
            phase = OB_Phase.Waiting;
            base.Reset();
        }

        protected override void OnDestroy() {
            PlayerData.OnPlayerRemoved -= OnPlayerLeft;
            ServerPlayerCharacter.RespawnTime = previousRespawnTime;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnPosition) ServerPlayerCharacter.SpawnLocationFunction = null;
            if (InstanceFinder.ServerManager != null) InstanceFinder.ServerManager.UnregisterBroadcast<OB_CourseRequest>(OnCourseRequest);
            base.OnDestroy();
        }
    }
}
