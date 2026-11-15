#if UNITY_SERVER
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    public sealed partial class DI_ServerRunner : ServerRunner {
        const int BaseCount = 32;
        const int StartingTroops = 12;
        const int NeutralTroops = 5;
        const int NpcTeamCount = 4;
        const int NpcBasesPerTeam = 1;
        const int NpcStartingTroops = 14;
        const int NpcTeamIdStart = 100;
        const int WinnerXPReward = 100;
        const int WinnerGoldReward = 50;
        const float ArenaHalfWidth = 100f;
        const float ArenaHalfHeight = 74f;
        // Keep complete base/dot visuals inside the border, not just their
        // center points. Dots interpolate between bases, so this also bounds
        // every route inside the same inset rectangle.
        const float ArenaEdgeClearance = 5f;
        const float MinimumBaseSpacing = 12f;
        const float CollisionDistance = 0.75f;
        const float SnapshotInterval = 0.1f;
        const float SimulationStep = 1f / 60f;
        const float AttackedProductionDelay = 2f;
        const float NpcSlowThinkInterval = 2.25f;
        const float NpcFastThinkInterval = 0.55f;
        // Waves that land together beat the same troops arriving in dribbles, so a
        // capable team lets extra garrisons join an attack when their arrival falls
        // within this window of the leading wave.
        const float NpcCoordinationWindow = 2.5f;
        const int NpcMaxCoordinatedSources = 3;
        // Score bonus for continuing to press the objective chosen last think, so a
        // team keeps pressure on one base instead of splitting between two similar
        // ones and losing both.
        const float NpcFocusBonus = 18f;
        // How far under the projected defence a committed wave has to fall before a
        // team writes the attack off. Without the slack a single trained defender
        // would cancel every wave the instant it appeared.
        const float NpcAbandonThreshold = 0.75f;
        const float MinimumTurretRange = 5f;
        const int NpcTroopReserve = 3;
        const int NpcFrontlineReserve = 7;
        const int NpcSuperBaseReserve = 18;
        const int NpcSpeedBaseReserve = 10;
        // Exposed legs cost up to this many times their length when a fully
        // intelligent team plans a route, so smart NPCs walk around turrets.
        const float NpcTurretAvoidance = 2.5f;
        // Speed base configuration
        const string BasesColumn = "Bases";
        const string TroopsColumn = "Troops";
        const int SpeedBaseCount = 4; // Number of speed bases on the map

        [SerializeField, Min(30)] int matchDurationSeconds = 300;
        [SerializeField, Range(1, 2)] int turretBaseCount = 2;
        [SerializeField, Range(3, 8)] int nearbyPathsPerBase = 6;
        [SerializeField, Min(12f)] float maximumLocalPathLength = 65f;
        [SerializeField, Range(0.1f, 1f)] float npcSuperLaunchCapacityFraction = 0.85f;
        // Pause after a match ends before the base class resets and starts the next round.
        [SerializeField, Range(3, 60)] int intermissionSeconds = 8;

        sealed class BaseState {
            public Vector2 position;
            public int ownerClientId = -1;
            public int teamId = -1;
            public int troops = NeutralTroops;
            public int pendingTarget = -1;
            public int pendingTroops;
            public float actionTimer;
            public float sendInterval = DI_Rules.DefaultSendInterval;
            public float productionDelay;
            public bool isTurret;
            public bool isSuperProducer;
            public bool isSpeedBase;
            public float uninterruptedSeconds;
            public float turretCooldown;
            public int shotSequence;
            public Vector2 shotPosition;
            public int[] pendingRoute;
            public float damageTaken;
        }

        sealed class DotState {
            public int id;
            public int originBaseId;
            public int sourceBaseId;
            public int targetBaseId;
            public int ownerClientId;
            public int teamId;
            public float progress;
            public int[] route;
            public int routeIndex;
            public float health = 1f;
        }

        sealed class NpcTeamState {
            public int ownerClientId;
            public int teamId;
            public float thinkTimer;
            // The objective this team is currently pressing. Re-deciding from
            // scratch every think made a team alternate between two comparable
            // targets and reinforce neither.
            public int focusTargetBaseId = -1;
        }

        readonly List<BaseState> bases = new();
        readonly List<Vector2Int> links = new();
        readonly List<DotState> dots = new();
        readonly List<NpcTeamState> npcTeams = new();
        readonly Dictionary<int, int> playerTeams = new();
        readonly HashSet<int> dotInvadersTeams = new();
        readonly HashSet<int> announcedEliminations = new();
        readonly Dictionary<int, float> playerDamageMultipliers = new();
        readonly Dictionary<int, float> playerProductionMultipliers = new();
        readonly List<Vector2> previousDotPositions = new();
        readonly HashSet<int> destroyedDots = new();

        float snapshotTimer;
        float simulationAccumulator;
        int nextTeamId;
        int nextDotId;
        int revision;
        int secondsRemaining;
        int winningTeamId = -1;
        bool initialized;
        bool matchInProgress;
        float npcIntelligence = DI_Rules.DefaultNPCIntelligence;
        float turretRange = DI_Rules.DefaultTurretRange;
        float turretFireRate = DI_Rules.DefaultTurretFireRate;
        int maxCapacity = DI_Rules.DefaultMaxCapacity;
        float moveSpeedMultiplier = 1f;
        float sendIntervalMultiplier = 1f;
        float speedBaseBonus = DI_Rules.DefaultSpeedBaseBonus;
        float productionSpeed = DI_Rules.DefaultProductionSpeed;
        // Both are tuned as multipliers of the authored default, which stays the source of truth.
        float moveSpeed => DI_Rules.DefaultMoveSpeed * moveSpeedMultiplier;
        float sendInterval => DI_Rules.DefaultSendInterval * sendIntervalMultiplier;
        float npcAggression = DI_Rules.DefaultNpcAggression;
        float superProductionSpeed = DI_Rules.DefaultSuperProductionSpeed;
        float superSpeedupSeconds = DI_Rules.DefaultSuperSpeedupSeconds;
        int superMaxCapacity = DI_Rules.DefaultSuperMaxCapacity;
        readonly Dictionary<int, int> teamSpeedBases = new();

        protected override void Awake() {
            base.Awake();
            ServerPlayerCharacter.CanSpawnFunction = _ => false;
            InstanceFinder.ServerManager.RegisterBroadcast<DI_SendRequest>(OnSendRequest, true);
            PlayerData.OnPlayerRemoved += OnPlayerRemoved;
            DI_Commands.Register(this);
        }

        protected override async UniTask StartAsync(System.Threading.CancellationToken token) {
            await base.StartAsync(token);
            // Reset clears the leaderboard each round, so columns are re-enabled here.
            SetLeaderboardEnabled(BasesColumn, true);
            SetLeaderboardEnabled(TroopsColumn, true);
            InitializeMatch();

            while (matchInProgress && secondsRemaining > 0) {
                UpdateTopMessage();
                await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
                if (matchInProgress)
                    secondsRemaining--;
            }

            if (matchInProgress)
                CompleteMatch(DetermineLeadingTeam());

            BroadcastState();
            // Intermission: the base class restarts the round (waiting for players) once this returns.
            for (int remaining = intermissionSeconds; remaining > 0; remaining--) {
                SetTopMessage($"{TopMessageText()} Next match in {remaining}s");
                await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: token);
            }
        }

        protected override void OnPlayerAdded(PlayerData playerData) {
            base.OnPlayerAdded(playerData);
            playerData.cameraTypes.Add(GameCameraType.TwoDimCamera);
            AssignPlayer(playerData);
            PlayerProgress.Load<DI_SaveData>(playerData, DI_SaveData.Universe).Forget();
        }

        void Update() {
            if (!initialized || !matchInProgress || !InstanceFinder.IsServerStarted)
                return;

            float deltaTime = Time.unscaledDeltaTime;
            simulationAccumulator += deltaTime;
            // Bound catch-up work per frame, retaining time debt after a hitch.
            for (int step = 0; step < 15 && simulationAccumulator >= SimulationStep && matchInProgress; step++) {
                simulationAccumulator -= SimulationStep;
                RefreshTeamSpeedBases();
                UpdateNpcTeams(SimulationStep);
                UpdateBases(SimulationStep);
                UpdateDots(SimulationStep);
                CheckEliminatedTeams();
                CheckEndConditions();
            }

            snapshotTimer += deltaTime;
            if (snapshotTimer >= SnapshotInterval) {
                snapshotTimer %= SnapshotInterval;
                BroadcastState();
            }
        }



        protected override void Reset() {
            initialized = false;
            matchInProgress = false;
            bases.Clear();
            links.Clear();
            dots.Clear();
            npcTeams.Clear();
            playerTeams.Clear();
            playerDamageMultipliers.Clear();
            playerProductionMultipliers.Clear();
            dotInvadersTeams.Clear();
            announcedEliminations.Clear();
            base.Reset();
        }

        protected override void Stop() {
            matchInProgress = false;
            base.Stop();
        }

        protected override void OnDestroy() {
            DI_Commands.Unregister();
            if (InstanceFinder.ServerManager != null)
                InstanceFinder.ServerManager.UnregisterBroadcast<DI_SendRequest>(OnSendRequest);
            PlayerData.OnPlayerRemoved -= OnPlayerRemoved;
            base.OnDestroy();
        }
    }
}
#endif
