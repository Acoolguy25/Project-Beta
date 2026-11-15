using FishNet.Broadcast;

namespace Universes.UniverseData.hide_and_seek {
    public enum HS_Phase : byte { Waiting, Intermission, Hiding, Seeking, Results }
    public enum HS_Role : byte { None, Hider, Seeker, Caught }

    public struct HS_PlayerRole {
        public int clientId;
        public HS_Role role;
        public string name;
    }

    /// <summary>Round status and everyone's role, on change and once a second.</summary>
    public struct HS_StateBroadcast : IBroadcast {
        public HS_Phase phase;
        public int round;
        public int secondsLeft;
        public int hidersLeft;
        public int hidersAtStart;
        public string headline;
        public HS_PlayerRole[] roles;
    }

    /// <summary>A seeker tries to tag whoever is closest in front of them.</summary>
    public struct HS_TagRequest : IBroadcast {
        public int round;
    }

    /// <summary>Someone was tagged (for the kill feed and sounds).</summary>
    public struct HS_TagBroadcast : IBroadcast {
        public int seekerId;
        public int hiderId;
        public string seekerName;
        public string hiderName;
        public int hidersLeft;
    }

    /// <summary>Late in a round, seekers get a rough fix on every hider.</summary>
    public struct HS_PingBroadcast : IBroadcast {
        public UnityEngine.Vector3[] positions;
    }

    public struct HS_StateRequest : IBroadcast {
        public int known;
    }

    public struct HS_ResultBroadcast : IBroadcast {
        public bool accepted;
        public string message;
    }
}
