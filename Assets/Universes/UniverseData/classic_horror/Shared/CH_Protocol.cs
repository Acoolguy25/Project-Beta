using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    public enum CH_Phase : byte { Waiting, Investigation, Descent, Escape, Complete, Failed }
    public enum CH_Temperament : byte { LightSeeker, LightShy, Listener }
    /// <summary>Encounter scares shown to one investigator (<see cref="CH_ScareBroadcast.kind"/>).</summary>
    public enum CH_ScareKind : byte { Warning = 0, Discovery = 1, Fatal = 2 }
    /// <summary>Atmospheric events the server schedules for everyone (<see cref="CH_AmbientScareBroadcast"/>).</summary>
    public enum CH_AmbientScare : byte { DoorSlam = 0, DistantCry = 1, LightsOut = 2, ChapelBell = 3 }

    public struct CH_InteractRequest : IBroadcast {
        public int seed;
        public int targetId;
        public int option;
    }
    public struct CH_ScareBroadcast : IBroadcast { public int seed, sequence; public byte kind; }
    public struct CH_InteractionResult : IBroadcast { public int seed; public bool accepted, playObjectiveCollect; public string message; }
    public struct CH_StateRequest : IBroadcast { public byte version; }
    public struct CH_PointState {
        public int id;
        public Vector3 position;
        public string title;
        public string area;
        public bool collected;
        public bool navigationTarget;
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
        public int livesLost;
        public int livesPerPlayer;
        public int secondsLeft;
        public int monsterId;
        public int completedCases;
        public string ending;
        // Appended for the case summary on the ending card.
        public int memoryCount;
        public int diaryCount;
        public int diaryTotal;
        public int ritualMistakes;
        public int caseSeconds;
        public CH_Temperament temperament;
    }
    /// <summary>A scripted scare everyone hears: a door slamming, a cry, the lights failing, the chapel bell.</summary>
    public struct CH_AmbientScareBroadcast : IBroadcast {
        public int seed;
        public CH_AmbientScare kind;
        public Vector3 position;
        public float seconds;
    }
}
