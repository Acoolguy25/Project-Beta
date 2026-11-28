using FishNet.Broadcast;

namespace Universes.UniverseData.obby {
    public enum OB_Phase : byte { Waiting, Countdown, Racing, Closing, Results }

    /// <summary>Why a player's progress changed, so the client can react.</summary>
    public enum OB_ProgressReason : byte { Sync, Checkpoint, Coin, Finish, Hazard, Fell, Star, OutOfBounds }

    /// <summary>Medal for a finish time (after the coin and star bonuses); see <see cref="OB_MedalTimes"/>.</summary>
    public enum OB_Medal : byte { None, Bronze, Silver, Gold }

    /// <summary>The course every peer builds. The same seed always produces the same course.</summary>
    public struct OB_CourseBroadcast : IBroadcast {
        public int seed;
        public int stageCount;
        public int round;
        // Appended fields.
        /// <summary>Seconds each coin takes off a finish time on this course.</summary>
        public float coinBonus;
        /// <summary>Average gold medal pace in scored seconds per stage (for display; splits use <see cref="goldFactor"/>).</summary>
        public float goldPerStage;
        /// <summary>Seconds each star takes off a finish time.</summary>
        public float starBonus;
        /// <summary>Gold target as a multiple of par: a split's gold pace is this times the par of the stages done.</summary>
        public float goldFactor;
        /// <summary><see cref="OB_CourseRules.Hash"/> of the rules the server built from (0: unknown).</summary>
        public int rulesHash;
    }

    /// <summary>
    /// A client's shared-clock time, sent a few times a second. The server compares it with its own
    /// clock to learn how far behind the server the moment a player saw is (clock skew plus travel
    /// time), and judges timed hazards (lasers, crushers) at that moment.
    /// </summary>
    public struct OB_ViewClock : IBroadcast {
        public double time;
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
        /// <summary>Stars collected, one bit per stage.</summary>
        public ulong stars;
        /// <summary>When this update closes a tier (its last checkpoint or the finish): seconds the tier took, else 0.</summary>
        public float tierTime;
        /// <summary>The player's saved best for that tier before this run (0 when none).</summary>
        public float tierBest;
        /// <summary>The tier closed by <see cref="tierTime"/>.</summary>
        public OB_Tier tier;
        /// <summary>Mechanics this player has cleared before (saved; <see cref="OB_StageHints"/>), so their hints stay hidden.</summary>
        public ulong seenMechanics;
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
        public int stars;
    }

    public struct OB_ResultsBroadcast : IBroadcast {
        public int seed;
        public OB_ResultEntry[] entries;
        // Appended fields: this course's medal targets (scored seconds) and the bonus per coin.
        public float goldTime, silverTime, bronzeTime;
        public float coinBonus;
        public float starBonus;
    }

    /// <summary>Where one racer is, for the race strip and overtake toasts.</summary>
    public struct OB_Standing {
        public int clientId;
        /// <summary>Unused on the wire (clients know the names); the server fills it only for its own ordering.</summary>
        public string name;
        /// <summary>Checkpoint reached (the stage count once finished).</summary>
        public int stage;
        public bool finished;
        public float scoredTime;
        // Appended fields.
        /// <summary>Race time <see cref="stage"/> was reached (0 at the start), so racers on one checkpoint have an order.</summary>
        public float reachedAt;
    }

    /// <summary>Every racer's standing, sent to everyone whenever someone reaches a checkpoint or finishes.</summary>
    public struct OB_StandingsBroadcast : IBroadcast {
        public int seed;
        public OB_Standing[] racers;
    }

    /// <summary>
    /// One racer's power-ups, sent to everyone when they change (so every client can draw the bubble and the rainbow),
    /// and to a joining client for every racer.
    /// </summary>
    public struct OB_PowerBroadcast : IBroadcast {
        public int seed;
        public int clientId;
        public OB_PowerEvent evt;
        /// <summary>The power just gained (<see cref="OB_PowerEvent.Got"/>), else None.</summary>
        public OB_PowerUp gained;
        /// <summary>Stage whose block was bumped (<see cref="OB_PowerEvent.Got"/>), else -1.</summary>
        public int stage;
        public bool shield;
        public float bootsLeft, rushLeft;
        /// <summary>Stages whose block this racer has used, one bit per stage.</summary>
        public ulong usedBlocks;
    }
}
