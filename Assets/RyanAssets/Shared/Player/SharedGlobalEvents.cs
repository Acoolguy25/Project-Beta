using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using RyanAssets.Commands.Shared;
using RyanAssets.Core;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RyanAssets.Shared.Player {
    
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
        public readonly SyncList<string> LeaderboardHeaders = new();
        public readonly SyncVar<MusicTracks> MusicTrack = new(initialValue: Player.MusicTracks.GameMusic1);
        readonly SyncVar<string> _topMessage = new();
        public static Dictionary<TeamColor, HashSet<TeamColor>> TeamEnemies;

        // Voting
        public readonly SyncVar<SharedVoteHeader> SharedVoteHeader = new(new());
        public readonly SyncList<int> VoteTotals = new(new());
        public readonly SyncVar<int> SkipVoteCount = new(0);

#if UNITY_SERVER
        [NonSerialized]
        public bool TeamKillEnabled = true;
        [NonSerialized]
        public bool GlobalInvul     = true;
#endif

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
            _topMessage.OnChange += (_, msg, _) => TopMessageChanged?.Invoke(msg);
            //PlayerListSynced = false;
#endif
        }
        public static bool isVoting => Instance != null && Instance.SharedVoteHeader.Value.voteId != VoteEnum.None && Instance.SharedVoteHeader.Value.endTime >= NetworkHelper.ServerTime;

        public override void OnStartServer() {
            base.OnStartServer();
            Instance = this;
        }
#if !UNITY_SERVER
        
        public static Action OnCommandsUpdated;

        void OnCommandsChanged(SyncListOperation op, int index, CommandConfig oldItem, CommandConfig newItem, bool asServer) {
            if (op != SyncListOperation.Complete)
                return;
            OnCommandsUpdated?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init(){
            Instance = null;
            OnInstanceReady = null;
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
