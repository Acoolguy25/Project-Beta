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
        Unarmed
    }
    public class MM_ServerRunner : ServerRunner {
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
        int startNPCs, startPlayers;
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
            ServerPlayerCharacter.CharacterAdded += OnCharacterAdded;
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
            return !gameInProgress || mode != MM_Mode.Classic;
        }
        Vector3 SpawnLocationFunction(NetworkConnection player) {
            return DebugSingleNpc.Value ? new Vector3(1120.56995f, 12.12100029f, 1008.34003f) : ServerPathfinding.GetRandomPosition();
        }
        protected void OnSetPlayerTeam(PlayerData player, TeamConfig teamConfig) {
            switch (teamConfig.team) {
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
                    player.SetPlayerTeam(new TeamConfig(TeamColor.Ghost, TeamColor.Ghost));
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
            }
            player.walkSpeed.Value = 16f;
            player.sprintSpeed.Value = 21f;
        }
        void OnCharacterAdded(LocalCharacter character){
            character.Init(100);
            character.InitDefaultEffects();
            
            character.OnDied += (source, killer) => SharedOnDied(character, source, killer);
            //new Vector3(1120.56995f, -8.12100029f, 1008.34003f);
            //character.transform.localScale = 0.7f * Vector3.one;  
            //character.GetComponent<CharacterScaler>().SetScale(0.7f * Vector3.one);
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
            if (mode == MM_Mode.Classic) {
                GameCharacter killerCharacter = killer as GameCharacter;
                if (killerCharacter != null) {
                    if (killerCharacter.Owner.IsValid && source == DamageType.Gun && character.GetTeam().team != TeamColor.Red) {
                        //ServerTool.Instance.DespawnTool(killerCharacter.Owner, ToolEnum.Pistol);
                        ToolBaseShared tool = ServerTool.Instance.GetTool(killerCharacter.Owner, ToolEnum.Pistol);
                        tool.UpdateServerCooldown(20f);
                        //tool.
                    }
                    if (killerCharacter.GetTeam().team == TeamColor.Red) {
                        killerCharacter.HealMaxHealth(character.Owner.IsValid ? 60 : 20);
                    }
                }
                if (character.Tools.Any((tool) => tool.toolEnum == ToolEnum.Pistol))
                    ServerTool.Instance.SpawnFloatingTool(ToolEnum.Pistol, character.transform.position + character.transform.lossyScale.y/2 * Vector3.up).OnToolCollectedFunc = OnTryCollectToolFunc;
                if (character.Tools.Any((tool) => tool.toolEnum == ToolEnum.Dagger))
                    ServerTool.Instance.SpawnFloatingTool(ToolEnum.Dagger, character.transform.position + character.transform.lossyScale.y / 2 * Vector3.up).OnToolCollectedFunc = OnTryCollectToolFunc;
                if (character.GetTeam().team == TeamColor.Blue) {
                    if (GetTeamCount(TeamColor.Blue) == 0) {
                        foreach (GameCharacter gameCharacter in GameCharacter.TeamToCharacter[TeamColor.Red]) {
                            if (gameCharacter.TryGetComponent<LocalNPC>(out LocalNPC localNPC)) {
                                localNPC.AttackDetectionRadius *= 2f;
                            }
                        }
                    }
                }
            }
            UpdateGameBarEvent?.Invoke();
        }
        bool OnTryCollectToolFunc(NetworkBehaviour collectObject, ToolEnum tool) {
            if (collectObject.TryGetComponent<GameCharacter>(out GameCharacter gameCharacter) && gameCharacter.Tools.Length == 0 && gameCharacter.GetTeam().team == TeamColor.Green) {
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
                        switch (source) {
                            case DamageType.Melee:
                                LevelsServer.AwardPlayerXPAndGold(killerCharacter.Owner, 15, 5);
                                break;
                            case DamageType.Gun:
                                LevelsServer.AwardPlayerXPAndGold(killerCharacter.Owner, 5, 5);
                                break;
                        }
                    }
                    UpdateGameBarEvent?.Invoke();
                };
                RandomizeCharacterName(gameCharacter);
            }
        }
        void SpawnPlayerTools(PlayerData player, NetworkObject character) {
            if (mode == MM_Mode.Unarmed)
                ServerTool.Instance.SpawnTool(character, ToolEnum.Dagger);
            switch (player.team.Value.team) {
                case TeamColor.Red:
                    ServerTool.Instance.SpawnTool(character, ToolEnum.Dagger);
                    break;
                case TeamColor.Blue:
                    ServerTool.Instance.SpawnTool(character, ToolEnum.Pistol);
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
                    case MM_Mode.Unarmed:
                        player.SetPlayerTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                        break;
                }

                LocalCharacter character = ServerPlayerCharacter.Instance.SpawnPlayerCharacter(player.Owner);
                if (character == null)
                    continue;
                RandomizeCharacterName(character);
                SpawnPlayerTools(player, character.NetworkObject);
                string teamName = GetTeamName(player.team.Value.team);
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
        int GetNPCCount() => GameObject.FindGameObjectsWithTag("NPC").Count();
        int GetPlayerCount() => GameObject.FindGameObjectsWithTag("Player").Count();
        int GetTeamCount(TeamColor team) => GameCharacter.TeamToCharacter.TryGetValue(team, out var characters) ? characters.Count : 0;
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
            switch (mode) {
                case MM_Mode.NPCsVsPlayers:
                case MM_Mode.Unarmed:
                    int npcsLeft = GetNPCCount();
                    SetTopMessage(string.Format(mode == MM_Mode.Unarmed?"Civilians Left: {0} ({1})":"NPC Killers Left: {0} ({1})", npcsLeft, durationLeft));
                    return npcsLeft > 0;
                case MM_Mode.Classic:
                    SetTopMessage($"Mystery In Progress ({durationLeft})");
                    return GetWinnerTeam(GetPlayerCount() > 0) == TeamColor.None;
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
            }

            SpawnNpcs();
            SpawnPlayers();
            base.SetGlobalInvul(false);
            int gameTime = mode == MM_Mode.Classic ? 180 : 90;
            gameInProgress = true;
            CancellationTokenSource coinCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            SpawnCoinLoop(coinCts.Token);
            await base.CustomTimerCountdown(DebugTimerSpeedUp.Value ? 10 : gameTime, UpdateInGameBar, interrupt => UpdateGameBarEvent += interrupt, token);
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
                    string winnerTeam = GetTeamName(GetWinnerTeam(false));
                    await TimerCountdown($"{winnerTeam}s Win: {GetTeamCount(TeamColor.Red)}/{startMurd} Murderers, {GetTeamCount(TeamColor.Blue)}/{startSheriff} Sheriffs, {GetTeamCount(TeamColor.Green)}/{startInnocent} Innocents remaining! ({{0}})", DebugTimerSpeedUp.Value ? 2 : 10, token);
                    break;
            }

            Restart();
        }
        protected override void Stop() {
            base.Stop();
            gameInProgress = false;
        }
        protected override void Reset() {
            base.Reset();
            ServerNPC.ClearAllNPC();
            ServerCoin.ClearAllCoins();
            SetPlayerTeams(new TeamConfig(TeamColor.Ghost));
            ServerPlayerCharacter.Instance.SpawnAllPlayerCharacters();
            SetLeaderboardEnabled("Kills", false);
        }
        protected override void OnDestroy() {
            ServerPlayerCharacter.CharacterAdded -= OnCharacterAdded;
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
