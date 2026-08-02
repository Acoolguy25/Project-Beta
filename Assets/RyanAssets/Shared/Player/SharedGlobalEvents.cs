using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using RyanAssets.Commands.Shared;
using RyanAssets.DataService;
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
    [Serializable]
    public enum MusicTracks : ushort {
        MenuMusic,
        GameMusic1,
        GameMusic2,
        None
    };

    public class SharedGlobalEvents : NetworkBehaviour {
        public static SharedGlobalEvents Instance;
        public static Action OnInstanceReady, OnInstanceReadyPersistent, OnInstanceRemoved;
        //public readonly SyncDictionary<NetworkConnection, ServerPlayerStats> Players = new();
        public readonly SyncList<CommandConfig> Commands = new();
        public readonly SyncList<SharedVoteOption> VoteOptions = new();
        public readonly SyncList<string> LeaderboardHeaders = new();
        public readonly SyncVar<MusicTracks> MusicTrack = new(initialValue: Player.MusicTracks.GameMusic1);
        readonly SyncVar<SharedVoteInfo> _currentVote = new();
        readonly SyncVar<string> _topMessage = new();
        public static Dictionary<TeamColor, HashSet<TeamColor>> TeamEnemies;

#if UNITY_SERVER
        [NonSerialized]
        public bool TeamKillEnabled = true;
        [NonSerialized]
        public bool GlobalInvul     = true;
#endif

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
            //Players.OnChange += OnPlayerChanged;
            Commands.OnChange += OnCommandsChanged;
            VoteOptions.OnChange += OnVoteOptionsChanged;
            _currentVote.OnChange += OnCurrentVoteChanged;
            _topMessage.OnChange += (_, msg, _) => TopMessageChanged?.Invoke(msg);
            //PlayerListSynced = false;
#endif
        }

        public override void OnStartServer() {
            base.OnStartServer();
            Instance = this;
        }
#if !UNITY_SERVER
        
        public static Action OnCommandsUpdated;
        public static Action OnVoteChanged;
        public static Action<SharedVoteInfo> OnCurrentVoteChangedEvent;

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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init(){
            Instance = null;
            OnInstanceReady = null;
            OnVoteChanged = null;
            OnCurrentVoteChangedEvent = null;
            OnCommandsUpdated = null;
        }
        public static void BindInstanceReady(Action action, bool persistent = false) {
            if (persistent)
                OnInstanceReadyPersistent += action;
            if (Instance == null) {
                if (!persistent)
                    OnInstanceReady += action;
            } else
                action();
        }
#else
        public static void SetTopMessage(string topMessage) {
            Instance.TopMessage = topMessage;
        }
#endif
        public static int GetLeaderboardIndex(string name) {
            return Instance?.LeaderboardHeaders?.IndexOf(name) ?? -1;
        }
        public override void OnStartNetwork() {
            OnInstanceReady?.Invoke();
            OnInstanceReadyPersistent?.Invoke();
            OnInstanceReady = null;
        }
        public override void OnStopNetwork() {
            OnInstanceRemoved?.Invoke();
        }
    }
}
