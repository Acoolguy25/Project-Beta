using System;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using RyanAssets.Shared.Declarations;
using FishNet.Transporting;

namespace RyanAssets.Shared.Player {
    [Serializable]
    public struct SharedVoteInfo {
        public int voteId;
        public string title;
        public string description;
        public long endUtcTicks;
        public bool isActive;
    }

    [Serializable]
    public struct SharedVoteOption {
        public int voteId;
        public int optionId;
        public string title;
        public string description;
        public string imageUrl;
        public int count;
    }

    public class SharedGlobalEvents : NetworkBehaviour {
        public static SharedGlobalEvents Instance;
        public readonly SyncDictionary<NetworkConnection, ServerPlayerStats> Players = new();
        public readonly SyncList<SharedVoteOption> VoteOptions = new();
        readonly SyncVar<SharedVoteInfo> _currentVote = new();
        readonly SyncVar<string> _topMessage = new();

        public SharedVoteInfo CurrentVote {
            get => _currentVote.Value;
            set => _currentVote.Value = value;
        }
        public string TopMessage {
            get => _topMessage.Value;
            set => _topMessage.Value = value;
        }
        public static Action<string> TopMessageChanged;

        void Awake() {
            Instance = this;
#if !UNITY_SERVER
            // foreach (var pair in Players) {
            //     OnPlayerAdded?.Invoke(pair.Key, pair.Value);
            // }
            InstanceFinder.ClientManager.RegisterBroadcast<PlayerLeaveBroadcast>(OnPlayerRemovedHandler);
            Players.OnChange += OnPlayerChanged;
            VoteOptions.OnChange += OnVoteOptionsChanged;
            _currentVote.OnChange += OnCurrentVoteChanged;
            _topMessage.OnChange += (_, msg, _) => TopMessageChanged?.Invoke(msg);
            PlayerListSynced = false;
#endif
        }
#if !UNITY_SERVER
        public static Action<NetworkConnection, ServerPlayerStats> OnPlayerRemoved, OnPlayerUpdated;
        public static Action<NetworkConnection, ServerPlayerStats, bool> OnPlayerAdded;
        public static Action OnVoteChanged;
        public static Action<SharedVoteInfo> OnCurrentVoteChangedEvent;
        public static bool PlayerListSynced;

        void OnPlayerChanged(SyncDictionaryOperation op, NetworkConnection key, ServerPlayerStats value, bool asServer) {
            switch (op) {
                case SyncDictionaryOperation.Add:
                    // Debug.Log($"{key} player added");
                    OnPlayerAdded?.Invoke(key, value, PlayerListSynced);
                    break;
                case SyncDictionaryOperation.Set:
                    OnPlayerUpdated?.Invoke(key, value);
                    break;
                case SyncDictionaryOperation.Remove:
                    // Debug.Log($"{key} player removed");
                    //OnPlayerRemoved?.Invoke(key, value);
                    break;
                case SyncDictionaryOperation.Clear:
                    Debug.LogError("PlayerList was cleared!");
                    break;
                case SyncDictionaryOperation.Complete:
                    PlayerListSynced = true;
                    break;
            }
        }

        void OnVoteOptionsChanged(SyncListOperation op, int index, SharedVoteOption oldItem, SharedVoteOption newItem, bool asServer) {
            OnVoteChanged?.Invoke();
        }

        void OnCurrentVoteChanged(SharedVoteInfo previous, SharedVoteInfo next, bool asServer) {
            OnCurrentVoteChangedEvent?.Invoke(next);
            OnVoteChanged?.Invoke();
        }
        void OnPlayerRemovedHandler(PlayerLeaveBroadcast data, Channel channel) {
            OnPlayerRemoved?.Invoke(data.player, data.stats);
        }

        // private void SyncLatePlayerAdd(Action<NetworkConnection, ServerPlayerStats> func) {
        //     foreach (var pair in Players) {
        //         func(pair.Key, pair.Value);
        //     }
        // }
        public static void RegisterLatePlayerAdd(Action<NetworkConnection, ServerPlayerStats, bool> func) {
            // if (Instance != null)
            //     Instance.SyncLatePlayerAdd(func);
            OnPlayerAdded += func;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init(){
            OnPlayerAdded = null;
            OnPlayerRemoved = null;
            OnPlayerUpdated = null;
            OnVoteChanged = null;
            OnCurrentVoteChangedEvent = null;
        }
        private void OnDestroy() {
            if (InstanceFinder.ClientManager == null)
                return;
            InstanceFinder.ClientManager.UnregisterBroadcast<PlayerLeaveBroadcast>(OnPlayerRemovedHandler);
        }
#endif
    }
}
