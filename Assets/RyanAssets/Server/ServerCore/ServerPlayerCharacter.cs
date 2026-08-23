using UnityEngine;
using FishNet;
using FishNet.Connection;
using Unity.VectorGraphics;
using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Requests;
using System.Threading;
using FishNet.Object;
using System;
using FishNet.Transporting;
using System.Collections.Generic;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.DataService;

namespace RyanAssets.Server.ServerCore {
    public class ServerPlayerCharacter: MonoBehaviour {
        [SerializeField]
        NetworkObject characterPrefab;
        //public static event Action<Transform> 
        public static Dictionary<NetworkConnection, LocalCharacter> ClientToCharacter => LocalCharacter.Characters;
        public static event Action<LocalCharacter> CharacterAdded;
        public static Func<NetworkConnection, bool> CanSpawnFunction;
        public static Func<NetworkConnection, Vector3> SpawnLocationFunction;
        public static float RespawnTime = 5f;
        public static ServerPlayerCharacter Instance { get; private set; }
        public LocalCharacter SpawnPlayerCharacter(NetworkConnection player, long health = 100, bool overideCanSpawnFunction = false) {
            if (!player.IsValid) {
                Debug.LogWarning($"Tried to spawn character for invalid player {player}");
                return null;
            }
            if (!overideCanSpawnFunction && CanSpawnFunction != null && !CanSpawnFunction(player))
                return null;
            DespawnPlayerCharacter(player); // make sure bro is despawned first!
            var newCharacter = Instantiate(characterPrefab.gameObject);
            LocalCharacter localChar = newCharacter.GetComponent<LocalCharacter>();
            localChar.OnDied += (_, _) => OnPlayerCharacterDie(player, newCharacter.transform);
            ClientToCharacter[player] = localChar;
            newCharacter.transform.position = SpawnLocationFunction?.Invoke(player) ?? Vector3.zero;
            InstanceFinder.ServerManager.Spawn(newCharacter, ownerConnection: player);
            // SyncVars must be written after FishNet initializes the NetworkObject.
            // Set the account name as the baseline; game modes may deliberately
            // replace it with a round-specific alias in CharacterAdded.
            PlayerData playerData = PlayerData.GetPlayerData(player);
            localChar.DisplayName = playerData?.username.Value;
            // NetworkBehaviour [Server] methods require the NetworkObject to be initialized.
            // Spawn before setting replicated health so FishNet can apply the change.
            localChar.Init(health);
            // Insert player tools
            foreach (var tool in playerData.tools) {
                ServerTool.Instance.SpawnTool(localChar.NetworkObject, tool);
            }
            CharacterAdded?.Invoke(localChar);
            //Debug.Log("Spawned PlayerCharacter");
            return localChar;
        }
        public void SpawnAllPlayerCharacters(long health = 100, bool overideCanSpawnFunction = false) {
            foreach (PlayerData player in PlayerData.Players.Values) {
                SpawnPlayerCharacter(player.Owner, health, overideCanSpawnFunction);
            }
        }
        public async void OnPlayerCharacterDie(NetworkConnection player, Transform character, CancellationToken cancellationToken = default){
            await Awaitable.WaitForSecondsAsync(RespawnTime, cancellationToken);
            if (!cancellationToken.IsCancellationRequested && ClientToCharacter.TryGetValue(player, out LocalCharacter localChar) && localChar.transform == character){
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
            if (character == null)
                return;

            // Tools are independent NetworkObjects. Explicitly despawn them
            // before the character so disconnects and non-death despawns cannot
            // leave weapons alive for a later character instance.
            if (ServerTool.Instance != null)
                ServerTool.Instance.DespawnTools(character);
            ClientToCharacter.Remove(character.Owner);
            //if (character.TryGetComponent(out LocalCharacter localCharacter) && !localCharacter.IsDead)
            //    localCharacter.Kill(DamageType.Despawn);
            if (character.IsSpawned) {
                InstanceFinder.ServerManager.Despawn(character);
                //Debug.Log("Despawning PlayerCharacter {}", character);
            }
        }
        public static void ResetPlayerCharacter(NetworkConnection player) {
            if (LocalCharacter.Characters.TryGetValue(player, out LocalCharacter character))
                ResetPlayerCharacter(character.NetworkObject);
        }
        public static void ResetPlayerCharacter(NetworkObject character){
            character.GetComponent<LocalCharacter>().Kill(RyanAssets.Shared.Declarations.DamageType.Reset);
        }
        public void OnMenuActionRequest(NetworkConnection conn, MenuActionRequest request, Channel channel = Channel.Reliable){
            if (request.type == MenuActionType.ResetCharacter)
                ResetPlayerCharacter(conn);
        }

        void PlayerAdded(PlayerData playerData){
            SpawnPlayerCharacter(playerData.Owner);
        }
        void PlayerRemoved(PlayerData playerData){
            DespawnPlayerCharacter(playerData.Owner);
        }
        void Awake()
        {
            Instance = this;
        }
        void OnEnable(){
            PlayerData.OnPlayerAdded += PlayerAdded;
            PlayerData.OnPlayerRemoved += PlayerRemoved;
            InstanceFinder.ServerManager.RegisterBroadcast<MenuActionRequest>(OnMenuActionRequest);
        }
        void OnDisable(){
            PlayerData.OnPlayerAdded -= PlayerAdded;
            PlayerData.OnPlayerRemoved -= PlayerRemoved;
            InstanceFinder.ServerManager.UnregisterBroadcast<MenuActionRequest>(OnMenuActionRequest);
        }
    }
}
