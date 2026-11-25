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
using RyanAssets.Platformer;
using RyanAssets.Retention;
using RyanAssets.Retention.Server;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using UnityEngine;
using Channel = FishNet.Transporting.Channel;

namespace Universes.UniverseData.obby.Server {
    /// <summary>
    /// Runs obby races: picks a seed, has every peer build the same course, and decides checkpoints,
    /// coins, stars, hazards (including each player's rising-lava chase), falls and finishes from the
    /// replicated character positions. Rewards, levels and daily challenges go through the shared
    /// retention kit (see <see cref="OB_Retention"/>); tier times and stars are saved in
    /// <see cref="OB_SaveData"/>. The progress half of the runner is in OB_ServerRunner.Progress.cs.
    /// </summary>
    public sealed partial class OB_ServerRunner : ServerRunner {
        [Header("Race")]
        [SerializeField, Range(1, OB_CourseGenerator.MaxStages)] int stageCount = OB_CourseGenerator.DefaultStageCount;
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
        [Header("Rewards (paid through the retention kit)")]
        [Tooltip("XP for each checkpoint reached, paid as it happens.")]
        [SerializeField, Min(0)] int checkpointXP = 5;
        [Tooltip("XP for each star, paid as it happens.")]
        [SerializeField, Min(0)] int starXP = 25;
        [Tooltip("XP for beating your best time through a tier.")]
        [SerializeField, Min(0)] int tierRecordXP = 20;
        [Tooltip("Gold for each coin, paid on the end-of-race card.")]
        [SerializeField, Min(0)] int coinGold = 3;
        [SerializeField, Min(0)] int finishXP = 60;
        [SerializeField, Min(0)] int finishGold = 25;
        [Tooltip("XP for a gold, silver and bronze medal.")]
        [SerializeField] int[] medalXP = { 60, 35, 15 };
        [Tooltip("Extra gold for the top three of the final results (races with two or more racers only).")]
        [SerializeField] int[] podiumGold = { 30, 15, 5 };
        [Header("Retention (levels, unlocks, daily challenges, streak)")]
        [SerializeField] RetentionTuning retentionTuning = new();
        [Header("Scoring")]
        [Tooltip("Seconds each collected coin takes off a finish time. Results are ranked by this scored time.")]
        [SerializeField, Min(0f)] float coinTimeBonus = 0.5f;
        [Tooltip("Seconds each collected star takes off a finish time (a pro route costs time; this pays some back).")]
        [SerializeField, Min(0f)] float starTimeBonus = 3f;
        [Tooltip("Medal targets as multiples of the course par.")]
        [SerializeField] OB_MedalTimes medalTimes = OB_MedalTimes.Default;
        [Header("Stamina (the kit's pool; the dash has its own charge on CharacterDash)")]
        [Tooltip("Kit stamina pool. Courses are fair at walk speed and jump freely, and the Dash is limited by its own charge, so this " +
                 "stays 0 (stamina off). A value above 0 makes every jump cost stamina again.")]
        [SerializeField, Min(0f)] float staminaMax;
        [Tooltip("Kit stamina regained per second (only when staminaMax is above 0).")]
        [SerializeField, Min(0f)] float staminaRegen = 60f;
        [Tooltip("Seconds after spending kit stamina before it regenerates (only when staminaMax is above 0).")]
        [SerializeField, Min(0f)] float staminaCooldown;
        [Header("Stage envelope")]
        [Tooltip("Room around the course the server allows (see OB_StageEnvelope). A racer whose middle leaves it, or gets inside an out-of-bounds guard " +
                 "(the far side of a gravity wall or ceiling), goes back to their checkpoint. The dash (walk x 1.9) and launches stay well inside the margins.")]
        [SerializeField] OB_EnvelopeRules envelopeRules = OB_EnvelopeRules.Default;
        [Tooltip("Fastest sideways speed (m/s) a racer's position may average over speedWindow seconds (OB_SpeedSanity): dash plus a launch plus a ride stays well under it. " +
                 "Faster, and they go back to their checkpoint.")]
        [SerializeField, Min(10f)] float maxPlanarSpeed = OB_SpeedSanity.DefaultLimit;
        [SerializeField, Min(0.1f)] float speedWindow = 0.5f;
        [Header("Hazards")]
        [Tooltip("How far the robot's feet may dip below rising lava before it counts (forgives a frame of latency).")]
        [SerializeField, Min(0f)] float lavaMargin = 0.3f;
        [Tooltip("Laser gates and crushers are judged at the moment the player saw, not the server's now: this is " +
                 "how long a position spends in the server's network transform buffer after it arrives (s). " +
                 "Clock skew and travel time are measured per player from their view-clock messages.")]
        [SerializeField, Min(0f)] float positionBufferSeconds = 0.05f;
        [Tooltip("Upper bound on how far back a timed hazard is judged (s), so a client cannot claim an arbitrary delay.")]
        [SerializeField, Min(0f)] float maxHazardRewind = 0.35f;
        [Tooltip("A laser or crusher counts only while it has been deadly this long before, and stays deadly this long after, " +
                 "the moment the player saw (s): the same forgiveness at both ends of each deadly window.")]
        [SerializeField, Min(0f)] float hazardGrace = 0.05f;
        [Tooltip("How quickly a player's measured view delay follows new samples (0..1 per sample).")]
        [SerializeField, Range(0.01f, 1f)] float viewDelaySmoothing = 0.25f;
        [Tooltip("Shortest gap between standings updates (the race strip on every client).")]
        [SerializeField, Min(0.05f)] float standingsInterval = 0.5f;

