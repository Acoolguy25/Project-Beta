using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.murder_mystery {
    /// <summary>Where a Classic round is. Other modes only use Lobby, Hunt and Results.</summary>
    public enum MM_Phase : byte { Lobby, RoleReveal, Hunt, Meeting, Voting, Ejection, Results }

    /// <summary>A player's secret role. Matches the TeamColor the server uses (Red/Blue/Green).</summary>
    public enum MM_Role : byte { None, Innocent, Sheriff, Murderer, Spectator }

    /// <summary>One player's view of the round, sent to that player about twice a second.</summary>
    public struct MM_StateBroadcast : IBroadcast {
        public int round;
        public MM_Phase phase;
        /// <summary>True when the running mode is Classic (rounds, reports and accusations).</summary>
        public bool classic;
        public int secondsLeft;
        public MM_Role role;
        public bool alive;
        public int meetingsLeft;
        /// <summary>Seconds until a body can be reported again (after a meeting).</summary>
        public int reportCooldown;
        /// <summary>Id of the body close enough to report, or -1.</summary>
        public int reportableBody;
        public string reportableBodyName;
        /// <summary>Unreported bodies near this player, for the floor markers.</summary>
        public Vector3[] nearbyBodies;
        /// <summary>Murderers only: whole seconds until they may kill again (grace period, kill cooldown).</summary>
        public int killCooldown;
        /// <summary>True while a dead sheriff's gun lies on the floor waiting for an innocent to take it.</summary>
        public bool gunDropped;
        /// <summary>Where the dropped gun lies, when <see cref="gunDropped"/>.</summary>
        public Vector3 gunPosition;
    }

    /// <summary>Client asks to report the body it is standing next to.</summary>
    public struct MM_ReportRequest : IBroadcast {
        public int bodyId;
    }

    /// <summary>Sent to everyone when a body is reported and a meeting starts.</summary>
    public struct MM_MeetingBroadcast : IBroadcast {
        public int round;
        public int meeting;
        public string reporterName;
        public string victimName;
        /// <summary>Who was seen near the body when it happened. These are the accusation options.</summary>
        public string[] suspects;
        public int discussionSeconds;
    }

    /// <summary>The outcome of an accusation vote.</summary>
    public struct MM_EjectionBroadcast : IBroadcast {
        public int round;
        public bool ejected;
        public string name;
        public MM_Role role;
        public bool wasPlayer;
        public string headline;
        /// <summary>How long the outcome stays on screen.</summary>
        public int seconds;
    }

    /// <summary>
    /// Someone died close to this player (or it was them). Sent only to players within earshot, so a
    /// client never learns about kills it could not have heard.
    /// </summary>
    public struct MM_KillBroadcast : IBroadcast {
        public Vector3 position;
        /// <summary>Shot by a gun rather than stabbed.</summary>
        public bool byGun;
        /// <summary>The receiving player is the one who died.</summary>
        public bool youDied;
        /// <summary>The receiving player made the kill.</summary>
        public bool youKilled;
    }

    public struct MM_ResultEntry {
        public string name;
        public string username;
        public MM_Role role;
        public bool isPlayer;
        public bool survived;
        public bool ejected;
        public int kills;
    }

    /// <summary>End-of-round reveal of every role.</summary>
    public struct MM_ResultsBroadcast : IBroadcast {
        public int round;
        public string winner;
        public string summary;
        public MM_ResultEntry[] entries;
        public int secondsLeft;
    }

    public static class MM_Text {
        public static string RoleName(MM_Role role) => role switch {
            MM_Role.Innocent => "Innocent",
            MM_Role.Sheriff => "Sheriff",
            MM_Role.Murderer => "Murderer",
            MM_Role.Spectator => "Spectator",
            _ => "Unassigned",
        };

        public static string RoleGoal(MM_Role role) => role switch {
            MM_Role.Innocent => "Survive. Report bodies with E, vote out the murderer, and grab the sheriff's gun if it drops.",
            MM_Role.Sheriff => "Find the murderer and shoot them. Shooting an innocent jams your pistol. If you die, your gun drops for an innocent to take.",
            MM_Role.Murderer => "Eliminate everyone with your dagger. Blend in with the crowd and don't get voted out.",
            MM_Role.Spectator => "You joined mid-round. Watch, and play next round.",
            _ => "",
        };

        public static Color RoleColor(MM_Role role) => role switch {
            MM_Role.Innocent => new Color(0.35f, 0.85f, 0.45f),
            MM_Role.Sheriff => new Color(0.35f, 0.6f, 1f),
            MM_Role.Murderer => new Color(0.95f, 0.3f, 0.3f),
            _ => new Color(0.8f, 0.8f, 0.8f),
        };
    }
}
