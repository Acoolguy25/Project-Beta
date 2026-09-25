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

namespace RyanAssets.Shared.Global {
    /// <summary>One "attacker may hurt target" entry of <see cref="SharedGlobalEvents.TeamEnemies"/>, as replicated.</summary>
    public struct TeamEnemyPair {
        public TeamColor attacker;
        public TeamColor target;
    }

    public class SharedGlobalEvents : NetworkBehaviour {
        public static SharedGlobalEvents Instance;
        public static Action OnInstanceReady, OnInstanceReadyPersistent, OnInstanceRemoved;
        //public readonly SyncDictionary<NetworkConnection, ServerPlayerStats> Players = new();
        public readonly SyncList<CommandConfig> Commands = new();
        public readonly SyncList<string> LeaderboardHeaders = new();
        public readonly SyncVar<MusicSelection> MusicTrack = new(initialValue: MusicSelection.GameMusic);
        readonly SyncVar<string> _topMessage = new();

        /// <summary>
        /// Which real teams may attack which. Game modes assign it on the server; it is replicated
        /// through <see cref="teamEnemyPairs"/> so clients answer "is that an enemy?" the same way.
        /// It used to be a plain static that only server runners filled, so on every client of a
        /// dedicated server it stayed null and nothing ever counted as hostile.
        /// </summary>
        public static Dictionary<TeamColor, HashSet<TeamColor>> TeamEnemies {
            get => teamEnemies;
            set {
                teamEnemies = value;
                if (Instance != null && Instance.IsServerInitialized)
                    Instance.PublishTeamEnemies();
            }
        }
        static Dictionary<TeamColor, HashSet<TeamColor>> teamEnemies;
        readonly SyncList<TeamEnemyPair> teamEnemyPairs = new();

        // Voting
        public readonly SyncVar<SharedVoteHeader> SharedVoteHeader = new(new());
        public readonly SyncList<int> VoteTotals = new(new());
        public readonly SyncVar<int> SkipVoteCount = new(0);

        // Structure building
        public readonly SyncList<ushort> Builds = new();
        public readonly SyncVar<bool> CanBuild = new(false);

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
            // A runner usually assigns the table in Awake, before this object has spawned.
            PublishTeamEnemies();
        }

        public override void OnStartClient() {
            base.OnStartClient();
            teamEnemyPairs.OnChange += OnTeamEnemyPairsChanged;
            if (!IsServerInitialized)
                RebuildTeamEnemies();
        }

        public override void OnStopClient() {
            teamEnemyPairs.OnChange -= OnTeamEnemyPairsChanged;
            base.OnStopClient();
        }

        void PublishTeamEnemies() {
            teamEnemyPairs.Clear();
            if (teamEnemies == null)
                return;
            foreach (KeyValuePair<TeamColor, HashSet<TeamColor>> entry in teamEnemies) {
                foreach (TeamColor target in entry.Value)
                    teamEnemyPairs.Add(new TeamEnemyPair { attacker = entry.Key, target = target });
            }
        }

        void OnTeamEnemyPairsChanged(SyncListOperation op, int index, TeamEnemyPair oldItem, TeamEnemyPair newItem, bool asServer) {
            // A host already holds the authoritative table; only a remote client mirrors it.
            if (!asServer && !IsServerInitialized)
                RebuildTeamEnemies();
        }

        void RebuildTeamEnemies() {
            var rebuilt = new Dictionary<TeamColor, HashSet<TeamColor>>();
            foreach (TeamEnemyPair pair in teamEnemyPairs) {
                if (!rebuilt.TryGetValue(pair.attacker, out HashSet<TeamColor> targets))
                    rebuilt[pair.attacker] = targets = new HashSet<TeamColor>();
                targets.Add(pair.target);
            }
            teamEnemies = rebuilt;
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
        public static void UnbindInstanceReady(Action action) {
            OnInstanceReady -= action;
            OnInstanceReadyPersistent -= action;
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
