using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace RyanAssets.Shared.Player {
    public class SharedGlobalEvents : NetworkBehaviour {
        public static SharedGlobalEvents Instance;
        public readonly SyncDictionary<NetworkConnection, ServerPlayerStats> Players = new();
        void Awake() {
            Instance = this;
        }
#if !UNITY_SERVER
        public static Action<NetworkConnection, ServerPlayerStats> OnPlayerAdded, OnPlayerRemoved;
        void Start() {
            Players.OnChange += OnPlayerChanged;
        }
        void OnPlayerChanged(SyncDictionaryOperation op, NetworkConnection key, ServerPlayerStats value, bool asServer) {
            switch (op) {
                case SyncDictionaryOperation.Add:
                    OnPlayerAdded?.Invoke(key, value);
                    break;
                case SyncDictionaryOperation.Remove:
                    OnPlayerAdded?.Invoke(key, value);
                    break;
                case SyncDictionaryOperation.Clear:
                    Debug.LogError("PlayerList was cleared!");
                    break;
            }
        }
#endif
    }
}