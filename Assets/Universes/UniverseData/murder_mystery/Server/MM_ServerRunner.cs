using Cysharp.Threading.Tasks;
using EasyDebug.Shared;
using FishNet.Connection;
using FishNet.Object;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.Core;
using RyanAssets.DataService;
using RyanAssets.Levels.Server;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.Tools.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Universes.UniverseData.murder_mystery.Server;

namespace Universes.murder_mystery.Server {
    enum MM_Mode {
        NPCsVsPlayers,
        Classic,
        Infection,
        Unarmed, // Deprecated
    }
    public class MM_ServerRunner : ServerRunner {
        // Infection needs a broad, team-neutral search radius so infected NPCs actively
        // hunt both innocents and sheriffs without restoring the sheriff-only override.
        private const float InfectionAttackDetectionMultiplier = 6f;

        [SerializeField]
        GameObject RobotNPC_Prefab;
        [SerializeField]
        DebugBool DebugSingleNpc, DebugMotionlessNpc;
        [SerializeField]
        DebugBool DebugNoIntermission;
        [SerializeField]
        DebugBool FallTest;
        //[SerializeField]
        //DebugValue<float> DebugMurderCount;
        [SerializeField]
        MM_Mode mode;
        [SerializeField]
        List<TeamColor> npcRoles, playerRoles;
        
