using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Characters.Server;
using UnityEngine;
using RyanAssets.DataService;
using System.Collections.Generic;
using RyanAssets.Shared.Player;
using System.Linq;
using System;

namespace Universes.murder_mystery.Server {
    public class MM_ServerRunner : MonoBehaviour {
        [SerializeField]
        GameObject RobotNPC_Prefab;
        [SerializeField]
        bool DebugSingleNpc, DebugMotionlessNpc;
        [SerializeField]
        bool DebugTimerSpeedUp;
        Action UpdateGameBarEvent;
        void Awake(){
            ServerPlayerCharacter.OnPlayerCharacterAdded += OnCharacterAdded;
            ServerPlayerCharacter.CanSpawnFunction = CanSpawnFunction;
            SharedGlobalEvents.Instance.LeaderboardHeaders.AddRange(new[] { "Kills" });
        }
        bool CanSpawnFunction(NetworkConnection player) {
            return true;
        }
        void OnCharacterAdded(NetworkConnection player, LocalCharacter character){
            character.transform.position = DebugSingleNpc? new Vector3(1120.56995f, -8.12100029f, 1008.34003f): ServerPathfinding.GetRandomPosition(); // Random position
            //new Vector3(1120.56995f, -8.12100029f, 1008.34003f);
            PlayerData.GetPlayerData(player).leaderboard.AddRange(new[] { 0 });
            //character.transform.localScale = 0.7f * Vector3.one;  
            //character.GetComponent<CharacterScaler>().SetScale(0.7f * Vector3.one);
        }
        void SpawnNpcs() {
            for (int i = 0; i < (DebugSingleNpc ? 1 : 30); i++) {
                LocalNPC obj = ServerNPC.SpawnNPC(RobotNPC_Prefab, location: (DebugSingleNpc ? new Vector3(1117.56995f, -8.12100029f, 1008.34003f) : null));
                GameCharacter gameCharacter = obj.GetComponent<GameCharacter>();
                gameCharacter.OnDied += (source, killer) => {
                    if (killer && killer.Owner != null) {
                        int kills = SharedGlobalEvents.GetLeaderboardIndex("Kills");
                        if (kills != -1)
                            PlayerData.GetPlayerData(killer.Owner).leaderboard[kills]++;
                    }
                    UpdateGameBarEvent?.Invoke();
                };
            }
        }
        bool UpdateInGameBar(int durationLeft, bool interrupted) {
            int npcsLeft = GameObject.FindGameObjectsWithTag("NPC").Count();
            if (npcsLeft > 0)
                SharedGlobalEvents.Instance.TopMessage = $"Civilians Left: {npcsLeft} ({durationLeft})";
            return npcsLeft > 0;
        }
        async void Start(){
            await ServerRunner.WaitForSceneAsync("murder_mystery_start");
            if (DebugMotionlessNpc) {
                LocalNPC.FleeSpeedMultiplier = 0f;
                LocalNPC.WalkSpeedMultiplier = 0f;
            }
            
            while(true) {
                await ServerRunner.Intermission(DebugTimerSpeedUp? 1: 5);

                SpawnNpcs();
                await ServerRunner.CustomTimerCountdown(DebugTimerSpeedUp ? 10 : 90, UpdateInGameBar, interrupt => UpdateGameBarEvent += interrupt);

                List<PlayerData> scores = ServerRunner.GetLeaderboardWinner("Kills");
                if (scores.Count > 0) {
                    ServerChat.SendSystemMessage(new($"{scores[0].username.Value} wins with {scores[0].leaderboard[SharedGlobalEvents.GetLeaderboardIndex("Kills")]} kills!", RyanAssets.Shared.Declarations.SystemMessageSource.CustomMessage));
                }

                await ServerRunner.TimerCountdown("Game Over! ({0})", DebugTimerSpeedUp ? 2 : 5);
                ServerNPC.ClearAllNPC();
                ServerRunner.ResetLeaderboardData();
            }
        }
    }
}