        const string StageColumn = "Stage";
        const string CoinColumn = "Coins";
        const float CoinRadius = 1.7f;
        const float StarRadius = 1.9f;
        const float HazardSkin = 0.05f;

        sealed class Runner {
            public int checkpoint;
            public ulong coins;
            public ulong stars;
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
            /// <summary>Race time the current checkpoint was reached (orders racers on the same checkpoint).</summary>
            public float checkpointTime;
            public float graceUntil;
            /// <summary>When a pending hazard respawn happens (0 when none is pending).</summary>
            public float respawnAt;
            /// <summary>Race time the current tier started, and whether it has gone without a fall or hazard so far.</summary>
            public float tierStart;
            public bool tierClean = true;
            /// <summary>The last tier this runner closed, its time and the saved best before it (sent with the progress that closed it).</summary>
            public OB_Tier closedTier;
            public float closedTierTime, closedTierBest;
            /// <summary>Time this runner's rising-lava chase on the current stage started (negative: not started).</summary>
            public float lavaStart = -1f;
            /// <summary>In this race's retention summary (BeginMatch was called).</summary>
            public bool inMatch;
            /// <summary>Where this runner was when the speed window opened (sampleTime 0: no window yet).</summary>
            public Vector3 samplePosition;
            public float sampleTime;
        }

