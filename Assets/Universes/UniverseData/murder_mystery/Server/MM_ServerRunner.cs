using Cysharp.Threading.Tasks;
using EasyDebug.Shared;
using FishNet.Connection;
using FishNet.Object;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Levels.Server;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Player;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Universes.UniverseData.murder_mystery.Server;
using static UnityEngine.Analytics.IAnalytic;

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
        DebugBool DebugNoIntermission, DebugTimerSpeedUp, DebugTimerInfinite;
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
        string[] alienNames;
        int startNPCs;
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
            ServerPlayerCharacter.OnPlayerCharacterAdded += OnCharacterAdded;
            ServerPlayerCharacter.CanSpawnFunction = CanSpawnFunction;
            


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
            return true;
        }
        protected override void OnPlayerAdded(NetworkConnection player, PlayerData data) {
            base.OnPlayerAdded(player, data);
            //data.leaderboard.AddRange(new[] { 0 });
            //data.tools.AddRange(new[] { ToolEnum.Dagger });
            //ServerTool.Instance.AddTool(player, ToolEnum.Dagger);
            //ServerTool.Instance.AddTool(player, ToolEnum.Pistol);
            
            //ServerTool.tool
            data.SetPlayerTeam(new TeamConfig(TeamColor.None, TeamColor.None));
        }
        void OnCharacterAdded(NetworkConnection player, LocalCharacter character){
            //PlayerData data = PlayerData.GetPlayerData(player);
            character.Init(100);
            character.InitDefaultEffects();
            character.transform.position = DebugSingleNpc.Value? new Vector3(1120.56995f, 12.12100029f, 1008.34003f): ServerPathfinding.GetRandomPosition(); // Random position
            character.OnDied += (source, killer) => SharedOnDied(character, source, killer);
            //new Vector3(1120.56995f, -8.12100029f, 1008.34003f);
            //character.transform.localScale = 0.7f * Vector3.one;  
            //character.GetComponent<CharacterScaler>().SetScale(0.7f * Vector3.one);
            //character.SetTeam(new TeamConfig(TeamColor.Green, TeamColor.Green));
            RandomizeCharacterName(character);
        }
        void SharedOnDied(GameCharacter character, DamageSource source, NetworkObject killer) {
            if (character.Owner.IsValid) {
                GameCharacter killerCharacter = killer?.GetComponent<GameCharacter>();
                if (source == DamageSource.Gun && killerCharacter.GetTeam().team != TeamColor.Red) {
                    ServerTool.Instance.RemoveTool(killerCharacter.Owner, ToolEnum.Pistol);
                }
            }
            UpdateGameBarEvent?.Invoke();
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
                        break;
                    case MM_Mode.Classic:
                        gameCharacter.SetTeam(new TeamConfig(npcRoles[i], TeamColor.None));
                        break;
                    case MM_Mode.Unarmed:
                        gameCharacter.SetTeam(new TeamConfig(TeamColor.Green, TeamColor.Green));
                        break;
                }
                npc.gameObject.AddComponent<MM_LocalNPC>();
                gameCharacter.OnDied += (source, killer) => SharedOnDied(gameCharacter, source, killer);
                gameCharacter.OnDied += (source, killer) =>
                {
                    if (source == DamageSource.Fall) {
                        if (FallTest.Value)
                            Time.timeScale = 0f;
                        Debug.LogError("NPC HAS FALLEN!");
                    }

                    if (killer && killer.Owner.IsValid) {
                        int kills = SharedGlobalEvents.GetLeaderboardIndex("Kills");
                        if (kills != -1) // if it exists
                            PlayerData.GetPlayerData(killer.Owner).leaderboard[kills]++;
                        switch (source) {
                            case DamageSource.Melee:
                                LevelsServer.AwardPlayerXPAndGold(killer.Owner, 15, 5);
                                break;
                            case DamageSource.Gun:
                                LevelsServer.AwardPlayerXPAndGold(killer.Owner, 5, 5);
                                break;
                        }
                    }
                    UpdateGameBarEvent?.Invoke();
                };
                RandomizeCharacterName(gameCharacter);
            }
        }
        void SpawnPlayers() {
            int i = 0;
            foreach (PlayerData player in PlayerData.Players.Values) {
                LocalCharacter character = ServerPlayerCharacter.Instance.SpawnPlayerCharacter(player.Owner);
                switch (mode) {
                    case MM_Mode.NPCsVsPlayers:
                        player.SetPlayerTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                        break;
                    case MM_Mode.Classic:
                        player.SetPlayerTeam(new TeamConfig(i < playerRoles.Count ? playerRoles[i] : TeamColor.Green, TeamColor.None));
                        break;
                    case MM_Mode.Unarmed:
                        player.SetPlayerTeam(new TeamConfig(TeamColor.Blue, TeamColor.Blue));
                        ServerTool.Instance.AddTool(player.Owner, ToolEnum.Dagger);
                        break;
                }
                switch (player.team.Value.team) {
                    case TeamColor.Red:
                        ServerTool.Instance.AddTool(player.Owner, ToolEnum.Dagger);
                        break;
                    case TeamColor.Blue:
                        ServerTool.Instance.AddTool(player.Owner, ToolEnum.Pistol);
                        break;
                    default:
                        break;
                }
                string teamName = player.team.Value.team switch
                {
                    TeamColor.Red => "Murderer",
                    TeamColor.Green => "Innocent",
                    TeamColor.Blue => "Sheriff"
                };
                ServerChat.SendSystemMessage(player.Owner, new($"Welcome to the {teamName} team!", SystemMessageSource.CustomMessage));
                i++;
            }
        }
        void RandomizeCharacterName(GameCharacter gameCharacter) {
            if (alienNames == null)
                alienNames = ServerBootStrap.universeCfg.LoadText("alien_names").Split("\n");
            gameCharacter.DisplayName.Value = alienNames[UnityEngine.Random.Range(0, alienNames.Length)];
        }
        int GetNPCCount() => GameObject.FindGameObjectsWithTag("NPC").Count();
        bool UpdateInGameBar(int durationLeft, bool interrupted) {
            int npcsLeft = GetNPCCount();
            SharedGlobalEvents.Instance.TopMessage = $"Civilians Left: {npcsLeft} ({durationLeft})";
            return npcsLeft > 0;
        }
        protected override async UniTask StartAsync(CancellationToken token){
            await base.StartAsync(token);
            
            
            while(true) {
                if (!DebugNoIntermission.Value) {
                    int intermissionDuration = DebugTimerInfinite.Value ? 9999 : DebugTimerSpeedUp.Value ? 1 : 15;
                    mode = (MM_Mode)await ServerVote.StartVote(VoteEnum.MM_VoteMode, intermissionDuration, token);
                    //await base.Intermission(DebugTimerInfinite.Value ? Int32.MaxValue : DebugTimerSpeedUp.Value ? 1 : 5, token);
                }
                SetLeaderboardEnabled("Kills", mode != MM_Mode.Classic);
                if (mode == MM_Mode.Classic) {
                    startNPCs = 30;
                    startNPCs = FallTest.Value ? 700 : Mathf.RoundToInt((DebugSingleNpc.Value ? 1 : startNPCs) * SpawnMultiplier);
                    GetComponent<MM_Roles>().AssignRoles(startNPCs, PlayerData.Players.Count, out npcRoles, out playerRoles);
                }

                SpawnNpcs();
                SpawnPlayers();
                base.SetGlobalInvul(false);
                await base.CustomTimerCountdown(DebugTimerSpeedUp.Value ? 10 : 90, UpdateInGameBar, interrupt => UpdateGameBarEvent += interrupt, token);

                if (mode != MM_Mode.Classic) {
                    List<PlayerData> scores = base.GetLeaderboardWinner("Kills");
                    if (scores.Count > 0) {
                        ServerChat.SendSystemMessage(new($"{scores[0].username.Value} wins with {scores[0].leaderboard[SharedGlobalEvents.GetLeaderboardIndex("Kills")]} kills!", RyanAssets.Shared.Declarations.SystemMessageSource.CustomMessage));
                    }
                }

                base.SetGlobalInvul(true);
                await TimerCountdown($"Game over: {GetNPCCount()}/{startNPCs} survivors! ({{0}})", DebugTimerSpeedUp.Value ? 2 : 5, token);
                ServerNPC.ClearAllNPC();
                SetLeaderboardEnabled("Kills", false);
            }
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
