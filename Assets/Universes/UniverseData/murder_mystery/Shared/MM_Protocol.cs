using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.murder_mystery {
    /// <summary>Where a round is. Other modes only use Lobby, Hunt and Results.</summary>
    public enum MM_Phase : byte { Lobby, RoleReveal, Hunt, Results }

    /// <summary>A player's secret role. Matches the TeamColor the server uses (Red/Blue/Green).</summary>
    public enum MM_Role : byte { None, Innocent, Sheriff, Murderer, Spectator }

    /// <summary>How a Classic round ended, for the results card's headline and colour.</summary>
    public enum MM_Outcome : byte {
        None,
        /// <summary>The murderer was shot (or otherwise died).</summary>
        MurdererStopped,
        /// <summary>Time ran out with innocents still alive.</summary>
        InnocentsSurvived,
        /// <summary>Nobody but the murderers is left.</summary>
        MurdererWon,
    }

    /// <summary>One player's view of the round, sent to that player about twice a second.</summary>
    public struct MM_StateBroadcast : IBroadcast {
        public int round;
        public MM_Phase phase;
        /// <summary>True when the running mode is Classic (roles, coins, the gun drop).</summary>
        public bool classic;
        public int secondsLeft;
        public MM_Role role;
        public bool alive;
        /// <summary>Sheriff by picking up the dead sheriff's gun.</summary>
        public bool hero;
        /// <summary>Living characters that are not murderers: the people the murderer still has to get.</summary>
        public int survivorsLeft;
        /// <summary>Murderers only: whole seconds until they may kill again (grace period, kill cooldown).</summary>
        public int killCooldown;
        /// <summary>Murderers only: seconds until the knife can be thrown again (0 = ready).</summary>
        public float throwCooldown;
        /// <summary>Coins in this player's bag this round, and how many the bag holds.</summary>
        public int coins, coinCap;
        /// <summary>True while a dead sheriff's gun lies on the floor waiting for an innocent to take it.</summary>
        public bool gunDropped;
        /// <summary>Where the dropped gun lies, when <see cref="gunDropped"/>.</summary>
        public Vector3 gunPosition;
        /// <summary>This player's chance (0-100) of being dealt murderer / sheriff next round.</summary>
        public byte murdererChance, sheriffChance;
    }

    /// <summary>Client (a murderer holding the knife) asks to throw it at a point in the world.</summary>
    public struct MM_ThrowKnifeRequest : IBroadcast {
        public Vector3 target;
    }

    /// <summary>A knife left someone's hand. Sent to everyone; the flight is simulated by the server.</summary>
    public struct MM_KnifeThrownBroadcast : IBroadcast {
        public int throwId;
        public Vector3 origin;
        public Vector3 direction;
        public float speed;
        public float maxDistance;
        /// <summary>The thrower's knife skin (<see cref="MM_KnifeSkins"/>).</summary>
        public byte skin;
    }

    /// <summary>A thrown knife stopped: in a wall, in someone, or at the end of its range.</summary>
    public struct MM_KnifeLandedBroadcast : IBroadcast {
        public int throwId;
        public Vector3 position;
        public bool hitCharacter;
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

    /// <summary>The receiving player picked up a coin.</summary>
    public struct MM_CoinBroadcast : IBroadcast {
        public Vector3 position;
        public int coins, coinCap;
    }

    /// <summary>A moment everyone hears about: the gun dropped or was taken, the last survivor, a sheriff's fatal mistake.</summary>
    public struct MM_AnnounceBroadcast : IBroadcast {
        public MM_Announcement kind;
        public string name;
    }

    public enum MM_Announcement : byte {
        None,
        /// <summary>The sheriff died and their gun is on the floor.</summary>
        GunDropped,
        /// <summary>Someone picked the gun up (<see cref="MM_AnnounceBroadcast.name"/> is not sent: heroes stay secret).</summary>
        GunTaken,
        /// <summary>Only one survivor is left.</summary>
        LastSurvivor,
        /// <summary>The sheriff shot an innocent and paid for it.</summary>
        SheriffMistake,
        /// <summary>No players are left alive: the NPCs finish the round quickly.</summary>
        PlayersOut,
    }

    /// <summary>Knife skins as the server tells clients about them: the knife tool's object id and its skin.</summary>
    public struct MM_KnifeSkinsBroadcast : IBroadcast {
        public int[] toolObjectIds;
        public byte[] skins;
    }

    public struct MM_ResultEntry {
        public string name;
        public string username;
        public MM_Role role;
        public bool isPlayer;
        public bool survived;
        public int kills;
        /// <summary>Took the dead sheriff's gun.</summary>
        public bool hero;
        public int coins;
    }

    /// <summary>End-of-round reveal of every role, personalised for the receiving player.</summary>
    public struct MM_ResultsBroadcast : IBroadcast {
        public int round;
        public MM_Outcome outcome;
        public string headline;
        public string summary;
        public MM_ResultEntry[] entries;
        public int secondsLeft;
        /// <summary>Who played the big parts: the murderer(s), the sheriff, the hero and whoever stopped the murderer.</summary>
        public string murdererName, sheriffName, heroName, stoppedBy;
        /// <summary>The receiving player's round: whether their side won, coins bagged and XP earned.</summary>
        public bool youWon;
        public int yourCoins, yourXp;
        /// <summary>Lifetime coins collected in murder mystery, which unlock knife skins.</summary>
        public int lifetimeCoins;
        public byte murdererChance, sheriffChance;
    }

    public static class MM_Text {
        public static string RoleName(MM_Role role) => role switch {
            MM_Role.Innocent => "Innocent",
            MM_Role.Sheriff => "Sheriff",
            MM_Role.Murderer => "Murderer",
            MM_Role.Spectator => "Spectator",
            _ => "Unassigned",
        };

        public static string RoleName(MM_Role role, bool hero) => hero && role == MM_Role.Sheriff ? "Hero" : RoleName(role);

        public static string RoleGoal(MM_Role role) => role switch {
            MM_Role.Innocent => "Stay alive and grab coins. If the sheriff falls, take their gun and become the hero.",
            MM_Role.Sheriff => "Find the murderer and shoot them. Shoot an innocent and you die too.",
            MM_Role.Murderer => "Eliminate everyone before time runs out. Stab up close, or press E to throw your knife.",
            MM_Role.Spectator => "You joined mid-round. Watch, and play next round.",
            _ => "",
        };

        public static string OutcomeHeadline(MM_Outcome outcome) => outcome switch {
            MM_Outcome.MurdererStopped => "Innocents win",
            MM_Outcome.InnocentsSurvived => "Innocents win",
            MM_Outcome.MurdererWon => "Murderer wins",
            _ => "Round over",
        };

        public static bool MurdererWon(MM_Outcome outcome) => outcome == MM_Outcome.MurdererWon;

        public static Color RoleColor(MM_Role role) => role switch {
            MM_Role.Innocent => new Color(0.35f, 0.85f, 0.45f),
            MM_Role.Sheriff => new Color(0.35f, 0.6f, 1f),
            MM_Role.Murderer => new Color(0.95f, 0.3f, 0.3f),
            _ => new Color(0.8f, 0.8f, 0.8f),
        };

        /// <summary>The hero's gold, so a picked-up gun reads differently from the dealt sheriff.</summary>
        public static readonly Color HeroColor = new(1f, 0.78f, 0.25f);

        public static Color RoleColor(MM_Role role, bool hero) => hero && role == MM_Role.Sheriff ? HeroColor : RoleColor(role);

        /// <summary>"2:05" for the top bar.</summary>
        public static string Clock(int seconds) {
            seconds = Mathf.Max(0, seconds);
            return $"{seconds / 60}:{seconds % 60:00}";
        }
    }
}