        readonly Dictionary<NetworkConnection, Runner> runners = new();
        readonly Dictionary<NetworkConnection, float> requestTimes = new();
        readonly List<HazardVolume> hazards = new();
        readonly TimedHazards timedHazards = new();
        /// <summary>Per player: how far the server's clock is ahead of the moment they saw (smoothed), see <see cref="OB_ViewClock"/>.</summary>
        readonly Dictionary<NetworkConnection, float> viewDelays = new();
        readonly Dictionary<NetworkConnection, float> viewClockTimes = new();
        RetentionService<OB_SaveData> retention;
        OB_Course course;
        OB_CourseLayout layout;
        OB_StageEnvelope envelope;
        OB_Phase phase;
        int round, finishCount, lastSeed, secondsLeft, builtRulesHash;
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
            // The HUD's level chip and the summary card already show a racer their new level, so their own
            // chat stays clear of it; everyone else still sees the line.
            retention = new RetentionService<OB_SaveData>(OB_Retention.Rules(retentionTuning), save => save.retention) {
                AnnounceLevelToSelf = false,
            };
            InstanceFinder.ServerManager.RegisterBroadcast<OB_CourseRequest>(OnCourseRequest);
            InstanceFinder.ServerManager.RegisterBroadcast<OB_ViewClock>(OnViewClock);
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
            player.staminaMax.Value = staminaMax;
            player.staminaRegen.Value = staminaRegen;
            player.staminaCooldown.Value = staminaCooldown;
        }

        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            Runner runner = RunnerFor(player.Owner);
            PreparePlayer(player);
            SendCourse(player.Owner);
            standingsDirty = true;
            PlayerProgress.Load<OB_SaveData>(player, OB_SaveData.Universe).Forget();
            retention.PlayerJoined(player);
            // Joining a race under way: the end-of-race card covers the rest of it.
            if (phase is OB_Phase.Countdown or OB_Phase.Racing or OB_Phase.Closing) BeginMatch(player.Owner, runner);
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
            viewDelays.Remove(player.Owner);
            viewClockTimes.Remove(player.Owner);
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
            builtRulesHash = (course.rules ?? OB_CourseRules.Default).Hash();
            envelope = new OB_StageEnvelope(layout, envelopeRules);
            IndexCourse();
            course.SetGateClosed(true);
            finishCount = 0;
            headline = "";
            foreach (var runner in runners.Values) ResetRunner(runner);
            foreach (var player in PlayerData.Players.Values) {
                BeginMatch(player.Owner, RunnerFor(player.Owner));
                WriteLeaderboard(player);
            }
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
            RecordPlaces(final);
            InstanceFinder.ServerManager.Broadcast(MakeResults(final));
            FinishMatches(final);
            var winner = final.Count > 0 && final[0].Value.finished ? final[0] : default;
            // Times are on every client's results board, so the top bar only names the winner.
            headline = winner.Key == null ? "Nobody finished this one. New course next!"
                : final.Count < 2 ? "Course complete!"
                : $"{NameOf(winner.Key)} wins!";
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
                if (runner.sampleTime <= 0f) { runner.samplePosition = centre; runner.sampleTime = Time.time; }
                else if (Time.time - runner.sampleTime >= speedWindow) {
                    bool tooFast = OB_SpeedSanity.Exceeds(runner.samplePosition, centre, Time.time - runner.sampleTime, maxPlanarSpeed);
                    runner.samplePosition = centre;
                    runner.sampleTime = Time.time;
                    if (tooFast && racing && !runner.finished) {
                        SendBack(character, runner, OB_ProgressReason.OutOfBounds);
                        continue;
                    }
                }

                if (racing && !runner.finished) {
                    for (int k = layout.StageCount - 1; k > runner.checkpoint; k--) {
                        if (!layout.stages[k].checkpoint.Contains(centre)) continue;
                        ReachCheckpoint(pair.Key, runner, k);
                        break;
                    }
                    if (layout.finish.Contains(centre)) Finish(pair.Key, runner);
                    CollectPickups(pair.Key, runner, centre);
                }

