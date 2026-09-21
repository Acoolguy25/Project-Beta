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
    public sealed class DI_ServerRunner : ServerRunner {
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
        const int SpeedBaseCount = 4; // Number of speed bases on the map

        [SerializeField, Min(30)] int matchDurationSeconds = 300;
        [SerializeField, Range(1, 2)] int turretBaseCount = 2;
        [SerializeField, Range(3, 8)] int nearbyPathsPerBase = 6;
        [SerializeField, Min(12f)] float maximumLocalPathLength = 65f;
        [SerializeField, Range(0.1f, 1f)] float npcSuperLaunchCapacityFraction = 0.85f;

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
            UpdateTopMessage();
            await UniTask.Delay(TimeSpan.FromSeconds(8), cancellationToken: token);
        }

        protected override void OnPlayerAdded(PlayerData playerData) {
            base.OnPlayerAdded(playerData);
            playerData.cameraTypes.Add(GameCameraType.TwoDimCamera);
            AssignPlayer(playerData);
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

        void InitializeMatch() {
            // Match bonuses come only from the newly generated bases.
            teamSpeedBases.Clear();
            playerDamageMultipliers.Clear();
            playerProductionMultipliers.Clear();
            simulationAccumulator = 0f;
            snapshotTimer = 0f;
            bases.Clear();
            links.Clear();
            dots.Clear();
            npcTeams.Clear();
            playerTeams.Clear();
            dotInvadersTeams.Clear();
            announcedEliminations.Clear();
            nextTeamId = 0;
            nextDotId = 0;
            revision = 0;
            winningTeamId = -1;
            secondsRemaining = matchDurationSeconds;
            matchInProgress = false;

            GenerateBases();
            GenerateLinks();
            AssignTurrets();
            AssignSuperBases();
            AssignSpeedBases();
            initialized = true;

            foreach (PlayerData player in PlayerData.Players.Values)
                AssignPlayer(player);

            SpawnNpcTeams();
            matchInProgress = true;
            UpdateTopMessage();
            BroadcastState();
        }

        void GenerateBases() {
            var random = new System.Random(unchecked(Environment.TickCount * 397 ^ DateTime.UtcNow.Ticks.GetHashCode()));
            int attempts = 0;

            while (bases.Count < BaseCount && attempts++ < 5000) {
                var candidate = new Vector2(
                    Mathf.Lerp(-ArenaHalfWidth + ArenaEdgeClearance, ArenaHalfWidth - ArenaEdgeClearance,
                        (float)random.NextDouble()),
                    Mathf.Lerp(-ArenaHalfHeight + ArenaEdgeClearance, ArenaHalfHeight - ArenaEdgeClearance,
                        (float)random.NextDouble()));

                bool overlaps = false;
                for (int i = 0; i < bases.Count; i++) {
                    if (Vector2.Distance(candidate, bases[i].position) < MinimumBaseSpacing) {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                    bases.Add(new BaseState { position = candidate });
            }

            // Extremely unlikely fallback that still guarantees a playable board.
            for (int i = bases.Count; i < BaseCount; i++) {
                float angle = i * Mathf.PI * 2f / BaseCount;
                bases.Add(new BaseState {
                    position = new Vector2(Mathf.Cos(angle) * 82f, Mathf.Sin(angle) * 46f)
                });
            }
        }

        void GenerateLinks() {
            var edgeKeys = new HashSet<ulong>();

            // Prim's minimum spanning tree keeps the board connected without the
            // long, crossing roads introduced by random base creation order.
            var connected = new HashSet<int> { 0 };
            while (connected.Count < bases.Count) {
                int source = -1, target = -1;
                float shortest = float.MaxValue;
                foreach (int i in connected) {
                    for (int j = 0; j < bases.Count; j++) {
                        if (connected.Contains(j))
                            continue;
                        float distance = (bases[i].position - bases[j].position).sqrMagnitude;
                        if (distance < shortest) {
                            shortest = distance;
                            source = i;
                            target = j;
                        }
                    }
                }
                if (target < 0)
                    break;
                AddLink(source, target, edgeKeys);
                connected.Add(target);
            }

            // Dense local alternatives; the spanning tree still connects any isolated
            // clusters. Crossings are allowed, but roads cannot pass through a base.
            for (int i = 0; i < bases.Count; i++) {
                var neighbors = new List<int>();
                for (int j = 0; j < bases.Count; j++) {
                    if (i != j)
                        neighbors.Add(j);
                }
                neighbors.Sort((a, b) =>
                    (bases[i].position - bases[a].position).sqrMagnitude.CompareTo(
                        (bases[i].position - bases[b].position).sqrMagnitude));

                for (int j = 0; j < Mathf.Min(nearbyPathsPerBase, neighbors.Count); j++) {
                    int neighbor = neighbors[j];
                    if (Vector2.Distance(bases[i].position, bases[neighbor].position) <= maximumLocalPathLength &&
                        !PassesThroughBase(i, neighbor))
                        AddLink(i, neighbor, edgeKeys);
                }
            }
        }

        bool PassesThroughBase(int a, int b) {
            Vector2 start = bases[a].position, end = bases[b].position;
            Vector2 direction = end - start;
            for (int i = 0; i < bases.Count; i++) {
                if (i == a || i == b)
                    continue;
                float along = Mathf.Clamp01(Vector2.Dot(bases[i].position - start, direction) / Mathf.Max(0.001f, direction.sqrMagnitude));
                if ((bases[i].position - (start + direction * along)).sqrMagnitude < 4.5f * 4.5f)
                    return true;
            }
            return false;
        }

        void AssignTurrets() {
            // Two separated objectives near the middle of each half of the board.
            int count = Mathf.Clamp(turretBaseCount, 1, 2);
            for (int t = 0; t < count; t++) {
                Vector2 objective = new Vector2(count == 1 ? 0f : (t == 0 ? -35f : 35f), 0f);
                int best = -1;
                float distance = float.MaxValue;
                for (int i = 0; i < bases.Count; i++) {
                    float candidate = (bases[i].position - objective).sqrMagnitude;
                    if (!bases[i].isTurret && candidate < distance) {
                        best = i;
                        distance = candidate;
                    }
                }
                if (best >= 0)
                    bases[best].isTurret = true;
            }
        }

        void AssignSuperBases() {
            for (int t = 0; t < 2; t++) {
                Vector2 objective = new Vector2(0f, t == 0 ? -35f : 35f);
                int best = -1;
                float distance = float.MaxValue;
                for (int i = 0; i < bases.Count; i++) {
                    float candidate = (bases[i].position - objective).sqrMagnitude;
                    if (!bases[i].isTurret && !bases[i].isSuperProducer && !bases[i].isSpeedBase && candidate < distance) {
                        best = i;
                        distance = candidate;
                    }
                }
                if (best >= 0)
                    bases[best].isSuperProducer = true;
            }
        }

        void AssignSpeedBases() {
            // Distribute speed bases across the map
            for (int i = 0; i < SpeedBaseCount; i++) {
                float xPos = 0f;
                if (SpeedBaseCount > 1) {
                    xPos = Mathf.Lerp(-ArenaHalfWidth + 20f, ArenaHalfWidth - 20f, (float)i / (SpeedBaseCount - 1));
                }
                Vector2 objective = new Vector2(xPos, 0f);
                int best = -1;
                float distance = float.MaxValue;
                for (int b = 0; b < bases.Count; b++) {
                    // Skip bases that are already turrets, super producers, or other speed bases
                    if (!bases[b].isTurret && !bases[b].isSuperProducer && !bases[b].isSpeedBase) {
                        float candidate = (bases[b].position - objective).sqrMagnitude;
                        if (candidate < distance) {
                            best = b;
                            distance = candidate;
                        }
                    }
                }
                if (best >= 0)
                    bases[best].isSpeedBase = true;
            }
        }

        int Capacity(BaseState state) => state.isSuperProducer ? superMaxCapacity : maxCapacity;

        float ProductionRate(BaseState state) => state.isSuperProducer
            ? DI_Rules.SuperProductionRate(state.uninterruptedSeconds, superProductionSpeed, superSpeedupSeconds)
            : productionSpeed;

        static void InterruptProduction(BaseState state) {
            state.uninterruptedSeconds = 0f;
            state.actionTimer = 0f;
        }

        void AddLink(int a, int b, HashSet<ulong> edgeKeys) {
            int source = Mathf.Min(a, b);
            int target = Mathf.Max(a, b);
            ulong key = ((ulong)(uint)source << 32) | (uint)target;
            if (edgeKeys.Add(key))
                links.Add(new Vector2Int(source, target));
        }

        void AssignPlayer(PlayerData player) {
            if (!initialized || player == null || !player.Owner.IsValid)
                return;

            int clientId = player.Owner.ClientId;
            if (!playerTeams.TryGetValue(clientId, out int teamId)) {
                teamId = nextTeamId++;
                playerTeams.Add(clientId, teamId);
            }
            dotInvadersTeams.Add(teamId);

            player.SetPlayerTeam(new TeamConfig(DI_Rules.GetTeamColor(teamId)));

            for (int i = 0; i < bases.Count; i++) {
                if (bases[i].ownerClientId == clientId)
                    return;
            }

            int baseId = FindSpawnBase();
            if (baseId < 0)
                return;

            BaseState home = bases[baseId];
            home.ownerClientId = clientId;
            home.teamId = teamId;
            home.troops = StartingTroops;
            home.damageTaken = 0f;
            home.pendingTarget = -1;
            home.pendingTroops = 0;
            home.pendingRoute = null;
            home.actionTimer = 0f;
            home.productionDelay = 0f;
            home.uninterruptedSeconds = 0f;
            if (matchInProgress)
                BroadcastState();
        }

        void SpawnNpcTeams() {
            for (int teamIndex = 0; teamIndex < NpcTeamCount; teamIndex++) {
                var npcTeam = new NpcTeamState {
                    ownerClientId = -2 - teamIndex,
                    teamId = NpcTeamIdStart + teamIndex,
                    thinkTimer = teamIndex * 0.35f
                };
                npcTeams.Add(npcTeam);
                dotInvadersTeams.Add(npcTeam.teamId);

                for (int baseIndex = 0; baseIndex < NpcBasesPerTeam; baseIndex++) {
                    int spawnBase = FindSpawnBase();
                    if (spawnBase < 0)
                        break;

                    BaseState state = bases[spawnBase];
                    state.ownerClientId = npcTeam.ownerClientId;
                    state.teamId = npcTeam.teamId;
                    state.troops = NpcStartingTroops;
                    state.pendingTarget = -1;
                    state.pendingTroops = 0;
                    state.pendingRoute = null;
                    state.actionTimer = 0f;
                    state.productionDelay = 0f;
                    state.uninterruptedSeconds = 0f;
                }
            }
        }

        int FindSpawnBase() {
            int best = -1;
            float bestDistance = float.MinValue;

            for (int i = 0; i < bases.Count; i++) {
                if (bases[i].teamId >= 0 || bases[i].isTurret || bases[i].isSuperProducer || bases[i].isSpeedBase)
                    continue;

                float nearestOwnedDistance = float.MaxValue;
                for (int j = 0; j < bases.Count; j++) {
                    if (bases[j].teamId >= 0) {
                        nearestOwnedDistance = Mathf.Min(nearestOwnedDistance,
                            (bases[i].position - bases[j].position).sqrMagnitude);
                    }
                }

                if (nearestOwnedDistance > bestDistance) {
                    bestDistance = nearestOwnedDistance;
                    best = i;
                }
            }

            return best;
        }

        void OnPlayerRemoved(PlayerData player) {
            if (player == null)
                return;

            int clientId = player.Owner.ClientId;
            playerTeams.Remove(clientId);
            playerDamageMultipliers.Remove(clientId);
            playerProductionMultipliers.Remove(clientId);

            for (int i = 0; i < bases.Count; i++) {
                BaseState state = bases[i];
                if (state.ownerClientId != clientId)
                    continue;

                state.ownerClientId = -1;
                state.teamId = -1;
                state.troops = NeutralTroops;
                state.damageTaken = 0f;
                state.pendingTarget = -1;
                state.pendingTroops = 0;
                state.pendingRoute = null;
                state.actionTimer = 0f;
                state.productionDelay = 0f;
                state.uninterruptedSeconds = 0f;
            }

            dots.RemoveAll(dot => dot.ownerClientId == clientId);
            CheckEliminatedTeams();
            BroadcastState();
            CheckEndConditions();
        }

        void OnSendRequest(NetworkConnection connection, DI_SendRequest request, FishNet.Transporting.Channel channel) {
            if (!initialized || !matchInProgress || !connection.IsValid ||
                request.sourceBaseId < 0 || request.sourceBaseId >= bases.Count)
                return;

            BaseState source = bases[request.sourceBaseId];
            if (source.ownerClientId != connection.ClientId)
                return;

            if (request.targetBaseId == -1) {
                CancelOrders(request.sourceBaseId, connection.ClientId);
                BroadcastState();
                return;
            }

            int[] route = request.route ?? new[] { request.sourceBaseId, request.targetBaseId };
            if (!IsValidRoute(request.sourceBaseId, request.targetBaseId, route))
                return;

            if (source.pendingTroops > 0) {
                source.pendingTarget = route[1];
                source.pendingRoute = (int[])route.Clone();
                BroadcastState();
                return;
            }

            if (source.troops <= 0)
                return;

            QueueSend(source, route[1], source.troops, sendInterval);
            source.pendingRoute = (int[])route.Clone();
            BroadcastState();
        }

        bool IsValidRoute(int source, int target, int[] route) {
            if (route == null || route.Length < 2 || route.Length > bases.Count ||
                route[0] != source || route[route.Length - 1] != target)
                return false;
            var visited = new HashSet<int>();
            for (int i = 0; i < route.Length; i++) {
                if (route[i] < 0 || route[i] >= bases.Count || !visited.Add(route[i]) ||
                    i > 0 && !AreNeighbors(route[i - 1], route[i]))
                    return false;
            }
            return true;
        }

        void CancelOrders(int sourceBaseId, int ownerClientId) {
            ClearSend(bases[sourceBaseId]);
            // Already moving troops finish their current leg, then stop routing.
            foreach (DotState dot in dots)
                if (dot.ownerClientId == ownerClientId && dot.originBaseId == sourceBaseId)
                    dot.route = null;
        }

        bool AreNeighbors(int source, int target) {
            for (int i = 0; i < links.Count; i++) {
                Vector2Int link = links[i];
                if ((link.x == source && link.y == target) || (link.x == target && link.y == source))
                    return true;
            }
            return false;
        }

        void UpdateNpcTeams(float deltaTime) {
            for (int teamIndex = 0; teamIndex < npcTeams.Count; teamIndex++) {
                NpcTeamState npcTeam = npcTeams[teamIndex];
                npcTeam.thinkTimer += deltaTime;
                float intelligence = npcIntelligence / 100f;
                float thinkInterval = Mathf.Lerp(NpcSlowThinkInterval, NpcFastThinkInterval, intelligence);
                if (npcTeam.thinkTimer < thinkInterval)
                    continue;
                npcTeam.thinkTimer %= thinkInterval;
                float aggression = npcAggression / 100f;
                float teamSpeed = TeamMoveSpeed(npcTeam.teamId);

                // An objective that is already ours is no longer an objective.
                if (npcTeam.focusTargetBaseId >= bases.Count ||
                    npcTeam.focusTargetBaseId >= 0 && bases[npcTeam.focusTargetBaseId].teamId == npcTeam.teamId)
                    npcTeam.focusTargetBaseId = -1;

                if (TryProtectNpcBase(npcTeam.teamId, intelligence))
                    continue;

                // Recovering a doomed wave happens before planning, so the freed
                // garrison can be spent on a target it can still take this think.
                TryAbandonHopelessAttack(npcTeam.teamId, intelligence);

                int bestSource = -1;
                int bestTroopCount = 0;
                int[] bestRoute = null;
                float bestScore = float.MinValue;
                for (int sourceId = 0; sourceId < bases.Count; sourceId++) {
                    BaseState source = bases[sourceId];
                    if (source.teamId != npcTeam.teamId || source.pendingTroops > 0 || HasOutgoingTroops(sourceId) ||
                        source.troops <= NpcReserveForBase(sourceId, npcTeam.teamId))
                        continue;

                    int reserve = NpcReserveForBase(sourceId, npcTeam.teamId);
                    int available = source.troops - reserve;
                    // Let a charging super base build a meaningful wave unless a base is in danger.
                    if (ShouldChargeNpcBase(source))
                        continue;

                    for (int targetId = 0; targetId < bases.Count; targetId++) {
                        BaseState target = bases[targetId];
                        if (target.teamId == npcTeam.teamId)
                            continue;

                        int[] route = FindNpcRoute(sourceId, targetId, npcTeam.teamId, false, intelligence);
                        if (route.Length < 2)
                            continue;
                        float routeDistance = RouteDistance(route);
                        int losses = EstimateTurretLosses(route, npcTeam.teamId, available, sendInterval);
                        // Speed bases make this team's waves land sooner. Planning with
                        // the base speed over-estimates how much the defender can train.
                        float travelTime = routeDistance / teamSpeed;
                        // Estimate the actual ramp before first contact. Using peak
                        // speed here makes a charging super base seem untouchable.
                        int growth = EstimateDefenderGrowth(targetId, travelTime);
                        // The garrison on the board is only part of what a wave meets. A
                        // neighbour walking in, a rival wave landing first, and a hostile
                        // stream already on our road all change the real cost, and reading
                        // them is most of what separates a strong team from one that keeps
                        // sending waves which are always just slightly too small.
                        int reinforcements = ScaleForecast(
                            EstimateDefenderReinforcements(targetId, travelTime), intelligence * 0.5f);
                        int thirdParty = ScaleForecast(
                            EstimateThirdPartyDamage(targetId, npcTeam.teamId, travelTime), intelligence);
                        int interception = ScaleForecast(
                            EstimateInterceptionLosses(route, npcTeam.teamId), intelligence);
                        int defense = Mathf.Max(0, target.troops + growth + reinforcements - thirdParty);
                        int committed = CountIncomingTroops(targetId, npcTeam.teamId);
                        // A cautious team insists on a wider margin than an aggressive one.
                        int margin = Mathf.RoundToInt(Mathf.Lerp(6f, 1f, aggression));
                        if (committed >= defense + losses + interception + margin)
                            continue;
                        int required = Mathf.Max(1, defense + losses + interception + margin - committed);
                        if (available < required)
                            continue;

                        int sending = source.isSuperProducer ? available
                            : Mathf.Min(available, required + Mathf.Max(2, required / 5));
                        float objectivePriority = NpcTargetPriority(targetId, npcTeam.teamId);
                        // A garrison sitting at capacity has stopped producing, so
                        // spending it costs nothing and idling wastes the base.
                        float idleCapacity = source.troops >= Capacity(source) ? 40f : 0f;
                        // Finishing what the team started is worth more than a marginally
                        // better new idea every couple of seconds.
                        float focus = targetId == npcTeam.focusTargetBaseId ? NpcFocusBonus : 0f;
                        float strategicScore = objectivePriority + idleCapacity + focus + (sending - required) * 1.5f -
                            (losses + interception) * 5f - travelTime - (source.isSuperProducer ? 12f : 0f);
                        float score = Mathf.Lerp(UnityEngine.Random.Range(0f, 100f), strategicScore, intelligence);
                        if (score <= bestScore)
                            continue;

                        bestScore = score;
                        bestSource = sourceId;
                        bestTroopCount = sending;
                        bestRoute = route;
                    }
                }

                if (bestSource >= 0) {
                    BaseState source = bases[bestSource];
                    QueueSend(source, bestRoute[1], bestTroopCount, sendInterval);
                    source.pendingRoute = bestRoute;
                    npcTeam.focusTargetBaseId = bestRoute[bestRoute.Length - 1];
                    CommitSupportingWaves(npcTeam.teamId, bestSource, bestRoute, intelligence);
                } else
                    TryConsolidateNpcTroops(npcTeam.teamId, intelligence);
            }
        }

        bool ShouldChargeNpcBase(BaseState source) {
            // Sending resets the entire ramp. Wait until the configured production
            // cap is nearly full, then spend one wave instead of repeated trickles.
            return source.isSuperProducer && source.troops < Mathf.CeilToInt(
                Capacity(source) * Mathf.Clamp(npcSuperLaunchCapacityFraction, 0.1f, 1f));
        }

        bool TryProtectNpcBase(int teamId, float intelligence) {
            int threatenedBase = -1;
            float greatestUrgency = 0f;
            for (int i = 0; i < bases.Count; i++) {
                BaseState target = bases[i];
                if (target.teamId != teamId)
                    continue;
                int hostile = CountHostileIncomingTroops(i, teamId);
                if (hostile <= 0)
                    continue;
                int friendly = CountIncomingTroops(i, teamId);
                float urgency = hostile - friendly - Mathf.Max(0, target.troops - target.pendingTroops) +
                    (target.isSuperProducer ? 14f : target.isSpeedBase ? 10f : 4f);
                if (urgency > greatestUrgency) {
                    greatestUrgency = urgency;
                    threatenedBase = i;
                }
            }
            if (threatenedBase < 0)
                return false;

            BaseState threatened = bases[threatenedBase];
            if (threatened.pendingTroops > 0 && CountHostileIncomingTroops(threatenedBase, teamId) >
                threatened.troops - threatened.pendingTroops + CountIncomingTroops(threatenedBase, teamId)) {
                ClearSend(threatened);
                if (CountHostileIncomingTroops(threatenedBase, teamId) <=
                    threatened.troops + CountIncomingTroops(threatenedBase, teamId))
                    return true;
            }

            int bestSource = -1;
            int[] bestRoute = null;
            float bestScore = float.MinValue;
            int needed = Mathf.Max(1, CountHostileIncomingTroops(threatenedBase, teamId) -
                CountIncomingTroops(threatenedBase, teamId) - bases[threatenedBase].troops + NpcFrontlineReserve);
            for (int i = 0; i < bases.Count; i++) {
                BaseState source = bases[i];
                if (i == threatenedBase || source.teamId != teamId || source.pendingTroops > 0 || HasOutgoingTroops(i))
                    continue;
                int available = source.troops - NpcReserveForBase(i, teamId);
                if (available <= 0)
                    continue;
                int[] route = FindNpcRoute(i, threatenedBase, teamId, true, intelligence);
                if (route.Length < 2)
                    continue;
                float score = Mathf.Min(available, needed) * 5f - RouteDistance(route) -
                    (source.isSuperProducer ? 10f : 0f);
                if (score > bestScore) {
                    bestScore = score;
                    bestSource = i;
                    bestRoute = route;
                }
            }
            if (bestSource < 0)
                return false;

            BaseState reinforcement = bases[bestSource];
            int troopCount = Mathf.Min(needed, reinforcement.troops - NpcReserveForBase(bestSource, teamId));
            QueueSend(reinforcement, bestRoute[1], troopCount, sendInterval);
            reinforcement.pendingRoute = bestRoute;
            return true;
        }

        int NpcReserveForBase(int baseId, int teamId) {
            BaseState state = bases[baseId];
            int reserve = state.isSuperProducer ? NpcSuperBaseReserve
                : state.isSpeedBase ? NpcSpeedBaseReserve
                : NpcTroopReserve;
            foreach (Vector2Int link in links) {
                int neighbor = link.x == baseId ? link.y : link.y == baseId ? link.x : -1;
                if (neighbor >= 0 && bases[neighbor].teamId != teamId) {
                    reserve = Mathf.Max(reserve, NpcFrontlineReserve);
                    break;
                }
            }
            // An aggressive team keeps less at home and commits the difference.
            reserve = Mathf.CeilToInt(reserve * Mathf.Lerp(1.5f, 0.6f, npcAggression / 100f));
            // An enemy stack next door is a threat before it ever launches. Reading it
            // is what stops a team being counter-attacked out of the base it just spent
            // its whole army taking.
            int adjacentThreat = 0;
            foreach (Vector2Int link in links) {
                int neighbor = link.x == baseId ? link.y : link.y == baseId ? link.x : -1;
                if (neighbor >= 0 && bases[neighbor].teamId >= 0 && bases[neighbor].teamId != teamId)
                    adjacentThreat = Mathf.Max(adjacentThreat, bases[neighbor].troops);
            }
            return Mathf.Min(reserve, Mathf.Max(0, Capacity(state) - 1)) +
                Mathf.Min(10, CountHostileIncomingTroops(baseId, teamId)) +
                Mathf.Min(12, ScaleForecast(adjacentThreat, 0.4f * (npcIntelligence / 100f)));
        }

        float NpcTargetPriority(int targetId, int teamId) {
            BaseState target = bases[targetId];
            float priority = target.teamId < 0 ? 24f : target.ownerClientId >= 0 ? 82f : 58f;
            if (target.isTurret)
                priority += 55f;
            if (target.isSuperProducer)
                priority += target.teamId < 0 ? 105f : 85f;
            if (target.isSpeedBase) {
                // Each speed base multiplies this team's entire army speed, so the
                // first one is worth far more than a duplicate.
                priority += (target.teamId < 0 ? 70f : 56f) / (1 + TeamSpeedBaseCount(teamId));
            }
            foreach (Vector2Int link in links) {
                int neighbor = link.x == targetId ? link.y : link.y == targetId ? link.x : -1;
                if (neighbor >= 0 && bases[neighbor].teamId == teamId && bases[neighbor].isSuperProducer) {
                    priority += 35f;
                    break;
                }
            }
            return priority;
        }

        int EstimateDefenderGrowth(int targetId, float travelTime) {
            BaseState target = bases[targetId];
            if (target.teamId < 0 || target.isTurret || target.pendingTroops > 0 || HasOutgoingTroops(targetId))
                return 0;
            float productiveTime = Mathf.Max(0f, travelTime - target.productionDelay);
            float production = target.isSuperProducer
                ? DI_Rules.SuperProductionOverInterval(target.uninterruptedSeconds, productiveTime,
                    superProductionSpeed, superSpeedupSeconds)
                : productiveTime * productionSpeed;
            return Mathf.Min(Mathf.Max(0, Capacity(target) - target.troops), Mathf.CeilToInt(production));
        }

        // A forecast is only as trustworthy as the team reading it. Confidence scales
        // how much of a projected figure a team plans around, so a poor team still
        // plans off little more than what is plainly on the board.
        static int ScaleForecast(int amount, float confidence) =>
            amount <= 0 ? 0 : Mathf.RoundToInt(amount * Mathf.Clamp01(confidence));

        /// <summary>
        /// Troops the defender can walk in from an adjacent base before a wave that is
        /// <paramref name="travelTime"/> seconds out arrives. No garrison empties itself
        /// to reinforce a neighbour, so a small reserve is always left behind.
        /// </summary>
        int EstimateDefenderReinforcements(int targetId, float travelTime) {
            BaseState target = bases[targetId];
            if (target.teamId < 0)
                return 0;
            float defenderSpeed = TeamMoveSpeed(target.teamId);
            int reinforcements = 0;
            foreach (Vector2Int link in links) {
                int neighbor = link.x == targetId ? link.y : link.y == targetId ? link.x : -1;
                if (neighbor < 0 || bases[neighbor].teamId != target.teamId)
                    continue;
                float legTime = Vector2.Distance(bases[neighbor].position, target.position) / defenderSpeed;
                if (legTime >= travelTime)
                    continue;
                reinforcements += Mathf.Max(0, bases[neighbor].troops - NpcTroopReserve);
            }
            return reinforcements;
        }

        /// <summary>
        /// Damage a rival team's wave will already have done to this target before ours
        /// lands. Spotting it is what lets a team take a contested base cheaply instead
        /// of queueing behind someone else's fight.
        /// </summary>
        int EstimateThirdPartyDamage(int targetId, int teamId, float travelTime) {
            BaseState target = bases[targetId];
            int damage = 0;
            foreach (DotState dot in dots) {
                if (dot.teamId == teamId || dot.teamId == target.teamId)
                    continue;
                if (RouteDestination(dot.route, dot.routeIndex, dot.teamId, dot.targetBaseId) != targetId)
                    continue;
                if (EstimatedArrivalSeconds(dot) < travelTime)
                    damage++;
            }
            // Past emptying the garrison the rival takes the base itself, which is a
            // different fight; never plan on more than the defenders actually there.
            return Mathf.Min(damage, Mathf.Max(0, target.troops));
        }

        /// <summary>
        /// Hostile troops already coming the other way down a leg of this route. They
        /// trade one for one on contact, so a team that ignores them keeps feeding a
        /// stream into a larger one.
        /// </summary>
        int EstimateInterceptionLosses(int[] route, int teamId) {
            int losses = 0;
            for (int i = 1; i < route.Length; i++) {
                foreach (DotState dot in dots) {
                    if (dot.teamId == teamId)
                        continue;
                    if (dot.sourceBaseId == route[i] && dot.targetBaseId == route[i - 1])
                        losses++;
                }
            }
            return losses;
        }

        /// <summary>Seconds until this troop reaches the end of its route.</summary>
        float EstimatedArrivalSeconds(DotState dot) {
            float speed = TeamMoveSpeed(dot.teamId);
            float seconds = (1f - dot.progress) * Vector2.Distance(
                bases[dot.sourceBaseId].position, bases[dot.targetBaseId].position) / speed;
            if (dot.route == null)
                return seconds;
            for (int i = dot.routeIndex + 1; i < dot.route.Length; i++)
                seconds += Vector2.Distance(bases[dot.route[i - 1]].position, bases[dot.route[i]].position) / speed;
            return seconds;
        }

        /// <summary>
        /// Adds further idle garrisons to the attack this team just launched, keeping only
        /// the ones whose wave lands close enough to the leading one to fight alongside it.
        /// Attacking with one base at a time is the single biggest reason a team holding a
        /// large army loses to a smaller defender that trains between each separate wave.
        /// </summary>
        void CommitSupportingWaves(int teamId, int primarySource, int[] primaryRoute, float intelligence) {
            // Timing several bases onto one target is a deliberate plan; a poor team
            // still attacks with whichever single base it happened to pick.
            if (intelligence < 0.5f)
                return;

            int targetId = primaryRoute[primaryRoute.Length - 1];
            float teamSpeed = TeamMoveSpeed(teamId);
            float leadArrival = RouteDistance(primaryRoute) / teamSpeed;
            float window = NpcCoordinationWindow * Mathf.Lerp(0.4f, 1f, intelligence);
            int committedSources = 0;

            for (int sourceId = 0; sourceId < bases.Count && committedSources < NpcMaxCoordinatedSources; sourceId++) {
                if (sourceId == primarySource)
                    continue;
                BaseState source = bases[sourceId];
                if (source.teamId != teamId || source.pendingTroops > 0 || HasOutgoingTroops(sourceId) ||
                    ShouldChargeNpcBase(source))
                    continue;

                int available = source.troops - NpcReserveForBase(sourceId, teamId);
                if (available <= 0)
                    continue;

                int[] route = FindNpcRoute(sourceId, targetId, teamId, false, intelligence);
                if (route.Length < 2)
                    continue;
                if (Mathf.Abs(RouteDistance(route) / teamSpeed - leadArrival) > window)
                    continue;
                if (EstimateTurretLosses(route, teamId, available, sendInterval) >= available)
                    continue;

                QueueSend(source, route[1], available, sendInterval);
                source.pendingRoute = route;
                committedSources++;
            }
        }

        /// <summary>
        /// Pulls the plug on a wave that can no longer take what it was sent for, so the
        /// rest of the garrison is spent somewhere it can still win instead of trickling
        /// into a base that has already outgrown it.
        /// </summary>
        bool TryAbandonHopelessAttack(int teamId, float intelligence) {
            // Reading a fight as already lost is a judgement a poor team does not make.
            if (intelligence < 0.65f)
                return false;

            bool abandoned = false;
            for (int i = 0; i < bases.Count; i++) {
                BaseState source = bases[i];
                if (source.teamId != teamId || source.pendingTroops <= 0)
                    continue;

                int targetId = RouteDestination(source.pendingRoute, 1, teamId, source.pendingTarget);
                if (targetId < 0 || targetId >= bases.Count || bases[targetId].teamId == teamId)
                    continue;

                float travelTime = (source.pendingRoute != null
                    ? RouteDistance(source.pendingRoute)
                    : Vector2.Distance(source.position, bases[targetId].position)) / TeamMoveSpeed(teamId);
                int defense = bases[targetId].troops + EstimateDefenderGrowth(targetId, travelTime) +
                    ScaleForecast(EstimateDefenderReinforcements(targetId, travelTime), intelligence * 0.5f);
                if (CountIncomingTroops(targetId, teamId) >= Mathf.CeilToInt(defense * NpcAbandonThreshold))
                    continue;

                ClearSend(source);
                abandoned = true;
            }
            return abandoned;
        }

        int CountIncomingTroops(int targetId, int teamId) {
            int count = 0;
            foreach (DotState dot in dots) {
                int destination = RouteDestination(dot.route, dot.routeIndex, dot.teamId, dot.targetBaseId);
                if (dot.teamId == teamId && destination == targetId)
                    count++;
            }
            foreach (BaseState source in bases) {
                int destination = RouteDestination(source.pendingRoute, 1, source.teamId, source.pendingTarget);
                if (source.teamId == teamId && source.pendingTroops > 0 && destination == targetId)
                    count += source.pendingTroops;
            }
            return count;
        }

        int CountHostileIncomingTroops(int targetId, int teamId) {
            int count = 0;
            foreach (DotState dot in dots) {
                int destination = RouteDestination(dot.route, dot.routeIndex, dot.teamId, dot.targetBaseId);
                if (dot.teamId != teamId && destination == targetId)
                    count++;
            }
            foreach (BaseState source in bases) {
                int destination = RouteDestination(source.pendingRoute, 1, source.teamId, source.pendingTarget);
                if (source.teamId >= 0 && source.teamId != teamId && source.pendingTroops > 0 && destination == targetId)
                    count += source.pendingTroops;
            }
            return count;
        }

        int RouteDestination(int[] route, int routeIndex, int teamId, int fallback) {
            if (route == null || route.Length == 0)
                return fallback;
            for (int i = routeIndex; i < route.Length; i++)
                if (bases[route[i]].teamId != teamId)
                    return route[i];
            return route[route.Length - 1];
        }

        int[] FindNpcRoute(int sourceId, int targetId, int teamId, bool friendlyTarget, float intelligence) {
            int count = bases.Count;
            var distances = new float[count];
            var previous = new int[count];
            var visited = new bool[count];
            for (int i = 0; i < count; i++) { distances[i] = float.PositiveInfinity; previous[i] = -1; }
            distances[sourceId] = 0f;
            for (int step = 0; step < count; step++) {
                int current = -1;
                for (int i = 0; i < count; i++)
                    if (!visited[i] && (current < 0 || distances[i] < distances[current])) current = i;
                if (current < 0 || float.IsPositiveInfinity(distances[current])) break;
                if (current == targetId) break;
                visited[current] = true;
                foreach (Vector2Int link in links) {
                    int neighbor = link.x == current ? link.y : link.y == current ? link.x : -1;
                    if (neighbor < 0 || visited[neighbor]) continue;
                    bool canEnter = neighbor == targetId
                        ? (friendlyTarget ? bases[neighbor].teamId == teamId : bases[neighbor].teamId != teamId)
                        : bases[neighbor].teamId == teamId;
                    if (!canEnter) continue;
                    // Cost the leg in distance plus the distance spent under fire,
                    // so a smarter team detours around hostile turret coverage.
                    float exposure = TurretExposureSeconds(current, neighbor, teamId) * TeamMoveSpeed(teamId) *
                        Mathf.Lerp(0f, NpcTurretAvoidance, intelligence);
                    float distance = distances[current] + exposure +
                        Vector2.Distance(bases[current].position, bases[neighbor].position);
                    if (distance < distances[neighbor]) { distances[neighbor] = distance; previous[neighbor] = current; }
                }
            }
            if (float.IsPositiveInfinity(distances[targetId])) return Array.Empty<int>();
            var route = new List<int>();
            for (int at = targetId; at >= 0; at = previous[at]) route.Add(at);
            route.Reverse();
            return route.ToArray();
        }

        float RouteDistance(int[] route) {
            float distance = 0f;
            for (int i = 1; i < route.Length; i++)
                distance += Vector2.Distance(bases[route[i - 1]].position, bases[route[i]].position);
            return distance;
        }

        int EstimateTurretLosses(int[] route, int teamId, int troopCount, float dispatchInterval) {
            int losses = 0;
            for (int i = 1; i < route.Length && losses < troopCount; i++)
                losses += EstimateTurretLosses(route[i - 1], route[i], teamId, troopCount - losses, dispatchInterval);
            return Mathf.Min(troopCount, losses);
        }

        int EstimateTurretLosses(int sourceId, int targetId, int teamId, int troopCount, float dispatchInterval) {
            if (troopCount <= 0)
                return 0;
            int losses = 0;
            foreach (BaseState turret in bases) {
                float covered = TurretCoverageSeconds(turret, sourceId, targetId, teamId);
                if (covered <= 0f)
                    continue;
                // A stream remains exposed between its first and last troop.
                // Assume every hostile/neutral turret can concentrate on this wave.
                float exposure = covered + (troopCount - 1) * dispatchInterval;
                losses += 1 + Mathf.FloorToInt(exposure / turretFireRate);
            }
            return Mathf.Min(troopCount, losses);
        }

        /// <summary>Total seconds one troop of <paramref name="teamId"/> spends under hostile turret fire on this leg.</summary>
        float TurretExposureSeconds(int sourceId, int targetId, int teamId) {
            float seconds = 0f;
            foreach (BaseState turret in bases)
                seconds += TurretCoverageSeconds(turret, sourceId, targetId, teamId);
            return seconds;
        }

        // Seconds a single troop spends inside one hostile turret's radius while
        // crossing this leg at the team's real speed. Zero for friendly turrets.
        float TurretCoverageSeconds(BaseState turret, int sourceId, int targetId, int teamId) {
            if (!turret.isTurret || turret.teamId >= 0 && turret.teamId == teamId)
                return 0f;
            Vector2 start = bases[sourceId].position;
            Vector2 delta = bases[targetId].position - start;
            float length = delta.magnitude;
            if (length < 0.001f)
                return 0f;
            Vector2 direction = delta / length;
            Vector2 offset = turret.position - start;
            float center = Vector2.Dot(offset, direction);
            float perpendicularSquared = Mathf.Max(0f, offset.sqrMagnitude - center * center);
            float radiusSquared = turretRange * turretRange;
            if (perpendicularSquared >= radiusSquared)
                return 0f;
            float halfChord = Mathf.Sqrt(radiusSquared - perpendicularSquared);
            float enter = Mathf.Max(0f, center - halfChord);
            float leave = Mathf.Min(length, center + halfChord);
            return leave <= enter ? 0f : (leave - enter) / TeamMoveSpeed(teamId);
        }

        void TryConsolidateNpcTroops(int teamId, float intelligence) {
            int bestSource = -1, bestTarget = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < bases.Count; i++) {
                BaseState source = bases[i];
                if (source.teamId != teamId || source.pendingTroops > 0 || HasOutgoingTroops(i) ||
                    source.isSuperProducer || source.troops <= NpcTroopReserve + 2)
                    continue;
                foreach (Vector2Int link in links) {
                    int targetId = link.x == i ? link.y : link.y == i ? link.x : -1;
                    if (targetId < 0)
                        continue;
                    BaseState target = bases[targetId];
                    // Pool toward a stable front, never toward whichever garrison
                    // happens to be largest this tick (which can reverse every wave).
                    if (target.teamId != teamId || target.isTurret || target.pendingTroops > 0 ||
                        target.isSuperProducer && target.uninterruptedSeconds > 0f ||
                        CountIncomingTroops(i, teamId) > 0)
                        continue;
                    float sourceFront = DistanceToNpcFront(i, teamId);
                    float targetFront = DistanceToNpcFront(targetId, teamId);
                    if (targetFront > sourceFront || Mathf.Approximately(targetFront, sourceFront) && targetId > i)
                        continue;
                    int sending = source.troops - NpcReserveForBase(i, teamId);
                    if (sending <= 0)
                        continue;
                    int losses = EstimateTurretLosses(i, targetId, teamId, sending, sendInterval);
                    // A capable team will not pay a quarter of a garrison just to shuffle
                    // it one base closer to the front.
                    if (losses > Mathf.Max(1, Mathf.RoundToInt(sending * Mathf.Lerp(0.5f, 0.15f, intelligence))))
                        continue;
                    bool onFront = false;
                    foreach (Vector2Int targetLink in links) {
                        int enemy = targetLink.x == targetId ? targetLink.y : targetLink.y == targetId ? targetLink.x : -1;
                        if (enemy >= 0 && bases[enemy].teamId != teamId) { onFront = true; break; }
                    }
                    if (!onFront)
                        continue;
                    float score = sending + (target.isSuperProducer ? 30f : 0f) +
                        (target.isSpeedBase ? 12f : 0f) - losses * 4f -
                        Vector2.Distance(source.position, target.position) * 0.1f;
                    if (score > bestScore) { bestScore = score; bestSource = i; bestTarget = targetId; }
                }
            }
            if (bestSource >= 0)
                QueueSend(bases[bestSource], bestTarget,
                    bases[bestSource].troops - NpcReserveForBase(bestSource, teamId), sendInterval);
        }

        float DistanceToNpcFront(int baseId, int teamId) {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < bases.Count; i++)
                if (bases[i].teamId != teamId)
                    nearest = Mathf.Min(nearest, (bases[i].position - bases[baseId].position).sqrMagnitude);
            return nearest;
        }

        void UpdateBases(float deltaTime) {
            for (int i = 0; i < bases.Count; i++) {
                BaseState state = bases[i];
                if (state.teamId < 0)
                    continue;

                state.productionDelay = Mathf.Max(0f, state.productionDelay - deltaTime);
                if (state.pendingTroops > 0) {
                    state.actionTimer += deltaTime;
                    while (state.actionTimer >= state.sendInterval) {
                        state.actionTimer -= state.sendInterval;
                        if (state.troops <= 0 || state.pendingTarget < 0) {
                            ClearSend(state);
                            break;
                        }

                        dots.Add(new DotState {
                            id = nextDotId++,
                            originBaseId = i,
                            sourceBaseId = i,
                            targetBaseId = state.pendingTarget,
                            ownerClientId = state.ownerClientId,
                            teamId = state.teamId,
                            route = state.pendingRoute,
                            routeIndex = 1
                        });
                        state.troops--;
                        if (state.troops == 0)
                            state.damageTaken = 0f;
                        state.pendingTroops--;
                        if (state.pendingTroops == 0) {
                            ClearSend(state);
                            break;
                        }
                    }
                    continue;
                }

                if (state.isTurret || state.productionDelay > 0f || HasOutgoingTroops(i)) {
                    InterruptProduction(state);
                    continue;
                }

                float production = state.isSuperProducer
                    ? DI_Rules.SuperProductionOverInterval(state.uninterruptedSeconds, deltaTime,
                        superProductionSpeed, superSpeedupSeconds)
                    : deltaTime * productionSpeed;
                production *= GetPlayerProductionMultiplier(state.ownerClientId);
                state.uninterruptedSeconds = Mathf.Min(superSpeedupSeconds, state.uninterruptedSeconds + deltaTime);
                if (state.troops >= Capacity(state)) {
                    state.actionTimer = 0f;
                    continue;
                }
                state.actionTimer += production;
                while (state.actionTimer >= 1f && state.troops < Capacity(state)) {
                    state.actionTimer -= 1f;
                    state.troops++;
                }
            }
        }

        bool HasOutgoingTroops(int sourceBaseId) {
            for (int i = 0; i < dots.Count; i++) {
                if (dots[i].originBaseId == sourceBaseId && dots[i].teamId == bases[sourceBaseId].teamId)
                    return true;
            }
            return false;
        }

        void QueueSend(BaseState source, int targetBaseId, int troopCount, float dispatchInterval) {
            source.pendingTarget = targetBaseId;
            source.pendingTroops = Mathf.Clamp(troopCount, 0, source.troops);
            source.sendInterval = Mathf.Max(0.05f, dispatchInterval);
            InterruptProduction(source);
            source.pendingRoute = null;
        }

        void ClearSend(BaseState state) {
            state.pendingTarget = -1;
            state.pendingTroops = 0;
            state.actionTimer = 0f;
            state.sendInterval = sendInterval;
            state.pendingRoute = null;
        }

        void RefreshTeamSpeedBases() {
            teamSpeedBases.Clear();
            foreach (BaseState state in bases) {
                if (!state.isSpeedBase || state.teamId < 0)
                    continue;
                teamSpeedBases.TryGetValue(state.teamId, out int count);
                teamSpeedBases[state.teamId] = count + 1;
            }
        }

        void UpdateDots(float deltaTime) {
            while (deltaTime > 0f && dots.Count > 0) {
                float step = Mathf.Min(deltaTime, SimulationStep);
                // Split at waypoints: every collision sweep is a straight road
                // segment, and unused time carries onto the next leg this tick.
                foreach (DotState dot in dots) {
                    float distance = Vector2.Distance(bases[dot.sourceBaseId].position, bases[dot.targetBaseId].position);
                    float arrival = (1f - dot.progress) * distance / TeamMoveSpeed(dot.teamId);
                    step = Mathf.Min(step, Mathf.Max(0.000001f, arrival));
                }
                StepDots(step);
                deltaTime = Mathf.Max(0f, deltaTime - step);
            }
            if (deltaTime > 0f)
                UpdateTurrets(deltaTime);
        }

        void StepDots(float deltaTime) {
            // Shoot before advancing/arriving so even a troop on its final step
            // is still a moving target. Stationary garrisons are never queried.
            UpdateTurrets(deltaTime);
            previousDotPositions.Clear();
            for (int i = 0; i < dots.Count; i++) {
                DotState dot = dots[i];
                previousDotPositions.Add(GetDotPosition(dot));
                float distance = Vector2.Distance(
                    bases[dot.sourceBaseId].position,
                    bases[dot.targetBaseId].position);

                dot.progress = Mathf.Min(1f, dot.progress + TeamMoveSpeed(dot.teamId) * deltaTime / Mathf.Max(0.000001f, distance));
                if (dot.progress >= 0.999999f)
                    dot.progress = 1f;
            }

            HashSet<int> destroyed = destroyedDots;
            destroyed.Clear();
            float collisionDistanceSquared = CollisionDistance * CollisionDistance;
            for (int i = 0; i < dots.Count; i++) {
                if (destroyed.Contains(i))
                    continue;

                Vector2 firstPosition = GetDotPosition(dots[i]);
                for (int j = i + 1; j < dots.Count; j++) {
                    if (destroyed.Contains(j) || dots[i].teamId == dots[j].teamId)
                        continue;

                    Vector2 relativeStart = previousDotPositions[i] - previousDotPositions[j];
                    Vector2 relativeEnd = firstPosition - GetDotPosition(dots[j]);
                    Vector2 relativeTravel = relativeEnd - relativeStart;
                    float closestTime = relativeTravel.sqrMagnitude > 0f
                        ? Mathf.Clamp01(-Vector2.Dot(relativeStart, relativeTravel) / relativeTravel.sqrMagnitude) : 0f;
                    if ((relativeStart + relativeTravel * closestTime).sqrMagnitude <= collisionDistanceSquared) {
                        float firstDamage = GetPlayerDamageMultiplier(dots[i].ownerClientId);
                        float secondDamage = GetPlayerDamageMultiplier(dots[j].ownerClientId);
                        float exchange = Mathf.Min(dots[i].health / secondDamage, dots[j].health / firstDamage);
                        dots[i].health -= secondDamage * exchange;
                        dots[j].health -= firstDamage * exchange;
                        if (dots[j].health <= 0.00001f)
                            destroyed.Add(j);
                        if (dots[i].health <= 0.00001f) {
                            destroyed.Add(i);
                            break;
                        }
                    }
                }
            }

            for (int i = dots.Count - 1; i >= 0; i--) {
                DotState dot = dots[i];
                if (destroyed.Contains(i)) {
                    dots.RemoveAt(i);
                } else if (dot.progress >= 1f) {
                    if (!ContinueRoute(dot)) {
                        ResolveArrival(dot);
                        dots.RemoveAt(i);
                    }
                }
            }
        }

        int TeamSpeedBaseCount(int teamId) =>
            teamId >= 0 && teamSpeedBases.TryGetValue(teamId, out int count) ? count : 0;

        /// <summary>Board units per second a team's troops actually travel at.</summary>
        float TeamMoveSpeed(int teamId) =>
            DI_Rules.TeamMoveSpeed(moveSpeed, TeamSpeedBaseCount(teamId), speedBaseBonus);

        bool ContinueRoute(DotState dot) {
            // Hostile intermediate bases must be captured before later troops
            // can pass through. Friendly waypoints do not consume garrison space.
            if (bases[dot.targetBaseId].teamId != dot.teamId || dot.route == null ||
                dot.routeIndex + 1 >= dot.route.Length)
                return false;
            dot.sourceBaseId = dot.targetBaseId;
            dot.targetBaseId = dot.route[++dot.routeIndex];
            dot.progress = 0f;
            return true;
        }

        Vector2 GetDotPosition(DotState dot) {
            return Vector2.Lerp(
                bases[dot.sourceBaseId].position,
                bases[dot.targetBaseId].position,
                Mathf.Clamp01(dot.progress));
        }

        void UpdateTurrets(float deltaTime) {
            foreach (BaseState turret in bases) {
                turret.turretCooldown = Mathf.Max(0f, turret.turretCooldown - deltaTime);
                if (!turret.isTurret || turret.turretCooldown > 0f)
                    continue;

                int target = -1;
                float nearest = turretRange * turretRange;
                Vector2 shotPosition = default;
                for (int i = 0; i < dots.Count; i++) {
                    DotState dot = dots[i];
                    if (turret.teamId >= 0 && dot.teamId == turret.teamId || dot.progress >= 1f)
                        continue;
                    Vector2 start = GetDotPosition(dot);
                    Vector2 end = Vector2.MoveTowards(start, bases[dot.targetBaseId].position, TeamMoveSpeed(dot.teamId) * deltaTime);
                    Vector2 travel = end - start;
                    float along = travel.sqrMagnitude > 0f
                        ? Mathf.Clamp01(Vector2.Dot(turret.position - start, travel) / travel.sqrMagnitude) : 0f;
                    Vector2 closest = start + travel * along;
                    float distance = (closest - turret.position).sqrMagnitude;
                    if (distance <= nearest) {
                        nearest = distance;
                        target = i;
                        shotPosition = closest;
                    }
                }
                if (target < 0)
                    continue;
                turret.shotPosition = shotPosition;
                turret.shotSequence++;
                turret.turretCooldown = turretFireRate;
                dots.RemoveAt(target);
            }
        }

        void ResolveArrival(DotState dot) {
            BaseState target = bases[dot.targetBaseId];
            // Allied waypoints bypass arrival; only actual recipients interrupt training.
            // Preserve the sending timer if this base is also dispatching troops.
            target.uninterruptedSeconds = 0f;
            if (target.pendingTroops == 0)
                target.actionTimer = 0f;
            target.productionDelay = AttackedProductionDelay;
            if (target.teamId == dot.teamId) {
                target.troops++;
                return;
            }

            if (target.troops > 0) {
                target.damageTaken += GetPlayerDamageMultiplier(dot.ownerClientId) * dot.health;
                int defeated = Mathf.Min(target.troops, Mathf.FloorToInt(target.damageTaken + 0.00001f));
                target.troops -= defeated;
                target.damageTaken -= defeated;
                if (target.troops == 0)
                    target.damageTaken = 0f;
                return;
            }

            target.ownerClientId = dot.ownerClientId;
            target.teamId = dot.teamId;
            target.troops = 1;
            target.damageTaken = 0f;
            target.turretCooldown = turretFireRate;
            ClearSend(target);
        }

        void CheckEndConditions() {
            if (!matchInProgress || bases.Count == 0)
                return;

            int survivor = FindSoleSurvivingTeam();
            if (survivor != -2)
                CompleteMatch(survivor);
        }

        // -2 means contested, -1 means no survivors. Neutral bases do not compete.
        int FindSoleSurvivingTeam() {
            int survivor = -1;
            foreach (BaseState state in bases) {
                if (state.teamId < 0)
                    continue;
                if (survivor >= 0 && survivor != state.teamId)
                    return -2;
                survivor = state.teamId;
            }
            foreach (DotState dot in dots) {
                if (dot.teamId < 0)
                    continue;
                if (survivor >= 0 && survivor != dot.teamId)
                    return -2;
                survivor = dot.teamId;
            }
            return survivor;
        }

        void CheckEliminatedTeams() {
            foreach (int teamId in dotInvadersTeams) {
                if (announcedEliminations.Contains(teamId) || HasTeamPresence(teamId))
                    continue;

                announcedEliminations.Add(teamId);
                SendTeamChatMessage(teamId, "eliminated.");
            }
        }

        bool HasTeamPresence(int teamId) {
            for (int i = 0; i < bases.Count; i++) {
                if (bases[i].teamId == teamId)
                    return true;
            }
            for (int i = 0; i < dots.Count; i++) {
                if (dots[i].teamId == teamId)
                    return true;
            }
            return false;
        }

        int DetermineLeadingTeam() {
            var scores = new Dictionary<int, int>();
            for (int i = 0; i < bases.Count; i++) {
                BaseState state = bases[i];
                if (state.teamId < 0)
                    continue;
                scores.TryGetValue(state.teamId, out int score);
                scores[state.teamId] = score + 10000 + state.troops;
            }
            for (int i = 0; i < dots.Count; i++) {
                scores.TryGetValue(dots[i].teamId, out int score);
                scores[dots[i].teamId] = score + 1;
            }

            int leader = -1;
            int bestScore = int.MinValue;
            bool tied = false;
            foreach (KeyValuePair<int, int> entry in scores) {
                if (entry.Value > bestScore) {
                    leader = entry.Key;
                    bestScore = entry.Value;
                    tied = false;
                } else if (entry.Value == bestScore) {
                    tied = true;
                }
            }
            return tied ? -1 : leader;
        }

        void CompleteMatch(int winnerTeamId) {
            if (!matchInProgress)
                return;

            CheckEliminatedTeams();
            matchInProgress = false;
            winningTeamId = winnerTeamId;
            for (int i = 0; i < bases.Count; i++)
                ClearSend(bases[i]);

            if (winnerTeamId >= 0 && winnerTeamId < NpcTeamIdStart) {
                foreach (NetworkConnection connection in InstanceFinder.ServerManager.Clients.Values) {
                    if (connection.IsValid && playerTeams.TryGetValue(connection.ClientId, out int playerTeam) &&
                        playerTeam == winnerTeamId)
                        ServerReward.AddReward(connection, WinnerXPReward, WinnerGoldReward);
                }
            }

            if (winnerTeamId >= 0)
                SendTeamChatMessage(winnerTeamId, "wins the game!");

            UpdateTopMessage();
            BroadcastState();
        }

        static void SendTeamChatMessage(int teamId, string message) {
            string teamName = GetDotInvadersTeamName(teamId, teamId >= NpcTeamIdStart);
            string coloredTeamName = TeamConfig.ColorRichText(
                teamName,
                TeamConfig.TeamToColor(DI_Rules.GetTeamColor(teamId)));
            ServerChat.SendSystemMessage(new SystemMessageBroadcast(
                $"{coloredTeamName} {message}",
                SystemMessageSource.CustomMessage));
        }

        static string GetDotInvadersTeamName(int teamId, bool npc) {
            string name = DI_Rules.GetTeamColor(teamId).ToString().ToUpperInvariant();
            return npc ? $"{name} NPC TEAM" : $"{name} TEAM";
        }

        void UpdateTopMessage() {
            if (matchInProgress) {
                int minutes = Mathf.Max(0, secondsRemaining) / 60;
                int seconds = Mathf.Max(0, secondsRemaining) % 60;
                SetTopMessage($"Dot Invaders - {minutes}:{seconds:00}");
                return;
            }

            if (winningTeamId < 0)
                SetTopMessage("Dot Invaders - Draw");
            else if (winningTeamId >= NpcTeamIdStart)
                SetTopMessage($"{GetDotInvadersTeamName(winningTeamId, true)} wins!");
            else
                SetTopMessage($"{GetDotInvadersTeamName(winningTeamId, false)} wins! +{WinnerXPReward} XP / +{WinnerGoldReward} gold");
        }

        void BroadcastState() {
            if (!initialized || !InstanceFinder.IsServerStarted)
                return;

            DI_StateBroadcast state = CreateState();
            foreach (NetworkConnection connection in InstanceFinder.ServerManager.Clients.Values) {
                if (!connection.IsActive || !connection.IsAuthenticated)
                    continue;

                state.yourClientId = connection.ClientId;
                state.yourTeamId = playerTeams.TryGetValue(connection.ClientId, out int teamId) ? teamId : -1;
                InstanceFinder.ServerManager.Broadcast(connection, state);
            }
        }

        DI_StateBroadcast CreateState() {
            int baseCount = bases.Count;
            int linkCount = links.Count;
            int dotCount = dots.Count;

            var state = new DI_StateBroadcast {
                revision = ++revision,
                yourTeamId = -1,
                secondsRemaining = secondsRemaining,
                matchEnded = initialized && !matchInProgress,
                winningTeamId = winningTeamId,
                npcIntelligence = npcIntelligence,
                turretRange = turretRange,
                turretFireRate = turretFireRate,
                maxCapacity = maxCapacity,
                moveSpeed = moveSpeed,
                superProductionSpeed = superProductionSpeed,
                superSpeedupSeconds = superSpeedupSeconds,
                superMaxCapacity = superMaxCapacity,
                productionSpeed = productionSpeed,
                sendInterval = sendInterval,
                speedBaseBonus = speedBaseBonus,
                npcAggression = npcAggression,
                baseSuperProducers = new bool[baseCount],
                baseProductionCharge = new float[baseCount],
                baseProductionRates = new float[baseCount],
                basePositions = new Vector2[baseCount],
                baseTroops = new int[baseCount],
                baseOwners = new int[baseCount],
                baseTeams = new int[baseCount],
                basePendingTroops = new int[baseCount],
                linkSources = new int[linkCount],
                linkTargets = new int[linkCount],
                dotIds = new int[dotCount],
                dotPositions = new Vector2[dotCount],
                dotTeams = new int[dotCount],
                baseTurrets = new bool[baseCount],
                turretShotSequences = new int[baseCount],
                turretShotPositions = new Vector2[baseCount],
                baseRoutes = new DI_Route[baseCount],
                baseSpeedBases = new bool[baseCount]
            };

            for (int i = 0; i < baseCount; i++) {
                state.basePositions[i] = bases[i].position;
                state.baseTroops[i] = bases[i].troops;
                state.baseOwners[i] = bases[i].ownerClientId;
                state.baseTeams[i] = bases[i].teamId;
                state.basePendingTroops[i] = bases[i].pendingTroops;
                state.baseTurrets[i] = bases[i].isTurret;
                state.baseSuperProducers[i] = bases[i].isSuperProducer;
                state.baseSpeedBases[i] = bases[i].isSpeedBase;
                state.baseProductionCharge[i] = Mathf.Clamp01(bases[i].uninterruptedSeconds / superSpeedupSeconds);
                BaseState production = bases[i];
                state.baseProductionRates[i] = production.teamId >= 0 && !production.isTurret &&
                    production.pendingTroops == 0 && production.productionDelay <= 0f &&
                    production.troops < Capacity(production) && !HasOutgoingTroops(i) ? ProductionRate(production) : 0f;
                state.turretShotSequences[i] = bases[i].shotSequence;
                state.turretShotPositions[i] = bases[i].shotPosition;
                state.baseRoutes[i] = new DI_Route { baseIds = bases[i].pendingRoute };
            }

            foreach (DotState dot in dots) {
                if (dot.route == null || dot.route.Length < 2)
                    continue;
                int source = dot.route[0];
                if (bases[source].ownerClientId == dot.ownerClientId && state.baseRoutes[source].baseIds == null)
                    state.baseRoutes[source] = new DI_Route { baseIds = dot.route };
            }

            for (int i = 0; i < linkCount; i++) {
                state.linkSources[i] = links[i].x;
                state.linkTargets[i] = links[i].y;
            }

            for (int i = 0; i < dotCount; i++) {
                state.dotIds[i] = dots[i].id;
                state.dotPositions[i] = GetDotPosition(dots[i]);
                state.dotTeams[i] = dots[i].teamId;
            }

            return state;
        }

        internal float NPCIntelligence => npcIntelligence;
        internal float TurretRange => turretRange;
        internal float TurretFireRate => turretFireRate;
        internal int MaxCapacity => maxCapacity;
        internal float MoveSpeed => moveSpeed;
        internal float MoveSpeedMultiplier => moveSpeedMultiplier;
        internal float SuperProductionSpeed => superProductionSpeed;
        internal float SuperSpeedupSeconds => superSpeedupSeconds;
        internal int SuperMaxCapacity => superMaxCapacity;
        internal float ProductionSpeed => productionSpeed;
        internal float SendInterval => sendInterval;
        internal float SendIntervalMultiplier => sendIntervalMultiplier;
        internal float SpeedBaseBonus => speedBaseBonus;
        internal float NpcAggression => npcAggression;
        internal int SecondsRemaining => secondsRemaining;
        internal bool MatchInProgress => matchInProgress;

        internal float GetPlayerDamageMultiplier(int clientId) =>
            playerDamageMultipliers.TryGetValue(clientId, out float value) ? value : 1f;

        internal float GetPlayerProductionMultiplier(int clientId) =>
            playerProductionMultipliers.TryGetValue(clientId, out float value) ? value : 1f;

        internal bool SetPlayerMultiplier(int clientId, float value, bool damage) {
            if (!matchInProgress || !playerTeams.ContainsKey(clientId) || float.IsNaN(value) || float.IsInfinity(value))
                return false;
            var multipliers = damage ? playerDamageMultipliers : playerProductionMultipliers;
            value = Mathf.Clamp(value, 1f, 10f);
            if (value == 1f)
                multipliers.Remove(clientId);
            else
                multipliers[clientId] = value;
            return true;
        }

        internal void SetSuperProduction(float speed, float speedupSeconds) {
            superProductionSpeed = Mathf.Clamp(speed, 0.01f, 500f);
            superSpeedupSeconds = Mathf.Clamp(speedupSeconds, 0.01f, 600f);
            BroadcastState();
        }

        internal void SetSuperMaxCapacity(int value) {
            superMaxCapacity = Mathf.Clamp(value, 1, 10000);
            BroadcastState();
        }

        internal void SetNPCIntelligence(float value) {
            npcIntelligence = Mathf.Clamp(value, 0f, 100f);
            BroadcastState();
        }

        internal void SetTurretRange(float value) {
            turretRange = Mathf.Max(MinimumTurretRange, value);
            BroadcastState();
        }

        internal void SetTurretFireRate(float value) {
            turretFireRate = Mathf.Max(0.01f, value);
            BroadcastState();
        }

        internal void SetMaxCapacity(int value) {
            maxCapacity = Mathf.Max(1, value);
            BroadcastState();
        }

        internal void SetMoveSpeedMultiplier(float value) {
            moveSpeedMultiplier = Mathf.Clamp(value, DI_Rules.MinMoveSpeedMultiplier, DI_Rules.MaxMoveSpeedMultiplier);
            BroadcastState();
        }

        internal void SetProductionSpeed(float value) {
            productionSpeed = Mathf.Clamp(value, 0.01f, 100f);
            BroadcastState();
        }

        internal void SetSendIntervalMultiplier(float value) {
            sendIntervalMultiplier = Mathf.Clamp(value, DI_Rules.MinSendIntervalMultiplier, DI_Rules.MaxSendIntervalMultiplier);
            // Bases already mid-dispatch keep the rate they were ordered at;
            // only idle bases would otherwise never pick the new value up.
            foreach (BaseState state in bases)
                if (state.pendingTroops == 0)
                    state.sendInterval = sendInterval;
            BroadcastState();
        }

        internal void SetSpeedBaseBonus(float value) {
            speedBaseBonus = Mathf.Clamp(value, 0f, 5f);
            BroadcastState();
        }

        internal void SetNpcAggression(float value) {
            npcAggression = Mathf.Clamp(value, 0f, 100f);
            BroadcastState();
        }

        internal void SetSecondsRemaining(int value) {
            secondsRemaining = Mathf.Clamp(value, 1, 7200);
            UpdateTopMessage();
            BroadcastState();
        }

        /// <summary>Rerolls the board without disturbing the running match clock.</summary>
        internal bool RestartMatch() {
            // The countdown lives in StartAsync; restarting after it exits would
            // leave a match that never ticks down, so only reroll a live match.
            if (!matchInProgress)
                return false;
            InitializeMatch();
            return true;
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
