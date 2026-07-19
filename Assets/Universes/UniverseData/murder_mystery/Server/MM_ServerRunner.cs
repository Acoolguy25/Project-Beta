using Cysharp.Threading.Tasks;
using EasyDebug.Shared;
using FishNet.Connection;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Levels.Server;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace Universes.murder_mystery.Server {
    public class MM_ServerRunner : ServerRunner {
        [SerializeField]
        GameObject RobotNPC_Prefab;
        [SerializeField]
        DebugBool DebugSingleNpc, DebugMotionlessNpc;
        [SerializeField]
        DebugBool DebugTimerSpeedUp, DebugTimerInfinite;
        public static float SpawnMultiplier;
        public static bool ForceEndGame;
        public static Action UpdateGameBarEvent;
        int startNPCs;
        protected override void Awake(){
            base.Awake();
            UpdateGameBarEvent = null;
            ForceEndGame = false;
            SpawnMultiplier = 1f;
            PlayerData.OnPlayerAdded += OnPlayerAdded;
            ServerPlayerCharacter.OnPlayerCharacterAdded += OnCharacterAdded;
            ServerPlayerCharacter.CanSpawnFunction = CanSpawnFunction;
            SharedGlobalEvents.Instance.LeaderboardHeaders.Clear();
            SharedGlobalEvents.Instance.LeaderboardHeaders.AddRange(new[] { "Kills" });
        }
        bool CanSpawnFunction(NetworkConnection player) {
            return true;
        }
        void OnPlayerAdded(NetworkConnection player, PlayerData data) {
            PlayerData.GetPlayerData(player).leaderboard.AddRange(new[] { 0 });
            //data.tools.AddRange(new[] { ToolEnum.Dagger });
            ServerTool.Instance.AddTool(player, ToolEnum.Dagger);
            //ServerTool.tool
        }
        void OnCharacterAdded(NetworkConnection player, LocalCharacter character){
            character.transform.position = DebugSingleNpc.Value? new Vector3(1120.56995f, -8.12100029f, 1008.34003f): ServerPathfinding.GetRandomPosition(); // Random position
            //new Vector3(1120.56995f, -8.12100029f, 1008.34003f);
            //character.transform.localScale = 0.7f * Vector3.one;  
            //character.GetComponent<CharacterScaler>().SetScale(0.7f * Vector3.one);
        }
        void SpawnNpcs() {
            startNPCs = Mathf.RoundToInt((DebugSingleNpc.Value ? 1 : 30) * SpawnMultiplier);
            for (int i = 0; i < startNPCs; i++) {
                LocalNPC npc = ServerNPC.SpawnNPC(RobotNPC_Prefab, location: (DebugSingleNpc.Value ? new Vector3(1117.56995f, -8.12100029f, 1008.34003f) : null));
                GameCharacter gameCharacter = npc.GetComponent<GameCharacter>();
                gameCharacter.OnDied += (source, killer) => {
                    if (killer && killer.Owner != null) {
                        int kills = SharedGlobalEvents.GetLeaderboardIndex("Kills");
                        if (kills != -1)
                            PlayerData.GetPlayerData(killer.Owner).leaderboard[kills]++;
                        LevelsServer.AwardPlayerXPAndGold(killer.Owner, 5);
                    }
                    //if (GameObject.FindGameObjectsWithTag("NPC").Count() == 0)
                    UpdateGameBarEvent?.Invoke();
                };
                npc.FleeTargets = MM_NPC.characters.ToArray();
            }
        }
        int GetNPCCount() => GameObject.FindGameObjectsWithTag("NPC").Count();
        bool UpdateInGameBar(int durationLeft, bool interrupted) {
            int npcsLeft = GetNPCCount();
            SharedGlobalEvents.Instance.TopMessage = $"Civilians Left: {npcsLeft} ({durationLeft})";
            return npcsLeft > 0;
        }
        protected override async UniTask StartAsync(CancellationToken token){
            await base.StartAsync(token);
            base.SetTeamKillEnabled(true);
            if (DebugMotionlessNpc.Value) {
                LocalNPC.FleeSpeedMultiplier = 0f;
                LocalNPC.WalkSpeedMultiplier = 0f;
            }
            
            while(true) {
                await base.Intermission(DebugTimerInfinite.Value? Int32.MaxValue: DebugTimerSpeedUp.Value ? 1: 5, token);

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
    }
}
