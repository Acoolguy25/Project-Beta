using Cysharp.Threading.Tasks;
using EasyDebug.Shared;
using FishNet.Connection;
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
        SharedVoteOption[] modeVoteOptions;
        
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
            PlayerData.OnPlayerAdded += OnPlayerAdded;
            ServerPlayerCharacter.OnPlayerCharacterAdded += OnCharacterAdded;
            ServerPlayerCharacter.CanSpawnFunction = CanSpawnFunction;
            SharedGlobalEvents.Instance.LeaderboardHeaders.Clear();
            SharedGlobalEvents.Instance.LeaderboardHeaders.AddRange(new[] { "Kills" });

            alienNames = ServerBootStrap.universeCfg.LoadText("alien_names").Split("\n");

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
        void OnPlayerAdded(NetworkConnection player, PlayerData data) {
            data.leaderboard.AddRange(new[] { 0 });
            //data.tools.AddRange(new[] { ToolEnum.Dagger });
            ServerTool.Instance.AddTool(player, ToolEnum.Dagger);
            ServerTool.Instance.AddTool(player, ToolEnum.Pistol);
            
            //ServerTool.tool
            data.SetPlayerTeam(new TeamConfig(TeamColor.Green, TeamColor.Green));
        }
        void OnCharacterAdded(NetworkConnection player, LocalCharacter character){
            //PlayerData data = PlayerData.GetPlayerData(player);
            character.Init(100);
            character.InitDefaultEffects();
            character.transform.position = DebugSingleNpc.Value? new Vector3(1120.56995f, 12.12100029f, 1008.34003f): ServerPathfinding.GetRandomPosition(); // Random position
            //new Vector3(1120.56995f, -8.12100029f, 1008.34003f);
            //character.transform.localScale = 0.7f * Vector3.one;  
            //character.GetComponent<CharacterScaler>().SetScale(0.7f * Vector3.one);
            //character.SetTeam(new TeamConfig(TeamColor.Green, TeamColor.Green));
            RandomizeCharacterName(character);
        }
        void SpawnNpcs() {
            startNPCs = FallTest.Value? 700: Mathf.RoundToInt((DebugSingleNpc.Value ? 1 : 30) * SpawnMultiplier);
            for (int i = 0; i < startNPCs; i++) {
                LocalNPC npc = ServerNPC.SpawnNPC(RobotNPC_Prefab, location: (DebugSingleNpc.Value ? new Vector3(1117.56995f, 12.12100029f, 1008.34003f) : null));
                GameCharacter gameCharacter = npc.GetComponent<GameCharacter>();
                //ServerTool.Instance.SpawnTool(gameCharacter, ToolEnum.Dagger);
                gameCharacter.Init(100);
                gameCharacter.InitDefaultEffects();
                gameCharacter.SetTeam(new TeamConfig(TeamColor.Red, TeamColor.Red));
                npc.gameObject.AddComponent<MM_LocalNPC>();
                gameCharacter.OnDied += (source, killer) =>
                {
                    if (killer && killer.Owner.IsValid) {
                        int kills = SharedGlobalEvents.GetLeaderboardIndex("Kills");
                        if (kills != -1)
                            PlayerData.GetPlayerData(killer.Owner).leaderboard[kills]++;
                        LevelsServer.AwardPlayerXPAndGold(killer.Owner, 5);
                    }
                    //if (GameObject.FindGameObjectsWithTag("NPC").Count() == 0)
                    UpdateGameBarEvent?.Invoke();
                    if (source == DamageSource.Fall && FallTest.Value) {
                        Time.timeScale = 0f;
                        Debug.LogError("NPC HAS FALLEN!");
                    }
                };
                RandomizeCharacterName(gameCharacter);
            }
        }
        void RandomizeCharacterName(GameCharacter gameCharacter) {
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
                    int intermissionDuration = DebugTimerInfinite.Value ? Int32.MaxValue : DebugTimerSpeedUp.Value ? 1 : 15;
                    mode = (MM_Mode)await VoteCountdown("Voting for new mode", "Pick your favorite mode", intermissionDuration, modeVoteOptions, token);
                    //await base.Intermission(DebugTimerInfinite.Value ? Int32.MaxValue : DebugTimerSpeedUp.Value ? 1 : 5, token);
                }

                SpawnNpcs();
                base.SetGlobalInvul(false);
                await base.CustomTimerCountdown(DebugTimerSpeedUp.Value ? 10 : 90, UpdateInGameBar, interrupt => UpdateGameBarEvent += interrupt, token);

                List<PlayerData> scores = base.GetLeaderboardWinner("Kills");
                if (scores.Count > 0) {
                    ServerChat.SendSystemMessage(new($"{scores[0].username.Value} wins with {scores[0].leaderboard[SharedGlobalEvents.GetLeaderboardIndex("Kills")]} kills!", RyanAssets.Shared.Declarations.SystemMessageSource.CustomMessage));
                }

                base.SetGlobalInvul(true);
                await TimerCountdown($"Game over: {GetNPCCount()}/{startNPCs} survivors! ({{0}})", DebugTimerSpeedUp.Value ? 2 : 5, token);
                ServerNPC.ClearAllNPC();
                base.ResetLeaderboardData();
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
