using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using System.Collections.Generic;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using FishNet.Component.Transforming;

namespace RyanAssets.Characters.Shared {
    public class LocalCharacter : GameCharacter {
        private const float RespawnVerticalOffset = 0.05f;

        public static Dictionary<NetworkConnection, LocalCharacter> Characters = new();
        public void InstantiateSelf(NetworkConnection prevOwner) {
            if (Characters.TryGetValue(prevOwner, out LocalCharacter newCharacter) && newCharacter != this)
                Characters.Remove(prevOwner);
            Characters[Owner] = this;
        }
        public static event Action<LocalCharacter> LocalCharacterAdded;
        public static event Action<LocalCharacter> LocalCharacterRemoved;
        public static event Action<LocalCharacter, DamageType, GameCharacter> LocalCharacterDied;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init() {
            LocalCharacterAdded = null;
            LocalCharacterRemoved = null;
            LocalCharacterDied = null;
        }
#if !UNITY_SERVER
        public override void OnOwnershipClient(NetworkConnection prevOwner) {
            InstantiateSelf(prevOwner);
            LocalCharacterAdded?.Invoke(this);
            if (IsOwner)
                gameObject.name = $"LocalCharacter";
        }
        public override void OnStartClient() {
            base.OnStartClient();
            PlayerData.OnMyPlayerAdded.Subscribe(OnMyPlayerAdded);
        }
#else
        public override void OnOwnershipServer(NetworkConnection prevOwner) {
            InstantiateSelf(prevOwner);
            LocalCharacterAdded?.Invoke(this);
        }
        [Server]
        public override void SetTeam(TeamConfig teamConfig) {
            PlayerData playerData = PlayerData.GetPlayerData(Owner);
            if (playerData == null)
                return;

            base.SetTeam(teamConfig);
            playerData.SetPlayerTeam(teamConfig);
        }
        public override void OnStartServer() {
            base.OnStartNetwork();
            PlayerData playerData = PlayerData.GetPlayerData(Owner);
            if (playerData == null) {
                Debug.LogWarning($"Character started before PlayerData was registered for {Owner}.");
                return;
            }
            OnMyPlayerAdded(playerData);
        }
#endif
        protected override void OnDestroy() {
            base.OnDestroy();
#if !UNITY_SERVER
            PlayerData.OnMyPlayerAdded.Unsubscribe(OnMyPlayerAdded);
#endif
        }
        void OnDiedEvent(DamageType source, IEntity sourceEntity) {
            LocalCharacterDied?.Invoke(this, source, sourceEntity as GameCharacter);
        }
        protected override void Awake() {
            base.Awake();
            CharacterCamera = transform.Find("CharacterCamera");
            OnDied += OnDiedEvent;
            foreach (Transform t in GetComponentsInChildren<Transform>(true)) {
                t.gameObject.layer = LayerMask.NameToLayer("LocalCharacter");
            }
        }
        void OnMyPlayerAdded(PlayerData data) {
            data.team.OnChange += OnPlayerTeamChanged;
            OnPlayerTeamChanged(default, data.team.Value, true);
        }
        void OnPlayerTeamChanged(TeamConfig prev, TeamConfig next, bool asServer) {
#if UNITY_SERVER
            base.SetTeam(next);
#endif
#if UNITY_EDITOR
            UpdateTeamEditorOptions(prev, next, asServer);
#endif
        }
        protected override void SharedDied(DamageType source, IEntity sourceEntity) {
            base.SharedDied(source, sourceEntity);
        }

        [Server]
        public bool ReviveAtPosition(long hp, Vector3 position) {
#if UNITY_SERVER
            if (!IsDead)
                return false;

            Vector3 respawnPosition = position + Vector3.down * RespawnVerticalOffset;
            Revive(hp);
            ApplyRespawnPosition(respawnPosition);
            RpcApplyRespawnPosition(respawnPosition);
            return true;
#else
            return false;
#endif
        }

        [ObserversRpc]
        private void RpcApplyRespawnPosition(Vector3 position) {
            ApplyRespawnPosition(position);
        }

        private void ApplyRespawnPosition(Vector3 position) {
            transform.position = position;

            if (TryGetComponent(out Rigidbody rootBody)) {
                rootBody.position = position;
                rootBody.linearVelocity = Vector3.zero;
                rootBody.angularVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
            if (TryGetComponent(out NetworkTransform networkTransform))
                networkTransform.Teleport();
        }

        public override void OnStopNetwork() {
            base.OnStopNetwork();
            Characters.Remove(Owner);
            if (PlayerData.TryGetPlayerData(Owner, out PlayerData playerData))
                playerData.team.OnChange -= OnPlayerTeamChanged;
            LocalCharacterRemoved?.Invoke(this);
        }
        public override TeamConfig GetTeam() {
            return PlayerData.GetPlayerData(Owner)?.team.Value ?? new();
        }
    }

    public static class PlayerDataCharacterExtensions {
        public static LocalCharacter GetCharacter(this PlayerData playerData) {
            if (playerData == null)
                return null;

            return LocalCharacter.Characters.TryGetValue(playerData.Owner, out LocalCharacter character)
                ? character
                : null;
        }

        public static bool TryGetCharacter(this PlayerData playerData, out LocalCharacter character) {
            character = playerData.GetCharacter();
            return character != null;
        }
    }
}
