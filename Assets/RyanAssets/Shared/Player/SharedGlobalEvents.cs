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
#if !UNITY_SERVER
            // foreach (var pair in Players) {
            //     OnPlayerAdded?.Invoke(pair.Key, pair.Value);
            // }
            Players.OnChange += OnPlayerChanged;
#endif
        }
#if !UNITY_SERVER
        public static Action<NetworkConnection, ServerPlayerStats> OnPlayerAdded, OnPlayerRemoved;
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
        // private void SyncLatePlayerAdd(Action<NetworkConnection, ServerPlayerStats> func) {
        //     foreach (var pair in Players) {
        //         func(pair.Key, pair.Value);
        //     }
        // }
        public static void RegisterLatePlayerAdd(Action<NetworkConnection, ServerPlayerStats> func) {
            // if (Instance != null)
            //     Instance.SyncLatePlayerAdd(func);
            OnPlayerAdded += func;
        }
#endif
    }
}