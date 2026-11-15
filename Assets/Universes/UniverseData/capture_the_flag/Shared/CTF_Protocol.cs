using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    public enum CTF_Phase : byte { Waiting, Countdown, Playing, Results }
    public enum CTF_EventKind : byte { Taken, Dropped, Returned, Captured, Won }

    public struct CTF_FlagSnap {
        public CTF_FlagState state;
        public Vector3 position;
        public int carrierId;
    }

    /// <summary>Score, clock and both flags, 10 times a second while playing.</summary>
    public struct CTF_StateBroadcast : IBroadcast {
        public CTF_Phase phase;
        public int secondsLeft;
        public int redScore, blueScore;
        public CTF_FlagSnap redFlag, blueFlag;
        public string headline;
    }

    /// <summary>Your team, sent when it is assigned.</summary>
    public struct CTF_TeamBroadcast : IBroadcast {
        public CTF_Team team;
    }

    public struct CTF_EventBroadcast : IBroadcast {
        public CTF_EventKind kind;
        /// <summary>Whose flag it is (for Won: the winning team).</summary>
        public CTF_Team flag;
        public string playerName;
        public int playerId;
    }

    public struct CTF_StateRequest : IBroadcast {
        public int known;
    }
}
