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
using System.Collections.Generic;

namespace RyanAssets.Server.ServerCore {
    public class ServerPlayerCharacter: MonoBehaviour {
        [SerializeField]
        NetworkObject characterPrefab;
        //public static event Action<Transform> 
        public static Dictionary<NetworkConnection, LocalCharacter> ClientToCharacter;
        public static Func<NetworkConnection, bool> CanSpawnFunction;
        public static event Action<NetworkConnection, LocalCharacter> OnPlayerCharacterAdded;
        public static event Action<NetworkConnection, LocalCharacter> OnPlayerCharacterDied;
        public static float RespawnTime = 5f;
        public static ServerPlayerCharacter Instance { get; private set; }

        public void SpawnPlayerCharacter(NetworkConnection player, long health = 100) {
            if (!player.IsValid) {
                Debug.LogWarning($"Tried to spawn character for invalid player {player}");
                return;
            }
            if (CanSpawnFunction != null && !CanSpawnFunction(player))
                return;
            var newCharacter = Instantiate(characterPrefab.gameObject);
            newCharacter.transform.position = Vector3.zero;
            LocalCharacter localChar = newCharacter.GetComponent<LocalCharacter>();
            localChar.OnDied += (_) => OnPlayerCharacterDie(player, newCharacter.transform);
            localChar.Init(health);
            OnPlayerCharacterAdded?.Invoke(player, localChar);
            InstanceFinder.ServerManager.Spawn(newCharacter, ownerConnection: player);
            ClientToCharacter[player] = localChar;
        }
        public async void OnPlayerCharacterDie(NetworkConnection player, Transform character, CancellationToken cancellationToken = default){
            OnPlayerCharacterDied?.Invoke(player, character.GetComponent<LocalCharacter>());
            await Awaitable.WaitForSecondsAsync(RespawnTime, cancellationToken);
            if (!cancellationToken.IsCancellationRequested){
                DespawnPlayerCharacter(player);
                if (player.IsValid) // Make sure bro didn't leave
                    SpawnPlayerCharacter(player);
            }
        }
        public static void DespawnPlayerCharacter(NetworkConnection player) {
            if (ClientToCharacter.TryGetValue(player, out LocalCharacter character))
                DespawnPlayerCharacter(character.NetworkObject);
        }
        public static void DespawnPlayerCharacter(NetworkObject character){
            ClientToCharacter.Remove(character.Owner);
            //if (character.TryGetComponent(out LocalCharacter localCharacter) && !localCharacter.IsDead())
            //    localCharacter.Kill(DamageSource.Despawn);
            InstanceFinder.ServerManager.Despawn(character);
        }
        public static void ResetPlayerCharacter(NetworkConnection player) {
            if (LocalCharacter.Characters.TryGetValue(player, out NetworkObject character))
                ResetPlayerCharacter(character);
        }
        public static void ResetPlayerCharacter(NetworkObject character){
            character.GetComponent<LocalCharacter>().Kill(DamageSource.Reset);
        }
        public void OnMenuActionRequest(NetworkConnection conn, MenuActionRequest request, Channel channel = Channel.Reliable){
            if (request.type == MenuActionType.ResetCharacter)
                ResetPlayerCharacter(conn);
        }

        void PlayerAdded(NetworkConnection player){
            SpawnPlayerCharacter(player);
        }
        void PlayerRemoved(NetworkConnection player){
            DespawnPlayerCharacter(player);
        }
        void Awake()
        {
            Instance = this;
            ClientToCharacter = new();
        }
        void OnEnable(){
            ServerPlayerEvents.OnPlayerAddedEvent += PlayerAdded;
            ServerPlayerEvents.OnPlayerRemovedEvent += PlayerRemoved;
            InstanceFinder.ServerManager.RegisterBroadcast<MenuActionRequest>(OnMenuActionRequest);
        }
        void OnDisable(){
            ServerPlayerEvents.OnPlayerAddedEvent -= PlayerAdded;
            ServerPlayerEvents.OnPlayerRemovedEvent -= PlayerRemoved;
            InstanceFinder.ServerManager.UnregisterBroadcast<MenuActionRequest>(OnMenuActionRequest);
        }
    }
}