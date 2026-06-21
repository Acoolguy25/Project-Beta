using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using UnityEngine;

namespace Universes.murder_mystery.Server {
    public class MM_ServerRunner : MonoBehaviour {
        void Awake(){
            ServerPlayerCharacter.OnPlayerCharacterAdded += OnCharacterAdded;
            ServerPlayerCharacter.CanSpawnFunction = CanSpawnFunction;
        }
        bool CanSpawnFunction(NetworkConnection player) {
            return true;
        }
        void OnCharacterAdded(NetworkConnection player, LocalCharacter character){
            character.transform.position = new Vector3(80f, 5f, 7.6f);
            character.transform.localScale = 0.7f * Vector3.one;
        }
        async void Start(){
            while(true) {
                await ServerRunner.Intermission(10);
            }
        }
    }
}