        public static float SpawnMultiplier;
        public static bool ForceEndGame;
        public static Action UpdateGameBarEvent;
        bool gameInProgress;
        string[] alienNames;
        int startNPCs, startPlayers, gameDurationLeft;
        int startMurd, startSheriff, startInnocent;
        protected override void Awake(){
            base.Awake();
            SharedGlobalEvents.TeamEnemies = new()
            {
                [TeamColor.Red] = new() { TeamColor.Blue, TeamColor.Green },
                [TeamColor.Blue] = new() { TeamColor.Red },
                [TeamColor.Green] = new() // can't kill anyone!
            };
            UpdateGameBarEvent = null;
            ForceEndGame = false;
            SpawnMultiplier = 1f;
            //ServerPlayerCharacter.CharacterAdded += OnCharacterAdded;
            ServerPlayerCharacter.CanSpawnFunction = CanSpawnFunction;
            ServerPlayerCharacter.SpawnLocationFunction = SpawnLocationFunction;


            base.SetTeamKillEnabled(true);
            if (DebugMotionlessNpc.Value) {
                LocalNPC.FleeSpeedMultiplier = 0f;
                LocalNPC.WalkSpeedMultiplier = 0f;
                LocalNPC.AttackSpeedMultiplier = 0f;
            }
            else {
                LocalNPC.FleeSpeedMultiplier = 1f;
                LocalNPC.WalkSpeedMultiplier = 1f;
                LocalNPC.AttackSpeedMultiplier = 1f;
            }
        }
        bool CanSpawnFunction(NetworkConnection player) {
            if (!gameInProgress)
                return true;
            if (mode == MM_Mode.Infection) {
                PlayerData playerData = PlayerData.GetPlayerData(player);
                //return gameDurationLeft > 30
                //    && playerData != null
                //    && playerData.team.Value.realTeam != TeamColor.Red;
                return false;
            }

            return mode switch {
                MM_Mode.Classic => false,
                _ => true
            };
        }
        Vector3 SpawnLocationFunction(NetworkConnection player) {
            return DebugSingleNpc.Value ? new Vector3(1120.56995f, 12.12100029f, 1008.34003f) : ServerPathfinding.GetRandomPosition();
        }
        protected void OnSetPlayerTeam(PlayerData player, TeamConfig teamConfig) {
            switch (teamConfig.realTeam) {
                case TeamColor.Red: // Murderer
                    player.staminaMax.Value = 165f;
                    player.staminaRegen.Value = 25f;
                    player.staminaCooldown.Value = 0.6f;
                    break;
                default:
                    player.staminaMax.Value = 100f;
                    player.staminaRegen.Value = 15f;
                    player.staminaCooldown.Value = 0.8f;
                    break;
            }
            player.walkSpeed.Value = 16f;
            player.sprintSpeed.Value = 21f;
        }
        protected override void OnPlayerAdded(PlayerData player) {
            base.OnPlayerAdded(player);
            player.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
            //data.leaderboard.AddRange(new[] { 0 });
            //data.tools.AddRange(new[] { ToolEnum.Dagger });
            //ServerTool.Instance.AddTool(player, ToolEnum.Dagger);
            //ServerTool.Instance.AddTool(player, ToolEnum.Pistol);

            //ServerTool.tool
            switch (mode) {
                case MM_Mode.NPCsVsPlayers:
                    player.SetPlayerTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                    break;
                case MM_Mode.Classic:
                    player.SetPlayerTeam(new TeamConfig(TeamColor.White, TeamColor.White));
                    break;
                case MM_Mode.Infection:
                    player.SetPlayerTeam(new TeamConfig(TeamColor.Blue));
                    break;
                case MM_Mode.Unarmed:
                    player.SetPlayerTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                    break;
            }
            player.team.OnChange += (oldTeam, newTeam, _) => OnSetPlayerTeam(player, newTeam);
            OnSetPlayerTeam(player, player.team.Value);
        }
        void PreparePlayerForActiveRound(PlayerData player) {
            switch (mode) {
                case MM_Mode.NPCsVsPlayers:
                case MM_Mode.Unarmed:
                    player.SetPlayerTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                    break;
                case MM_Mode.Classic:
                    // Classic roles are assigned once in SpawnPlayers. Late joins
                    // cannot spawn while a Classic round is already in progress.
                    break;
                case MM_Mode.Infection:
                    player.SetPlayerTeam(new TeamConfig(TeamColor.Blue));
                    break;
            }
            player.walkSpeed.Value = 16f;
            player.sprintSpeed.Value = 21f;
        }
        protected override void OnCharacterAdded(LocalCharacter character){
            base.OnCharacterAdded(character);
            character.Init(100);
            character.InitDefaultEffects();
            
            character.OnDied += (source, killer) => SharedOnDied(character, source, killer);
            //new Vector3(1120.56995f, -8.12100029f, 1008.34003f);
            //character.SetScale(0.7f * Vector3.one);
            //character.SetTeam(new TeamConfig(TeamColor.Green, TeamColor.Green));
            if (!gameInProgress)
                return;

            // PlayerData.OnPlayerAdded and ServerPlayerCharacter.PlayerAdded are
            // independent subscribers, so their invocation order is not safe to
            // rely on. Reapply the active-round configuration after the character
            // exists and immediately before assigning its tools.
            PlayerData player = PlayerData.GetPlayerData(character.Owner);
            PreparePlayerForActiveRound(player);
            RandomizeCharacterName(character);
            SpawnPlayerTools(player, character.NetworkObject);
        }
        void SharedOnDied(GameCharacter character, DamageType source, IEntity killer) {
            GameCharacter killerCharacter = killer as GameCharacter;
            if (mode == MM_Mode.Classic) {
                if (killerCharacter != null) {
                    if (killerCharacter.Owner.IsValid && source == DamageType.Gun && character.GetTeam().realTeam != TeamColor.Red) {
                        //ServerTool.Instance.DespawnTool(killerCharacter.Owner, ToolEnum.Pistol);
                        ToolBaseShared tool = ServerTool.Instance.GetTool(killerCharacter.Owner, ToolEnum.Pistol);
                        tool.UpdateServerCooldown(20f);
                        //tool.
                    }
                    if (killerCharacter.GetTeam().realTeam == TeamColor.Red) {
                        killerCharacter.HealMaxHealth(character.Owner.IsValid ? 60 : 20);
                    }
                }
                if (character.Tools.Any((tool) => tool.toolEnum == ToolEnum.Pistol))
                    ServerTool.Instance.SpawnFloatingTool(ToolEnum.Pistol, character.transform.position + character.transform.lossyScale.y / 2 * Vector3.up).OnToolCollectedFunc = OnTryCollectToolFunc;
                if (character.Tools.Any((tool) => tool.toolEnum == ToolEnum.Dagger))
                    ServerTool.Instance.SpawnFloatingTool(ToolEnum.Dagger, character.transform.position + character.transform.lossyScale.y / 2 * Vector3.up).OnToolCollectedFunc = OnTryCollectToolFunc;
                if (character.GetTeam().realTeam == TeamColor.Blue) {
                    if (GetTeamCount(TeamColor.Blue) == 0) {
                        foreach (GameCharacter gameCharacter in GameCharacter.TeamToCharacter[TeamColor.Red]) {
                            if (gameCharacter.TryGetComponent<LocalNPC>(out LocalNPC localNPC)) {
                                localNPC.AttackDetectionRadius *= 3.5f;
                            }
                        }
                    }
                }
                character.SetRealColor(TeamColor.White);
            } else if (mode == MM_Mode.Infection) {
                if (killerCharacter != null
                    && killerCharacter.GetTeam().realTeam == TeamColor.Red
                    && character.GetTeam().realTeam != TeamColor.Red) {
                    // Capture this before the delayed revive. The owner-authoritative
                    // root can continue changing while the character is ragdolled.
                    ReviveAsInfected(character, character.transform.position);
                }
                else {
                    character.SetRealColor(TeamColor.White);
                }
            }
            if (killerCharacter != null && killerCharacter.Owner.IsValid) {
                switch (mode) {
                    case MM_Mode.Classic:
                        switch (source) {
                            case DamageType.Melee:
                                LevelsServer.AwardPlayerXPAndGold(killerCharacter.Owner, 15, 5);
                                break;
                            case DamageType.Gun:
                                LevelsServer.AwardPlayerXPAndGold(killerCharacter.Owner, 5, 5);
                                break;
                        }
                        break;
                    default:
                        LevelsServer.AwardPlayerXPAndGold(killerCharacter.Owner, 1, 1);
                        break;
                }
            }
            UpdateGameBarEvent?.Invoke();
        }
        async void ReviveAsInfected(GameCharacter character, Vector3 deathPosition) {
            // HealthComponent sends the death RPC after its server-side OnDied event.
            // Wait one frame so observers process death before the revive RPC, rather
            // than receiving the two lifecycle notifications in reverse order.
            character.SetTeam(new TeamConfig(TeamColor.Red));
            character.GetComponent<RobotColor>().ApplyColor(TeamColor.Green);
            UpdateGameBarEvent?.Invoke();
            await UniTask.Yield();
            await UniTask.WaitForSeconds(3f);
            if (character == null || !character.IsDead || mode != MM_Mode.Infection)
                return;

            if (character.Owner.IsValid)
                ServerTool.Instance.ClearTools(character);
            else
                ServerTool.Instance.DespawnTools(character.NetworkObject);

            if (character is LocalCharacter localCharacter)
                localCharacter.ReviveAtPosition(75, deathPosition);
            else
                character.Revive(75);

            if (character.TryGetComponent(out MM_LocalNPC mmLocalNPC)) {
                character.GetComponent<LocalNPC>().AttackDetectionRadius *= InfectionAttackDetectionMultiplier;
                mmLocalNPC.InitializeAttackState();
            } else
                ServerTool.Instance.SpawnTool(character, ToolEnum.Dagger);

        }
        bool OnTryCollectToolFunc(NetworkBehaviour collectObject, ToolEnum tool) {
            if (collectObject.TryGetComponent<GameCharacter>(out GameCharacter gameCharacter) && gameCharacter.Tools.Length == 0 && gameCharacter.GetTeam().realTeam == TeamColor.Green) {
                if (tool == ToolEnum.Dagger)
                    gameCharacter.SetTeam(new TeamConfig(TeamColor.Red, TeamColor.Red));
                else if (tool == ToolEnum.Pistol)
                    gameCharacter.SetTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                ServerTool.Instance.SpawnTool(collectObject, tool);
                return true;
            }
            return false;
        }
        void SpawnNpcs() {
            for (int i = 0; i < startNPCs; i++) {
                LocalNPC npc = ServerNPC.SpawnNPC(RobotNPC_Prefab, location: (DebugSingleNpc.Value ? new Vector3(1117.56995f, 12.12100029f, 1008.34003f) : null));
                GameCharacter gameCharacter = npc.GetComponent<GameCharacter>();
                //ServerTool.Instance.SpawnTool(gameCharacter, ToolEnum.Dagger);
                gameCharacter.Init(100);
                gameCharacter.InitDefaultEffects();
                switch (mode) {
                    case MM_Mode.NPCsVsPlayers:
                        gameCharacter.SetTeam(new TeamConfig(TeamColor.Red, TeamColor.Red));
                        npc.AttackDetectionRadius *= 3.5f;
                        break;
                    case MM_Mode.Classic:
                        gameCharacter.SetTeam(new TeamConfig(npcRoles[i], TeamColor.None));
                        break;
                    case MM_Mode.Infection:
                        gameCharacter.SetTeam(new TeamConfig(npcRoles[i]));
                        npc.AllowAttackTargetOverrides = false;
                        if (npcRoles[i] == TeamColor.Red) {
                            gameCharacter.GetComponent<RobotColor>().ApplyColor(TeamColor.Green);
                            npc.AttackDetectionRadius *= InfectionAttackDetectionMultiplier;
                        }
                        break;
                    case MM_Mode.Unarmed:
                        gameCharacter.SetTeam(new TeamConfig(TeamColor.Green, TeamColor.Green));
                        break;
                }
                npc.gameObject.AddComponent<MM_LocalNPC>();
                npc.gameObject.AddComponent<MM_NPC>();
                gameCharacter.OnDied += (source, killer) =>
                {
                    SharedOnDied(gameCharacter, source, killer);
                    if (source == DamageType.Fall) {
                        if (FallTest.Value)
                            Time.timeScale = 0f;
                        Debug.LogError("NPC HAS FALLEN!");
                    }

                    GameCharacter killerCharacter = killer as GameCharacter;
                    if (killerCharacter != null && killerCharacter.Owner.IsValid) {
                        int kills = SharedGlobalEvents.GetLeaderboardIndex("Kills");
                        if (kills != -1) // if it exists
                            PlayerData.GetPlayerData(killerCharacter.Owner).leaderboard[kills]++;
                    }
                    UpdateGameBarEvent?.Invoke();
                };
                RandomizeCharacterName(gameCharacter);
            }
        }
        void SpawnPlayerTools(PlayerData player, NetworkObject character) {
            if (mode == MM_Mode.Unarmed)
                ServerTool.Instance.SpawnTool(character, ToolEnum.Dagger);
            switch (player.team.Value.realTeam) {
                case TeamColor.Red:
                    ServerTool.Instance.SpawnTool(character, ToolEnum.Dagger);
                    break;
                case TeamColor.Blue:
                    ToolBaseShared toolBase = ServerTool.Instance.SpawnTool(character, ToolEnum.Pistol, (tool) => {
                        if (mode == MM_Mode.Infection) {
                            tool.maxClipAmmo = 6;
                        }
                    });
                    break;
                default:
                    break;
            }
        }
        void SpawnPlayers() {
            int i = 0;
            foreach (PlayerData player in PlayerData.Players.Values.ToList()) {
                if (!player.Owner.IsValid || !player.Owner.IsAuthenticated)
                    continue;

                // Establish authoritative round state before replacing the lobby
                // character. This keeps every CharacterAdded observer from seeing
                // a stale Ghost/lobby team during the spawn callback.
                switch (mode) {
                    case MM_Mode.NPCsVsPlayers:
                        player.SetPlayerTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                        break;
                    case MM_Mode.Classic:
                        player.SetPlayerTeam(new TeamConfig(i < playerRoles.Count ? playerRoles[i] : TeamColor.Green, TeamColor.None));
                        break;
                    case MM_Mode.Infection:
                        player.SetPlayerTeam(new TeamConfig(TeamColor.Blue));
                        break;
                    case MM_Mode.Unarmed:
                        player.SetPlayerTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                        break;
                }

                LocalCharacter character = ServerPlayerCharacter.Instance.SpawnPlayerCharacter(player.Owner);
                if (character == null)
                    continue;
                RandomizeCharacterName(character);
                SpawnPlayerTools(player, character.NetworkObject);
                string teamName = GetTeamName(player.team.Value.realTeam);
                ServerChat.SendSystemMessage(player.Owner, new($"Welcome to the {teamName} team!", SystemMessageSource.CustomMessage));
                i++;
            }
        }
        void RandomizeCharacterName(GameCharacter gameCharacter) {
            if (alienNames == null)
                alienNames = ServerBootStrap.universeCfg.LoadText("alien_names").Replace("\r", "").Split("\n");
            gameCharacter.DisplayName = alienNames[UnityEngine.Random.Range(0, alienNames.Length)];
        }
        string GetTeamName(TeamColor team) {
            return team switch
            {
                TeamColor.Red => "Murderer",
                TeamColor.Green => "Innocent",
                TeamColor.Blue => "Sheriff",
                _ => "Unknown"
            };
        }
        string GetWinnerName(TeamColor team) {
            return team switch {
                TeamColor.Red => mode == MM_Mode.Infection ? "Infected" : "Murderers",
                TeamColor.Green => "Innocents",
                TeamColor.Blue => "Sheriffs",
                _ => "Nobody"
            };
        }
        int GetNPCCount() => GameObject.FindGameObjectsWithTag("NPC").Count();
        int GetPlayerCount() => GameObject.FindGameObjectsWithTag("Player").Count();
        int GetTeamCount(TeamColor team) => GameCharacter.TeamToCharacter.TryGetValue(team, out var characters) ? characters.Count : 0;
        void AssignInfectionRoles() {
            npcRoles = Enumerable.Repeat(TeamColor.Green, startNPCs).ToList();
            playerRoles = Enumerable.Repeat(TeamColor.Blue, startPlayers).ToList();

            int murdererTarget = Mathf.RoundToInt(startNPCs * MM_Roles.murdererBaseChance);
            murdererTarget = Mathf.Max(murdererTarget, MM_Roles.minMurderers);
            murdererTarget = Mathf.Min(murdererTarget, Mathf.Max(MM_Roles.minMurderers, Mathf.FloorToInt(startNPCs * MM_Roles.murdererMaxRatio)));
            murdererTarget = Mathf.Min(murdererTarget, startNPCs);

            foreach (int index in Enumerable.Range(0, startNPCs).OrderBy(_ => UnityEngine.Random.value).Take(murdererTarget))
                npcRoles[index] = TeamColor.Red;

            startMurd = murdererTarget;
            startSheriff = startPlayers;
            startInnocent = startNPCs - murdererTarget;
        }
        bool AreAllPlayersInfected() {
            List<PlayerData> activePlayers = PlayerData.Players.Values
                .Where(player => player.Owner.IsValid && player.Owner.IsAuthenticated)
                .ToList();
            return activePlayers.Count > 0
                && activePlayers.All(player => new[] { TeamColor.Red, TeamColor.White }.Contains(player.team.Value.realTeam));
        }
        TeamColor GetInfectionWinnerTeam() {
            if (GetTeamCount(TeamColor.Red) == 0)
                return TeamColor.Blue;
            if (AreAllPlayersInfected())
                return TeamColor.Red;
            return TeamColor.None;
        }
        TeamColor GetWinnerTeam(bool isRunning) {
            if (GetTeamCount(TeamColor.Red) == 0) {
                return TeamColor.Blue;
            }
            if (GetTeamCount(TeamColor.Blue) == 0 && GetTeamCount(TeamColor.Green) == 0) {
                return TeamColor.Red;
            }
            if (!isRunning) {
                return TeamColor.Green;
            }
            return TeamColor.None;
        }
        bool UpdateInGameBar(int durationLeft, bool interrupted) {
            gameDurationLeft = durationLeft;
            switch (mode) {
                case MM_Mode.NPCsVsPlayers:
                case MM_Mode.Unarmed:
                    int npcsLeft = GetNPCCount();
                    SetTopMessage(string.Format(mode == MM_Mode.Unarmed?"Civilians Left: {0} ({1})":"NPC Killers Left: {0} ({1})", npcsLeft, durationLeft));
                    return npcsLeft > 0;
                case MM_Mode.Classic:
                    SetTopMessage($"Mystery In Progress ({durationLeft})");
                    return GetWinnerTeam(GetPlayerCount() > 0) == TeamColor.None;
                case MM_Mode.Infection:
                    SetTopMessage($"Infection In Progress ({durationLeft})");
                    return GetInfectionWinnerTeam() == TeamColor.None;
                default:
                    Debug.LogError($"Unknown mode: {mode}");
                    return false;
            }
        }
        async void SpawnCoinLoop(CancellationToken token) {
            do {
                ServerCoin.SpawnCoin(null);
                await UniTask.Delay(
                    TimeSpan.FromSeconds(MathHelper.Range(5f, 15f)),
                    cancellationToken: token
                ).SuppressCancellationThrow();
            } while (!token.IsCancellationRequested);
        }
        protected override async UniTask StartAsync(CancellationToken token){
            await base.StartAsync(token);

            if (!DebugNoIntermission.Value) {
                mode = (MM_Mode)await ServerVote.StartVote(VoteEnum.MM_VoteMode, 15f, token);
                //await base.Intermission(DebugTimerInfinite.Value ? Int32.MaxValue : DebugTimerSpeedUp.Value ? 1 : 5, token);
            }
            SetLeaderboardEnabled("Kills", mode != MM_Mode.Classic);
            startNPCs = 30;
            startNPCs = FallTest.Value ? 700 : Mathf.RoundToInt((DebugSingleNpc.Value ? 1 : startNPCs) * SpawnMultiplier);
            startPlayers = PlayerData.Players.Count;
            if (mode == MM_Mode.Classic) {
                GetComponent<MM_Roles>().AssignRoles(startNPCs, startPlayers, out npcRoles, out playerRoles, out startMurd, out startSheriff, out startInnocent);
            } else if (mode == MM_Mode.Infection) {
                AssignInfectionRoles();
            }

            SpawnNpcs();
            SpawnPlayers();
            base.SetGlobalInvul(false);
            int gameTime = mode == MM_Mode.Classic ? 180 : 90;
            gameDurationLeft = DebugTimerSpeedUp.Value ? 10 : gameTime;
            gameInProgress = true;
            using CancellationTokenSource coinCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            SpawnCoinLoop(coinCts.Token);
            await base.CustomTimerCountdown(
                DebugTimerSpeedUp.Value ? 10 : gameTime,
                UpdateInGameBar,
                interrupt => UpdateGameBarEvent += interrupt,
                interrupt => UpdateGameBarEvent -= interrupt,
                token);
            coinCts.Cancel();
            ServerCoin.ClearAllCoins();
            base.SetGlobalInvul(true);
            switch (mode) {
                case MM_Mode.Unarmed:
                case MM_Mode.NPCsVsPlayers:
                    List<PlayerData> scores = base.GetLeaderboardWinner("Kills");
                    if (scores.Count > 0) {
                        ServerChat.SendSystemMessage(new($"{scores[0].username.Value} wins with {scores[0].leaderboard[SharedGlobalEvents.GetLeaderboardIndex("Kills")]} kills!", RyanAssets.Shared.Declarations.SystemMessageSource.CustomMessage));
                    }
                    await TimerCountdown($"Game over: {GetNPCCount()}/{startNPCs} NPCs survived! ({{0}})", DebugTimerSpeedUp.Value ? 2 : 5, token);
                    break;
                case MM_Mode.Classic:
                    string winnerName = GetWinnerName(GetWinnerTeam(false));
                    await TimerCountdown($"{winnerName} Win: {GetTeamCount(TeamColor.Red)}/{startMurd} Murderers, {GetTeamCount(TeamColor.Blue)}/{startSheriff} Sheriffs, {GetTeamCount(TeamColor.Green)}/{startInnocent} Innocents remaining! ({{0}})", DebugTimerSpeedUp.Value ? 2 : 10, token);
                    break;
                case MM_Mode.Infection:
                    TeamColor infectionWinner = GetInfectionWinnerTeam();
                    if (infectionWinner == TeamColor.None)
                        infectionWinner = TeamColor.Blue;
                    await TimerCountdown($"{GetWinnerName(infectionWinner)} Win! ({{0}})", DebugTimerSpeedUp.Value ? 2 : 10, token);
                    break;
            }
        }
        protected override void Stop() {
            base.Stop();
            gameInProgress = false;
            gameDurationLeft = 0;
        }
        protected override void Reset() {
            base.Reset();
            ServerNPC.ClearAllNPC();
            ServerCoin.ClearAllCoins();

            // Tools are stored in PlayerData and are copied into every newly spawned
            // character. Clear the round inventory before respawning lobby characters,
            // otherwise an infection dagger survives into the next round.
            foreach (PlayerData player in PlayerData.Players.Values)
                ServerTool.Instance.ClearTools(player.Owner);

            SetPlayerTeams(new TeamConfig(TeamColor.White));
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            SetLeaderboardEnabled("Kills", false);
        }
        protected override void OnDestroy() {
            //ServerPlayerCharacter.CharacterAdded -= OnCharacterAdded;
            if (ServerPlayerCharacter.CanSpawnFunction == CanSpawnFunction)
                ServerPlayerCharacter.CanSpawnFunction = null;
            if (ServerPlayerCharacter.SpawnLocationFunction == SpawnLocationFunction)
                ServerPlayerCharacter.SpawnLocationFunction = null;
            base.OnDestroy();
        }
        public static void RefreshNPCSpeeds() {
            foreach (GameObject obj in GameObject.FindGameObjectsWithTag("NPC")) {
                if (obj.TryGetComponent(out LocalNPC npc)) {
                    npc.UpdateSpeed();
                }
            }
        }
    }
}
