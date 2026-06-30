using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Ionic.Zlib;
using RyanAssets.Commands.Shared;
using RyanAssets.Shared.Declarations;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
        public static Action OnInstanceReady;
        public readonly SyncDictionary<NetworkConnection, ServerPlayerStats> Players = new();
        public readonly SyncList<CommandConfig> Commands = new();
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
            OnInstanceReady?.Invoke();
#if !UNITY_SERVER
            // foreach (var pair in Players) {
            //     OnPlayerAdded?.Invoke(pair.Key, pair.Value);
            // }
            InstanceFinder.ClientManager.RegisterBroadcast<PlayerLeaveBroadcast>(OnPlayerRemovedHandler);
            Players.OnChange += OnPlayerChanged;
            Commands.OnChange += OnCommandsChanged;
            VoteOptions.OnChange += OnVoteOptionsChanged;
            _currentVote.OnChange += OnCurrentVoteChanged;
            _topMessage.OnChange += (_, msg, _) => TopMessageChanged?.Invoke(msg);
            PlayerListSynced = false;
#endif
        }

        public override void OnStartServer() {
            base.OnStartServer();
            Instance = this;
            OnInstanceReady?.Invoke();
        }
#if !UNITY_SERVER
        public static Action<NetworkConnection, ServerPlayerStats> OnPlayerRemoved, OnPlayerUpdated;
        public static Action<NetworkConnection, ServerPlayerStats, bool> OnPlayerAdded;
        public static Action<ServerPlayerStats> OnMyPlayerUpdated;
        public static Action OnCommandsUpdated;
        public static Action OnVoteChanged;
        public static Action<SharedVoteInfo> OnCurrentVoteChangedEvent;
        public static bool PlayerListSynced;

        void OnPlayerChanged(SyncDictionaryOperation op, NetworkConnection key, ServerPlayerStats value, bool asServer) {
            switch (op) {
                case SyncDictionaryOperation.Add:
                    // Debug.Log($"{key} player added");
                    OnPlayerAdded?.Invoke(key, value, PlayerListSynced);
                    if (key.IsLocalClient)
                        OnMyPlayerUpdated?.Invoke(value);
                    break;
                case SyncDictionaryOperation.Set:
                    OnPlayerUpdated?.Invoke(key, value);
                    if (key.IsLocalClient)
                        OnMyPlayerUpdated?.Invoke(value);
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
            if (op != SyncListOperation.Complete)
                return;
            OnVoteChanged?.Invoke();
        }

        void OnCommandsChanged(SyncListOperation op, int index, CommandConfig oldItem, CommandConfig newItem, bool asServer) {
            if (op != SyncListOperation.Complete)
                return;
            OnCommandsUpdated?.Invoke();
        }

        void OnCurrentVoteChanged(SharedVoteInfo previous, SharedVoteInfo next, bool asServer) {
            OnCurrentVoteChangedEvent?.Invoke(next);
            OnVoteChanged?.Invoke();
        }
        void OnPlayerRemovedHandler(PlayerLeaveBroadcast msg, FishNet.Transporting.Channel channel) {
            OnPlayerRemoved?.Invoke(msg.player, msg.stats);
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
            Instance = null;
            OnInstanceReady = null;
            OnPlayerAdded = null;
            OnPlayerRemoved = null;
            OnPlayerUpdated = null;
            OnMyPlayerUpdated = null;
            OnVoteChanged = null;
            OnCurrentVoteChangedEvent = null;
            OnCommandsUpdated = null;
        }
        private void OnDestroy() {
            if (InstanceFinder.ClientManager == null)
                return;
            InstanceFinder.ClientManager.UnregisterBroadcast<PlayerLeaveBroadcast>(OnPlayerRemovedHandler);
        }
#endif
        public List<string> GetPlayerNames(Func<KeyValuePair<NetworkConnection, ServerPlayerStats>, bool> selector = null) {
            List<string> strings = new();
            foreach (var item in Players) {
                if (selector != null && !selector(item))
                    continue;
                strings.Add(item.Value.data.username);
            }
            return strings;
        }
        public static string GetPlayerName(NetworkConnection connection) {
            if (Instance && Instance.Players.TryGetValue(connection, out ServerPlayerStats serverPlayerStats)){
                return serverPlayerStats.data.username;
            }
            return null;
        }
    }
}