                PlatformerQuery.GetBox(body, HazardSkin, out Vector3 boxCenter, out Vector3 halfExtents, out Quaternion orientation);
                if (TouchesHazard(scene, pair.Key, boxCenter, halfExtents, orientation) || InRisingLava(runner, bounds)) {
                    SendBack(character, runner, OB_ProgressReason.Hazard);
                    continue;
                }
                // Falls are judged by the reached stage the player is physically over, so going back for a
                // missed star is safe; after a gravity flip a player can fall up, past the ceilings.
                float killY = layout.finishTop.y - OB_CourseGenerator.KillDepth, killTop = float.PositiveInfinity;
                if (!runner.finished) layout.KillHeights(runner.checkpoint, centre, out killY, out killTop);
                if (bounds.min.y < killY || bounds.max.y > killTop) SendBack(character, runner, OB_ProgressReason.Fell);
                // The far side of a gravity wall, the top of a ceiling, or anywhere the course does not reach is out of bounds.
                else if (envelope != null && !runner.finished && !envelope.Allows(runner.checkpoint, centre)) SendBack(character, runner, OB_ProgressReason.OutOfBounds);
            }
        }

        /// <summary>
        /// Whether the body box touches a hazard. Steady hazards (lava, spikes) are judged on the server's
        /// physics now; laser gates and crushers at the shared-clock moment this player saw (see
        /// <see cref="SeenTime"/>), so a player who visibly cleared a beam or a slam is not sent back.
        /// </summary>
        bool TouchesHazard(PhysicsScene scene, NetworkConnection connection, Vector3 center, Vector3 halfExtents, Quaternion orientation) {
            if (PlatformerQuery.Overlap(scene, center, halfExtents, orientation, hazards) > 0)
                foreach (var hazard in hazards)
                    if (!timedHazards.Contains(hazard)) return true;
            return timedHazards.Hits(center, halfExtents, orientation, SeenTime(connection), hazardGrace);
        }

        /// <summary>The shared-clock moment a player saw when they were where the server now has them.</summary>
        double SeenTime(NetworkConnection connection) {
            float delay = viewDelays.TryGetValue(connection, out float measured) ? measured : 0f;
            return PlatformerClock.Now - Mathf.Clamp(delay + positionBufferSeconds, 0f, maxHazardRewind);
        }

        void OnViewClock(NetworkConnection connection, OB_ViewClock message, Channel channel) {
            if (!connection.IsAuthenticated || double.IsNaN(message.time) || double.IsInfinity(message.time)) return;
            // A few samples a second are plenty; more are ignored.
            if (viewClockTimes.TryGetValue(connection, out float previous) && Time.unscaledTime - previous < 0.1f) return;
            viewClockTimes[connection] = Time.unscaledTime;
            float sample = Mathf.Clamp((float)(PlatformerClock.Now - message.time), 0f, maxHazardRewind);
            viewDelays[connection] = viewDelays.TryGetValue(connection, out float smoothed)
                ? Mathf.Lerp(smoothed, sample, viewDelaySmoothing)
                : sample;
        }

        void ReachCheckpoint(NetworkConnection connection, Runner runner, int checkpoint) {
            int previous = runner.checkpoint;
            int gained = checkpoint - previous;
            runner.checkpoint = checkpoint;
            runner.lastEventTime = runner.checkpointTime = RaceTime;
            runner.lavaStart = -1f;
            standingsDirty = true;
            retention.AddXp(connection, checkpointXP * gained, "Checkpoints", announce: false);
            retention.Count(connection, OB_Retention.CheckpointsStat, gained);
            for (int s = previous; s < checkpoint; s++) CountStageCleared(connection, layout.stages[s]);
            PlayerProgress.Modify<OB_SaveData>(connection, save => save.RecordStage(checkpoint));
            OB_Tier from = OB_Tiers.TierOf(previous, layout.StageCount), to = OB_Tiers.TierOf(checkpoint, layout.StageCount);
            if (to != from) {
                CloseTier(connection, runner, from);
                runner.tierStart = runner.checkpointTime;
                runner.tierClean = true;
                retention.Reach(connection, OB_Retention.TierStat, (int)to + 1);
            }
            if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
            SendProgress(connection, OB_ProgressReason.Checkpoint);
            runner.closedTierTime = 0f;
        }

        void Finish(NetworkConnection connection, Runner runner) {
            int previous = runner.checkpoint;
            runner.finished = true;
            runner.finishTime = RaceTime;
            runner.lastEventTime = runner.finishTime;
            runner.checkpoint = layout.StageCount;
            int stages = layout.StageCount;
            runner.scoredTime = OB_Scoring.ScoredTime(runner.finishTime, OB_Scoring.CountCoins(runner.coins), coinTimeBonus,
                OB_Scoring.CountCoins(runner.stars), starTimeBonus);
            runner.medal = medalTimes.Award(runner.scoredTime, layout.TotalPar);
            // Provisional: a later finisher with more coins can still beat this scored time.
            runner.place = 1 + runners.Values.Count(r => r != runner && r.finished && r.scoredTime < runner.scoredTime);
            bool firstFinisher = ++finishCount == 1;
            for (int s = previous; s < stages; s++) CountStageCleared(connection, layout.stages[s]);
            CloseTier(connection, runner, OB_Tiers.TierOf(stages - 1, stages));
            retention.Count(connection, OB_Retention.FinishStat);
            if (runner.medal >= OB_Medal.Silver) retention.Count(connection, OB_Retention.SilverStat);
            if (runner.medal == OB_Medal.Gold) retention.Count(connection, OB_Retention.GoldStat);
            float finishTime = runner.finishTime, scoredTime = runner.scoredTime;
            OB_Medal medal = runner.medal;
            bool personalBest = false;
            PlayerProgress.Modify<OB_SaveData>(connection, save => personalBest = save.RecordFinish(finishTime, scoredTime, medal, stages));
            runner.personalBest = personalBest;
            string name = NameOf(connection);
            headline = $"{name} finished in {OB_Scoring.Clock(runner.scoredTime, true)}" + (runner.place == 1 ? ", the best time so far!" : "!");
            standingsDirty = true;
            if (firstFinisher && phase == OB_Phase.Racing) {
                phase = OB_Phase.Closing;
                raceDeadline = Mathf.Min(raceDeadline, Time.time + closingSeconds);
            }
            if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
            SendProgress(connection, OB_ProgressReason.Finish);
            runner.closedTierTime = 0f;
            BroadcastRound();
        }

        /// <summary>
        /// Returns a player to their checkpoint. Falls teleport at once; after a hazard the client first
        /// shows the character knocked limp, so the teleport waits <see cref="hazardRespawnDelay"/>.
        /// </summary>
        void SendBack(LocalCharacter character, Runner runner, OB_ProgressReason reason) {
            runner.tierClean = false;
            if (reason == OB_ProgressReason.Hazard && hazardRespawnDelay > 0f) runner.respawnAt = Time.time + hazardRespawnDelay;
            else Respawn(character, runner);
            SendProgress(character.Owner, reason);
        }

        void Respawn(LocalCharacter character, Runner runner) {
            runner.respawnAt = 0f;
            runner.lavaStart = -1f;
            runner.graceUntil = Time.time + teleportGrace;
            runner.sampleTime = 0f;
            character.TeleportTo(SpawnPosition(character.Owner));
        }

        // ---------------------------------------------------------------- scoring
        float RaceTime => phase is OB_Phase.Racing or OB_Phase.Closing ? Time.time - raceStart : 0f;

        static void ResetRunner(Runner runner) {
            runner.checkpoint = 0;
            runner.coins = runner.stars = 0;
            runner.tierStart = 0f;
            runner.tierClean = true;
            runner.closedTierTime = runner.closedTierBest = 0f;
            runner.lavaStart = -1f;
            runner.inMatch = false;
            runner.finished = false;
            runner.finishTime = runner.scoredTime = runner.lastEventTime = runner.checkpointTime = 0f;
            runner.medal = OB_Medal.None;
            runner.personalBest = false;
            runner.place = 0;
        }

        /// <summary>A racer's standing; the name is only filled for the server's own ordering (clients have the names).</summary>
        OB_Standing StandingOf(NetworkConnection connection, Runner runner, bool withName) => new() {
            clientId = connection.ClientId, name = withName ? NameOf(connection) : "", stage = runner.checkpoint,
            finished = runner.finished, scoredTime = runner.scoredTime, reachedAt = runner.checkpointTime
        };

        /// <summary>Active racers ranked for the results: finishers by scored time, then the furthest checkpoint.</summary>
        List<KeyValuePair<NetworkConnection, Runner>> FinalOrder() {
            var order = runners.Where(r => r.Key.IsActive).ToList();
            order.Sort((a, b) => OB_Scoring.Compare(StandingOf(a.Key, a.Value, true), StandingOf(b.Key, b.Value, true)));
            return order;
        }

        // ---------------------------------------------------------------- messages
        OB_CourseBroadcast MakeCourse() => new() {
            seed = layout?.seed ?? 0, stageCount = layout?.StageCount ?? 0, round = round, coinBonus = coinTimeBonus,
            goldPerStage = layout != null ? medalTimes.Target(OB_Medal.Gold, layout.TotalPar) / layout.StageCount : 0f,
            starBonus = starTimeBonus, goldFactor = medalTimes.goldFactor, rulesHash = builtRulesHash
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
                personalBest = runner.personalBest, stars = runner.stars,
                tierTime = runner.closedTierTime, tierBest = runner.closedTierBest, tier = runner.closedTier
            });
        }

        void SendStandings() {
            standingsDirty = false;
            nextStandings = Time.unscaledTime + standingsInterval;
            if (layout == null) return;
            InstanceFinder.ServerManager.Broadcast(new OB_StandingsBroadcast {
                seed = layout.seed,
                racers = runners.Where(r => r.Key.IsActive).Select(r => StandingOf(r.Key, r.Value, false)).ToArray()
            });
        }

        void BroadcastRound() {
            if (layout == null) return;
            var leader = FinalOrder().FirstOrDefault();
            InstanceFinder.ServerManager.Broadcast(new OB_RoundBroadcast {
                seed = layout.seed, phase = phase, secondsLeft = secondsLeft, raceTime = RaceTime,
                leaderName = leader.Key != null ? NameOf(leader.Key) : "",
                leaderStage = leader.Value?.checkpoint ?? 0, finishedCount = finishCount,
                playerCount = PlayerData.Players.Count, headline = headline
            });
        }

        OB_ResultsBroadcast MakeResults(List<KeyValuePair<NetworkConnection, Runner>> final) => new() {
            seed = layout.seed,
            entries = final.Select(r => new OB_ResultEntry {
                name = NameOf(r.Key), finished = r.Value.finished, time = r.Value.finishTime,
                stage = r.Value.checkpoint, coins = OB_Scoring.CountCoins(r.Value.coins),
                scoredTime = r.Value.scoredTime, medal = r.Value.medal, clientId = r.Key.ClientId,
                stars = OB_Scoring.CountCoins(r.Value.stars)
            }).ToArray(),
            goldTime = medalTimes.Target(OB_Medal.Gold, layout.TotalPar),
            silverTime = medalTimes.Target(OB_Medal.Silver, layout.TotalPar),
            bronzeTime = medalTimes.Target(OB_Medal.Bronze, layout.TotalPar),
            coinBonus = coinTimeBonus, starBonus = starTimeBonus
        };

        void WriteLeaderboard(PlayerData player) {
            if (!runners.TryGetValue(player.Owner, out Runner runner)) return;
            // The same stage number as the client's stage card ("STAGE 4 / 30" while stage 4 is next).
            SetLeaderboardValue(player, StageColumn, runner.finished ? layout.StageCount : runner.checkpoint + 1);
            SetLeaderboardValue(player, CoinColumn, OB_Scoring.CountCoins(runner.coins));
        }

        static void SetLeaderboardValue(PlayerData player, string column, int value) {
            int index = SharedGlobalEvents.GetLeaderboardIndex(column);
            if (index >= 0 && index < player.leaderboard.Count && player.leaderboard[index] != value)
                player.leaderboard[index] = value;
        }

        /// <summary>The racer's display name; a connection without player data (already left) still gets one.</summary>
        static string NameOf(NetworkConnection connection) =>
            PlayerData.GetPlayerName(connection) is { Length: > 0 } name ? name : $"Racer {connection.ClientId}";

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
            retention?.Dispose();
            ServerPlayerCharacter.RespawnTime = previousRespawnTime;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnPosition) ServerPlayerCharacter.SpawnLocationFunction = null;
            if (InstanceFinder.ServerManager != null) {
                InstanceFinder.ServerManager.UnregisterBroadcast<OB_CourseRequest>(OnCourseRequest);
                InstanceFinder.ServerManager.UnregisterBroadcast<OB_ViewClock>(OnViewClock);
            }
            base.OnDestroy();
        }
    }
}
