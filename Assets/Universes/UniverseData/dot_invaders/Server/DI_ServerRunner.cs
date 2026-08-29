#if UNITY_SERVER
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    public struct DI_SendRequest : IBroadcast {
        public int sourceBaseId;
        public int targetBaseId;
    }

    public struct DI_StateBroadcast : IBroadcast {
        public int revision;
        public int yourClientId;
        public int yourTeamId;
        public int secondsRemaining;
        public bool matchEnded;
        public int winningTeamId;
        public Vector2[] basePositions;
        public int[] baseTroops;
        public int[] baseOwners;
        public int[] baseTeams;
        public int[] basePendingTroops;
        public int[] linkSources;
        public int[] linkTargets;
        public int[] dotIds;
        public Vector2[] dotPositions;
        public int[] dotTeams;
    }

    public sealed class DI_ServerRunner : ServerRunner {
        static readonly TeamColor[] DotInvadersTeamOrder = {
            TeamColor.Blue,
            TeamColor.Red,
            TeamColor.Green,
            TeamColor.Orange,
            TeamColor.Purple,
            TeamColor.Cyan,
            TeamColor.Pink,
            TeamColor.Lime
        };

        const int BaseCount = 32;
        const int StartingTroops = 12;
        const int NeutralTroops = 5;
        const int NpcTeamCount = 2;
        const int NpcBasesPerTeam = 2;
        const int NpcStartingTroops = 14;
        const int NpcTeamIdStart = 100;
        const int WinnerXPReward = 100;
        const int WinnerGoldReward = 50;
        const float ArenaHalfWidth = 100f;
        const float ArenaHalfHeight = 74f;
        const float MinimumBaseSpacing = 12f;
        const float DotSpeed = 12f;
        const float CollisionDistance = 0.75f;
        const float SnapshotInterval = 0.1f;
        const float ProductionInterval = 0.8f;
        const float AttackedProductionDelay = 2f;
        const float SendInterval = 0.4f;
        const float NpcThinkInterval = 1.75f;
        const int NpcTroopReserve = 3;

        [SerializeField, Min(30)] int matchDurationSeconds = 300;

        sealed class BaseState {
            public Vector2 position;
            public int ownerClientId = -1;
            public int teamId = -1;
            public int troops = NeutralTroops;
            public int pendingTarget = -1;
            public int pendingTroops;
            public float actionTimer;
            public float productionDelay;
        }

        sealed class DotState {
            public int id;
            public int sourceBaseId;
            public int targetBaseId;
            public int ownerClientId;
            public int teamId;
            public float progress;
        }

        sealed class NpcTeamState {
            public int ownerClientId;
            public int teamId;
            public float thinkTimer;
        }

        readonly List<BaseState> bases = new();
        readonly List<Vector2Int> links = new();
        readonly List<DotState> dots = new();
        readonly List<NpcTeamState> npcTeams = new();
        readonly Dictionary<int, int> playerTeams = new();
        readonly HashSet<int> dotInvadersTeams = new();
        readonly HashSet<int> announcedEliminations = new();

        float snapshotTimer;
        int nextTeamId;
        int nextDotId;
        int revision;
        int secondsRemaining;
        int winningTeamId = -1;
        bool initialized;
        bool matchInProgress;
        bool hadHumanParticipant;

        protected override void Awake() {
            base.Awake();
            ServerPlayerCharacter.CanSpawnFunction = _ => false;
            InstanceFinder.ServerManager.RegisterBroadcast<DI_SendRequest>(OnSendRequest, true);
            PlayerData.OnPlayerRemoved += OnPlayerRemoved;
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
            Restart();
        }

        protected override void OnPlayerAdded(PlayerData playerData) {
            base.OnPlayerAdded(playerData);
            playerData.cameraTypes.Add(GameCameraType.TwoDimCamera);
            AssignPlayer(playerData);
        }

        void Update() {
            if (!initialized || !matchInProgress || !InstanceFinder.IsServerStarted)
                return;

            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            UpdateNpcTeams(deltaTime);
            UpdateBases(deltaTime);
            UpdateDots(deltaTime);
            CheckEliminatedTeams();
            CheckEndConditions();

            snapshotTimer += deltaTime;
            if (snapshotTimer >= SnapshotInterval) {
                snapshotTimer %= SnapshotInterval;
                BroadcastState();
            }
        }

        void InitializeMatch() {
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
            hadHumanParticipant = false;

            GenerateBases();
            GenerateLinks();
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
                    Mathf.Lerp(-ArenaHalfWidth, ArenaHalfWidth, (float)random.NextDouble()),
                    Mathf.Lerp(-ArenaHalfHeight, ArenaHalfHeight, (float)random.NextDouble()));

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

            // Connecting every new base to an earlier base guarantees one connected graph.
            for (int i = 1; i < bases.Count; i++) {
                int nearest = 0;
                float nearestDistance = float.MaxValue;
                for (int j = 0; j < i; j++) {
                    float distance = (bases[i].position - bases[j].position).sqrMagnitude;
                    if (distance < nearestDistance) {
                        nearestDistance = distance;
                        nearest = j;
                    }
                }
                AddLink(i, nearest, edgeKeys);
            }

            // Add local alternatives so every base has useful neighboring choices.
            for (int i = 0; i < bases.Count; i++) {
                var neighbors = new List<int>();
                for (int j = 0; j < bases.Count; j++) {
                    if (i != j)
                        neighbors.Add(j);
                }
                neighbors.Sort((a, b) =>
                    (bases[i].position - bases[a].position).sqrMagnitude.CompareTo(
                        (bases[i].position - bases[b].position).sqrMagnitude));

                for (int j = 0; j < Mathf.Min(3, neighbors.Count); j++)
                    AddLink(i, neighbors[j], edgeKeys);
            }
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

            player.SetPlayerTeam(new TeamConfig((teamId % 3) switch {
                0 => TeamColor.Blue,
                1 => TeamColor.Red,
                _ => TeamColor.Green
            }));

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
            home.pendingTarget = -1;
            home.pendingTroops = 0;
            home.actionTimer = 0f;
            home.productionDelay = 0f;
            hadHumanParticipant = true;
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
                    state.actionTimer = 0f;
                    state.productionDelay = 0f;
                }
            }
        }

        int FindSpawnBase() {
            int best = -1;
            float bestDistance = float.MinValue;

            for (int i = 0; i < bases.Count; i++) {
                if (bases[i].teamId >= 0)
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

            for (int i = 0; i < bases.Count; i++) {
                BaseState state = bases[i];
                if (state.ownerClientId != clientId)
                    continue;

                state.ownerClientId = -1;
                state.teamId = -1;
                state.troops = NeutralTroops;
                state.pendingTarget = -1;
                state.pendingTroops = 0;
                state.actionTimer = 0f;
                state.productionDelay = 0f;
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
                if (source.pendingTroops > 0) {
                    ClearSend(source);
                    BroadcastState();
                }
                return;
            }

            if (request.targetBaseId < 0 || request.targetBaseId >= bases.Count ||
                !AreNeighbors(request.sourceBaseId, request.targetBaseId))
                return;

            if (source.pendingTroops > 0) {
                source.pendingTarget = request.targetBaseId;
                BroadcastState();
                return;
            }

            if (source.troops <= 0)
                return;

            QueueSend(source, request.targetBaseId, source.troops);
            BroadcastState();
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
                if (npcTeam.thinkTimer < NpcThinkInterval)
                    continue;
                npcTeam.thinkTimer %= NpcThinkInterval;

                int bestSource = -1;
                int bestTarget = -1;
                float bestScore = float.MinValue;
                for (int sourceId = 0; sourceId < bases.Count; sourceId++) {
                    BaseState source = bases[sourceId];
                    if (source.teamId != npcTeam.teamId || source.pendingTroops > 0 ||
                        source.troops <= NpcTroopReserve)
                        continue;

                    for (int linkIndex = 0; linkIndex < links.Count; linkIndex++) {
                        Vector2Int link = links[linkIndex];
                        int targetId = link.x == sourceId ? link.y : link.y == sourceId ? link.x : -1;
                        if (targetId < 0 || bases[targetId].teamId == npcTeam.teamId)
                            continue;

                        BaseState target = bases[targetId];
                        float enemyPriority = target.ownerClientId >= 0 ? 80f : target.teamId >= 0 ? 55f : 20f;
                        float score = enemyPriority + source.troops * 2f - target.troops * 3f + UnityEngine.Random.value * 4f;
                        if (score <= bestScore)
                            continue;

                        bestScore = score;
                        bestSource = sourceId;
                        bestTarget = targetId;
                    }
                }

                if (bestSource >= 0) {
                    BaseState source = bases[bestSource];
                    QueueSend(source, bestTarget, source.troops - NpcTroopReserve);
                }
            }
        }

        void UpdateBases(float deltaTime) {
            for (int i = 0; i < bases.Count; i++) {
                BaseState state = bases[i];
                if (state.teamId < 0)
                    continue;

                state.productionDelay = Mathf.Max(0f, state.productionDelay - deltaTime);
                if (state.pendingTroops > 0) {
                    state.actionTimer += deltaTime;
                    while (state.actionTimer >= SendInterval) {
                        state.actionTimer -= SendInterval;
                        if (state.troops <= 0 || state.pendingTarget < 0) {
                            ClearSend(state);
                            break;
                        }

                        dots.Add(new DotState {
                            id = nextDotId++,
                            sourceBaseId = i,
                            targetBaseId = state.pendingTarget,
                            ownerClientId = state.ownerClientId,
                            teamId = state.teamId
                        });
                        state.troops--;
                        state.pendingTroops--;
                        if (state.pendingTroops == 0) {
                            ClearSend(state);
                            break;
                        }
                    }
                    continue;
                }

                if (state.productionDelay > 0f || HasOutgoingTroops(i)) {
                    state.actionTimer = 0f;
                    continue;
                }

                state.actionTimer += deltaTime;
                while (state.actionTimer >= ProductionInterval) {
                    state.actionTimer -= ProductionInterval;
                    state.troops++;
                }
            }
        }

        bool HasOutgoingTroops(int sourceBaseId) {
            for (int i = 0; i < dots.Count; i++) {
                if (dots[i].sourceBaseId == sourceBaseId)
                    return true;
            }
            return false;
        }

        static void QueueSend(BaseState source, int targetBaseId, int troopCount) {
            source.pendingTarget = targetBaseId;
            source.pendingTroops = Mathf.Clamp(troopCount, 0, source.troops);
            source.actionTimer = 0f;
        }

        static void ClearSend(BaseState state) {
            state.pendingTarget = -1;
            state.pendingTroops = 0;
            state.actionTimer = 0f;
        }

        void UpdateDots(float deltaTime) {
            for (int i = 0; i < dots.Count; i++) {
                DotState dot = dots[i];
                float distance = Vector2.Distance(
                    bases[dot.sourceBaseId].position,
                    bases[dot.targetBaseId].position);
                dot.progress += DotSpeed * deltaTime / Mathf.Max(0.01f, distance);
            }

            var destroyed = new HashSet<int>();
            float collisionDistanceSquared = CollisionDistance * CollisionDistance;
            for (int i = 0; i < dots.Count; i++) {
                if (destroyed.Contains(i))
                    continue;

                Vector2 firstPosition = GetDotPosition(dots[i]);
                for (int j = i + 1; j < dots.Count; j++) {
                    if (destroyed.Contains(j) || dots[i].teamId == dots[j].teamId)
                        continue;

                    if ((firstPosition - GetDotPosition(dots[j])).sqrMagnitude <= collisionDistanceSquared) {
                        destroyed.Add(i);
                        destroyed.Add(j);
                        break;
                    }
                }
            }

            for (int i = dots.Count - 1; i >= 0; i--) {
                DotState dot = dots[i];
                if (destroyed.Contains(i)) {
                    dots.RemoveAt(i);
                } else if (dot.progress >= 1f) {
                    ResolveArrival(dot);
                    dots.RemoveAt(i);
                }
            }
        }

        Vector2 GetDotPosition(DotState dot) {
            return Vector2.Lerp(
                bases[dot.sourceBaseId].position,
                bases[dot.targetBaseId].position,
                Mathf.Clamp01(dot.progress));
        }

        void ResolveArrival(DotState dot) {
            BaseState target = bases[dot.targetBaseId];
            if (target.teamId == dot.teamId) {
                target.troops++;
                return;
            }

            target.productionDelay = AttackedProductionDelay;
            if (target.troops > 0) {
                target.troops--;
                return;
            }

            target.ownerClientId = dot.ownerClientId;
            target.teamId = dot.teamId;
            target.troops = 1;
            ClearSend(target);
        }

        void CheckEndConditions() {
            if (!matchInProgress || bases.Count == 0)
                return;

            int capturedTeam = bases[0].teamId;
            if (capturedTeam >= 0) {
                bool ownsEveryBase = true;
                for (int i = 1; i < bases.Count; i++) {
                    if (bases[i].teamId != capturedTeam) {
                        ownsEveryBase = false;
                        break;
                    }
                }
                if (ownsEveryBase) {
                    CompleteMatch(capturedTeam);
                    return;
                }
            }

            bool humanTeamPresent = false;
            foreach (int teamId in playerTeams.Values) {
                if (HasTeamPresence(teamId)) {
                    humanTeamPresent = true;
                    break;
                }
            }
            if (hadHumanParticipant && !humanTeamPresent)
                CompleteMatch(DetermineLeadingTeam());
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
                TeamConfig.TeamToColor(GetDotInvadersTeam(teamId)));
            ServerChat.SendSystemMessage(new SystemMessageBroadcast(
                $"{coloredTeamName} {message}",
                SystemMessageSource.CustomMessage));
        }

        static TeamColor GetDotInvadersTeam(int teamId) {
            return teamId < 0 ? TeamColor.None : DotInvadersTeamOrder[teamId % DotInvadersTeamOrder.Length];
        }

        static string GetDotInvadersTeamName(int teamId, bool npc) {
            string name = GetDotInvadersTeam(teamId).ToString().ToUpperInvariant();
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
                basePositions = new Vector2[baseCount],
                baseTroops = new int[baseCount],
                baseOwners = new int[baseCount],
                baseTeams = new int[baseCount],
                basePendingTroops = new int[baseCount],
                linkSources = new int[linkCount],
                linkTargets = new int[linkCount],
                dotIds = new int[dotCount],
                dotPositions = new Vector2[dotCount],
                dotTeams = new int[dotCount]
            };

            for (int i = 0; i < baseCount; i++) {
                state.basePositions[i] = bases[i].position;
                state.baseTroops[i] = bases[i].troops;
                state.baseOwners[i] = bases[i].ownerClientId;
                state.baseTeams[i] = bases[i].teamId;
                state.basePendingTroops[i] = bases[i].pendingTroops;
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

        protected override void Reset() {
            initialized = false;
            matchInProgress = false;
            bases.Clear();
            links.Clear();
            dots.Clear();
            npcTeams.Clear();
            playerTeams.Clear();
            dotInvadersTeams.Clear();
            announcedEliminations.Clear();
            hadHumanParticipant = false;
            base.Reset();
        }

        protected override void Stop() {
            matchInProgress = false;
            base.Stop();
        }

        protected override void OnDestroy() {
            if (InstanceFinder.ServerManager != null)
                InstanceFinder.ServerManager.UnregisterBroadcast<DI_SendRequest>(OnSendRequest);
            PlayerData.OnPlayerRemoved -= OnPlayerRemoved;
            base.OnDestroy();
        }
    }
}
#endif
