using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Characters.Server;
using UnityEngine;

namespace Universes.murder_mystery.Server {
    public class MM_ServerRunner : MonoBehaviour {
        [SerializeField]
        GameObject RobotNPC_Prefab;
        [SerializeField]
        bool Debug = true;
        void Awake(){
            ServerPlayerCharacter.OnPlayerCharacterAdded += OnCharacterAdded;
            ServerPlayerCharacter.CanSpawnFunction = CanSpawnFunction;
        }
        bool CanSpawnFunction(NetworkConnection player) {
            return true;
        }
        void OnCharacterAdded(NetworkConnection player, LocalCharacter character){
            character.transform.position = new Vector3(1120.56995f, -8.12100029f, 1008.34003f);
            //character.transform.localScale = 0.7f * Vector3.one;
            //character.GetComponent<CharacterScaler>().SetScale(0.7f * Vector3.one);
        }
        async void Start(){
            await ServerRunner.WaitForSceneAsync("murder_mystery_start");
            if (Debug) {
                LocalNPC.FleeSpeedMultiplier = 0f;
                LocalNPC.WalkSpeedMultiplier = 0f;
            }
            for (int i = 0; i < (Debug? 1: 30); i++){
                LocalNPC obj = ServerNPC.SpawnNPC(RobotNPC_Prefab, location: (Debug? new Vector3(1117.56995f, -8.12100029f, 1008.34003f): null));
            }
            while(true) {
                await ServerRunner.Intermission(10);
            }
        }
    }
}
