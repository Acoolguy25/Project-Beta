using UnityEngine;
using FishNet;
using FishNet.Connection;
using Unity.VectorGraphics;
using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Requests;
using System.Threading.Tasks;
using System.Threading;
using FishNet.Object;
using System;
using FishNet.Transporting;

namespace RyanAssets.Server.ServerCore {
    public class ServerPlayerCharacter: MonoBehaviour {
        [SerializeField]
        NetworkObject characterPrefab;
        //public static event Action<Transform> 
        public static Func<NetworkConnection, bool> CanSpawnFunction;
        public static event Action<NetworkConnection, LocalCharacter> OnPlayerCharacterAdded;

        public void SpawnPlayerCharacter(NetworkConnection player, long health = 100){
            if (CanSpawnFunction != null && !CanSpawnFunction(player))
                return;
            GameObject newCharacter = Instantiate(characterPrefab.gameObject);
            newCharacter.transform.position = Vector3.zero;
            LocalCharacter localChar = newCharacter.GetComponent<LocalCharacter>();
            localChar.OnDied += () => OnPlayerCharacterDied(player, newCharacter.transform);
            localChar.Init(health);
            OnPlayerCharacterAdded?.Invoke(player, localChar);
            InstanceFinder.ServerManager.Spawn(newCharacter, ownerConnection: player);
        }
        public async void OnPlayerCharacterDied(NetworkConnection player, Transform character, CancellationToken cancellationToken = default){
            await Awaitable.WaitForSecondsAsync(5f, cancellationToken);
            if (!cancellationToken.IsCancellationRequested){
                DespawnPlayerCharacter(player);
                SpawnPlayerCharacter(player);
            }
        }
        public void DespawnPlayerCharacter(NetworkConnection player) {
            if (LocalCharacter.Characters.TryGetValue(player, out NetworkObject character))
                DespawnPlayerCharacter(character);
        }
        public void DespawnPlayerCharacter(NetworkObject character){
            InstanceFinder.ServerManager.Despawn(character);
        }
        public void ResetPlayerCharacter(NetworkConnection player) {
            if (LocalCharacter.Characters.TryGetValue(player, out NetworkObject character))
                ResetPlayerCharacter(character);
        }
        public void ResetPlayerCharacter(NetworkObject character){
            character.GetComponent<LocalCharacter>().Kill();
        }
        public void OnMenuActionRequest(NetworkConnection conn, MenuActionRequest request, Channel channel = Channel.Reliable){
            if (request.type == MenuActionType.ResetCharacter)
                ResetPlayerCharacter(conn);
        }

        void PlayerAdded(NetworkConnection player){
            SpawnPlayerCharacter(player);
        }
        void OnEnable(){
            ServerPlayerEvents.OnPlayerAddedEvent += PlayerAdded;
            InstanceFinder.ServerManager.RegisterBroadcast<MenuActionRequest>(OnMenuActionRequest);
        }
        void OnDisable(){
            ServerPlayerEvents.OnPlayerAddedEvent -= PlayerAdded;
            InstanceFinder.ServerManager.UnregisterBroadcast<MenuActionRequest>(OnMenuActionRequest);
        }
    }
}