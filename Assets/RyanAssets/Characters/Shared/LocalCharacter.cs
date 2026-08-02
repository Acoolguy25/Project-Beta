using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using System.Collections.Generic;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;

namespace RyanAssets.Characters.Shared {
    public class LocalCharacter : TrackedGameCharacter {
        public Transform CharacterCamera;
        public static Dictionary<NetworkConnection, LocalCharacter> Characters = new();
        public void InstantiateSelf(NetworkConnection prevOwner) {
            if (Characters.TryGetValue(prevOwner, out LocalCharacter newCharacter) && newCharacter != this)
                Characters.Remove(prevOwner);
            Characters[Owner] = this;
        }
#if !UNITY_SERVER
        public static event Action<(Transform, bool)> AnyCharacterAdded;
        public static event Action<(Transform, bool)> AnyCharacterRemoved;
        public static event Action<(Transform, bool)> AnyCharacterDied;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init() {
            AnyCharacterAdded = null;
            AnyCharacterRemoved = null;
        }
        public override void OnOwnershipClient(NetworkConnection prevOwner) {
            AnyCharacterAdded?.Invoke((transform, IsOwner));
            if (!IsOwner)
                gameObject.name = $"{base.Owner}";
            else
                gameObject.name = $"LocalCharacter";
            InstantiateSelf(prevOwner);
        }
        public override void OnStartClient() {
            base.OnStartClient();
            PlayerData.OnMyPlayerAdded.Subscribe(OnMyPlayerAdded);
        }

        void OnDestroy() {
            AnyCharacterRemoved?.Invoke((transform, IsOwner));
            PlayerData.OnMyPlayerAdded.Unsubscribe(OnMyPlayerAdded);
        }
        void OnDiedEvent(DamageSource source, NetworkObject sourceObject) {
            AnyCharacterDied?.Invoke((transform, IsOwner));
        }
        protected void Awake() {
            CharacterCamera = transform.Find("CharacterCamera");
            OnDied += OnDiedEvent;
            foreach (Transform t in GetComponentsInChildren<Transform>(true)) {
                t.gameObject.layer = LayerMask.NameToLayer("LocalCharacter");
            }
        }
        
#else
        public override void OnOwnershipServer(NetworkConnection prevOwner) {
            InstantiateSelf(prevOwner);
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
            OnMyPlayerAdded(PlayerData.GetPlayerData(Owner));
        }
#endif
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
        public override void OnStopNetwork() {
            if (PlayerData.TryGetPlayerData(Owner, out PlayerData playerData))
                playerData.team.OnChange -= OnPlayerTeamChanged;
            base.OnStopNetwork();
        }
        public override TeamConfig GetTeam() {
            return PlayerData.GetPlayerData(Owner).team.Value;
        }
    }
}
