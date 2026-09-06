using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    public enum CH_Phase : byte { Waiting, Investigation, Descent, Escape, Complete, Failed }
    public enum CH_Temperament : byte { LightSeeker, LightShy, Listener }
    public struct CH_InteractRequest : IBroadcast {
        public int seed;
        public int targetId;
        public int option;
    }
    public struct CH_ScareBroadcast : IBroadcast { public int seed, sequence; public byte kind; }
    public struct CH_InteractionResult : IBroadcast { public int seed; public bool accepted; public string message; }
    public struct CH_StateRequest : IBroadcast { public byte version; }
    public struct CH_PointState {
        public int id;
        public Vector3 position;
        public string title;
        public string area;
        public bool collected;
    }
    public struct CH_StateBroadcast : IBroadcast {
        public int seed;
        public CH_Phase phase;
        public string caseTitle;
        public string objective;
        public string dialogue;
        public int dialogueRevision;
        public string[] journal;
        public CH_PointState[] points;
        public int evidenceCount;
        public int relicCount;
        public int ritualStep;
        public int losses;
        public int lossLimit;
        public int secondsLeft;
        public int monsterId;
        public int completedCases;
        public string ending;
    }
}
