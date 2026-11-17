using FishNet.Broadcast;

namespace Universes.UniverseData.obby {
    public enum OB_Phase : byte { Waiting, Countdown, Racing, Closing, Results }

    /// <summary>Why a player's progress changed, so the client can react.</summary>
    public enum OB_ProgressReason : byte { Sync, Checkpoint, Coin, Finish, Hazard, Fell }

    /// <summary>Medal for a finish time (after the coin bonus); see <see cref="OB_MedalTimes"/>.</summary>
    public enum OB_Medal : byte { None, Bronze, Silver, Gold }

    /// <summary>The course every peer builds. The same seed always produces the same course.</summary>
    public struct OB_CourseBroadcast : IBroadcast {
        public int seed;
        public int stageCount;
        public int round;
        // Appended fields.
        /// <summary>Seconds each coin takes off a finish time on this course.</summary>
        public float coinBonus;
        /// <summary>Gold medal pace in scored seconds per stage (splits are shown against it).</summary>
        public float goldPerStage;
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
        /// <summary>Provisional rank among the racers finished so far, by scored time.</summary>
        public int place;
        public OB_ProgressReason reason;
        // Appended fields (keep the order above stable).
        /// <summary>Seconds since the race started when this happened (the split at a checkpoint).</summary>
        public float raceTime;
        /// <summary>Finish time minus the coin bonus; 0 until finished.</summary>
        public float scoredTime;
        public OB_Medal medal;
        /// <summary>The finish beat this player's saved best scored time.</summary>
        public bool personalBest;
    }

    public struct OB_ResultEntry {
        public string name;
        public bool finished;
        public float time;
        public int stage;
        public int coins;
        // Appended fields.
        /// <summary>Finish time minus the coin bonus (what the results are ranked by).</summary>
        public float scoredTime;
        public OB_Medal medal;
        /// <summary>The racer's connection id, so a client can always show its own row.</summary>
        public int clientId;
    }

    public struct OB_ResultsBroadcast : IBroadcast {
        public int seed;
        public OB_ResultEntry[] entries;
        // Appended fields: this course's medal targets (scored seconds) and the bonus per coin.
        public float goldTime, silverTime, bronzeTime;
        public float coinBonus;
    }

    /// <summary>Where one racer is, for the race strip and overtake toasts.</summary>
    public struct OB_Standing {
        public int clientId;
        public string name;
        /// <summary>Checkpoint reached (the stage count once finished).</summary>
        public int stage;
        public bool finished;
        public float scoredTime;
    }

    /// <summary>Every racer's standing, sent to everyone whenever someone reaches a checkpoint or finishes.</summary>
    public struct OB_StandingsBroadcast : IBroadcast {
        public int seed;
        public OB_Standing[] racers;
    }
}
