using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using System.Collections.Generic;
using RyanAssets.DataService;

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
        void OnDestroy() {
            AnyCharacterRemoved?.Invoke((transform, IsOwner));
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
        public override void SetTeam(TeamConfig teamConfig) {
            PlayerData.GetPlayerData(Owner).SetPlayerTeam(teamConfig);
        }
#endif
        public override TeamConfig GetTeam() {
            return PlayerData.GetPlayerData(Owner).team.Value;
        }
    }
}
