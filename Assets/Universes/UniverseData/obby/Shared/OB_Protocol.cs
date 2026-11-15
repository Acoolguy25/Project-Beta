using FishNet.Broadcast;

namespace Universes.UniverseData.obby {
    public enum OB_Phase : byte { Waiting, Countdown, Racing, Closing, Results }

    /// <summary>Why a player's progress changed, so the client can react.</summary>
    public enum OB_ProgressReason : byte { Sync, Checkpoint, Coin, Finish, Hazard, Fell }

    /// <summary>The course every peer builds. The same seed always produces the same course.</summary>
    public struct OB_CourseBroadcast : IBroadcast {
        public int seed;
        public int stageCount;
        public int round;
    }

    /// <summary>A client without the current course asks for it (and its own progress).</summary>
    public struct OB_CourseRequest : IBroadcast {
        public int knownSeed;
    }

    /// <summary>Round status for everyone, sent about once a second.</summary>
    public struct OB_RoundBroadcast : IBroadcast {
        public int seed;
        public OB_Phase phase;
        public int secondsLeft;
        public float raceTime;
        public string leaderName;
        public int leaderStage;
        public int finishedCount;
        public int playerCount;
        public string headline;
    }

    /// <summary>One player's own progress.</summary>
    public struct OB_ProgressBroadcast : IBroadcast {
        public int seed;
        public int checkpoint;
        public ulong coins;
        public bool finished;
        public float finishTime;
        public int place;
        public OB_ProgressReason reason;
    }

    public struct OB_ResultEntry {
        public string name;
        public bool finished;
        public float time;
        public int stage;
        public int coins;
    }

    public struct OB_ResultsBroadcast : IBroadcast {
        public int seed;
        public OB_ResultEntry[] entries;
    }
}
