using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    public enum CTF_Phase : byte { Waiting, Countdown, Playing, Results }
    public enum CTF_EventKind : byte { Taken, Dropped, Returned, Captured, Won }

    // Wire contracts: only ever append fields, never reorder or retype them.

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
        /// <summary>Bit i set = <c>CTF_Map.powerUps[i]</c> can be taken (see <see cref="CTF_Rules.IsReady"/>).</summary>
        public uint powerUpsReady;
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

    /// <summary>A player hurt another player (server-confirmed): hit marker, damage number, impact sparks.</summary>
    public struct CTF_HitBroadcast : IBroadcast {
        public int attackerId;
        public int victimId;
        /// <summary>The victim's position when hit (feet).</summary>
        public Vector3 position;
        public int damage;
        public bool killed;
    }

    /// <summary>Someone died: the kill feed, the killer's kill sound and the victim's respawn countdown.</summary>
    public struct CTF_KillBroadcast : IBroadcast {
        /// <summary>-1 when nobody gets the credit (fall, own death).</summary>
        public int killerId;
        public string killerName;
        public CTF_Team killerTeam;
        public int victimId;
        public string victimName;
        public CTF_Team victimTeam;
        public CTF_KillCause cause;
        /// <summary>Seconds until the victim is back.</summary>
        public float respawnSeconds;
    }

    /// <summary>A power-up was taken.</summary>
    public struct CTF_PowerUpBroadcast : IBroadcast {
        /// <summary>Index into <c>CTF_Map.powerUps</c>.</summary>
        public int index;
        public CTF_PowerUpKind kind;
        public int playerId;
        /// <summary>For a speed boost: how long it lasts.</summary>
        public float seconds;
    }
}
