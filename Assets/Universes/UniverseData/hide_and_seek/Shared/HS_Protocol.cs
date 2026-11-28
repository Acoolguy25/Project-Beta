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
        /// <summary>Length of the current phase, for the HUD's countdown bar.</summary>
        public int phaseSeconds;
        /// <summary>The map event running now (<see cref="HS_RoundEvent.None"/> when none), so late joiners see it too.</summary>
        public HS_RoundEvent activeEvent;
        /// <summary>Seconds the active event has left.</summary>
        public int eventSecondsLeft;
        /// <summary>Today's featured event (<see cref="HS_Daily"/>): it comes most rounds today and pays double for its play.</summary>
        public HS_RoundEvent featuredEvent;
        /// <summary>Which hiding-spot doors are shut (bit i = <see cref="HS_Map.doors"/>[i]), so a late joiner's doors match.</summary>
        public ulong closedDoors;
        /// <summary>The glowing pumpkins out now (<see cref="HS_Pumpkins"/>), so a late joiner or a missed message still matches.</summary>
        public HS_Pumpkin[] pumpkins;
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
        /// <summary>Set when the message is about an ability (a refusal, a radar warning): it shows above the ability bar, not mid-screen.</summary>
        public HS_Ability ability;
    }

    /// <summary>A hider asks to taunt: a sound plays at their spot for everyone, and it earns XP.</summary>
    public struct HS_TauntRequest : IBroadcast {
        public int round;
    }

    /// <summary>Someone taunted (sent to everyone, so seekers can hear where from).</summary>
    public struct HS_TauntBroadcast : IBroadcast {
        public int clientId;
        public UnityEngine.Vector3 position;
        /// <summary>XP the taunter earned; more when a seeker was close (see <see cref="HS_Rules.TauntReward"/>).</summary>
        public int xp;
        public bool brave;
        /// <summary>Taunts the taunter has used this round, so their HUD's count survives a reconnect or a late state.</summary>
        public int used;
        /// <summary>
        /// Not a taunt: the server is correcting the taunter's count after refusing a request (sent to
        /// that client only, with <see cref="used"/> and no XP).
        /// </summary>
        public bool correction;
    }

    /// <summary>Where a hider who was never found spent the end of the round.</summary>
    public struct HS_HidingSpot {
        public int clientId;
        public string name;
        public UnityEngine.Vector3 position;
    }

    /// <summary>Sent when a round ends: every survivor's hiding spot, for the results screen.</summary>
    public struct HS_RevealBroadcast : IBroadcast {
        public int round;
        public HS_HidingSpot[] spots;
        /// <summary>How long the results show, so the clients keep the names up that long.</summary>
        public int seconds;
    }

    // ---------------------------------------------------------------- retention pass (2026-11-23): abilities, points, events

    /// <summary>A role ability. Hiders: Dash, Decoy, Blend. Seekers (and found hiders): Rush, Radar, Flashlight.</summary>
    public enum HS_Ability : byte { None, Dash, Decoy, Blend, Rush, Radar, Flashlight }

    /// <summary>Something that shakes up a round while the seekers hunt.</summary>
    public enum HS_RoundEvent : byte { None, Blackout, Frenzy, Shuffle }

    /// <summary>A player asks to use one of their abilities; the server checks role, phase, unlock and cooldown.</summary>
    public struct HS_AbilityRequest : IBroadcast {
        public int round;
        public HS_Ability ability;
        /// <summary>Where the player is looking (camera forward): the decoy's throw and the flashlight's beam.</summary>
        public UnityEngine.Vector3 direction;
    }

    /// <summary>One slot of a player's ability bar, as the server sees it.</summary>
    public struct HS_AbilitySlot {
        public HS_Ability ability;
        public bool unlocked;
        /// <summary>Level of this game's track that unlocks it (shown on a locked slot).</summary>
        public int unlockLevel;
        /// <summary>The ability's upgrade is owned too.</summary>
        public bool upgraded;
        /// <summary>Full cooldown (seconds), for the slot's recharge sweep.</summary>
        public float cooldown;
        /// <summary>Seconds until it can be used again (0 = ready).</summary>
        public float readyIn;
        /// <summary>How long an activation lasts (0 = instant).</summary>
        public float duration;
        /// <summary>Seconds the current activation has left (0 = not active).</summary>
        public float activeLeft;
    }

    /// <summary>Server -> one player: their ability bar (sent when their role, unlocks or cooldowns change).</summary>
    public struct HS_AbilityStateBroadcast : IBroadcast {
        public int round;
        /// <summary>The slots for the player's side, in key order (1, 2, 3); empty outside a round.</summary>
        public HS_AbilitySlot[] slots;
        /// <summary>Taunts the player may make this round (more with the Taunt Master unlock); 0 from an older server means the default.</summary>
        public int tauntAllowance;
    }

    /// <summary>
    /// Someone used an ability: everyone gets the visible part (a dash, a decoy's noise, a flashlight
    /// beam, a hider blending in); a radar's findings go only to the seeker who used it.
    /// </summary>
    public struct HS_AbilityBroadcast : IBroadcast {
        public int clientId;
        public HS_Ability ability;
        /// <summary>Where it happened: the user's feet, or the decoy's spot.</summary>
        public UnityEngine.Vector3 position;
        public UnityEngine.Vector3 direction;
        /// <summary>How long the effect lasts on clients (0 = a moment; a blend sent with 0 has ended).</summary>
        public float seconds;
        /// <summary>Flashlight: the hiders it dazzled (client ids).</summary>
        public int[] targets;
        /// <summary>Radar: rough positions of what it picked up (hiders and decoys look alike).</summary>
        public UnityEngine.Vector3[] positions;
        /// <summary>Range or radius, for the visual (world units).</summary>
        public float range;
        /// <summary>Flashlight: the beam's full width (degrees), so the client draws the cone the server tested.</summary>
        public float angle;
        /// <summary>Dash / Rush: the user owns the Ghost Trail cosmetic, so everyone sees a trail behind them.</summary>
        public bool flair;
    }

    /// <summary>Server -> one player: round points just earned, with the reason ("Close call") and the round total.</summary>
    public struct HS_PointsBroadcast : IBroadcast {
        public int points;
        public int total;
        public string reason;
        /// <summary>Points from something the player did on purpose (a tag, a brave taunt) get the big popup.</summary>
        public bool highlight;
    }

    /// <summary>Server -> one player at the end of a round: their points, records, streak and the round's MVP.</summary>
    public struct HS_RoundRecapBroadcast : IBroadcast {
        public int round;
        /// <summary>False for someone who joined after the round's roles were dealt (only the MVP applies).</summary>
        public bool played;
        public bool won;
        public int points;
        public int bestPoints;
        public bool newBest;
        public int winStreak;
        public int bestWinStreak;
        public int mvpClientId;
        public string mvpName;
        public int mvpPoints;
    }

    /// <summary>Server -> everyone: a map event starts (<see cref="started"/>) or ends.</summary>
    public struct HS_EventBroadcast : IBroadcast {
        public int round;
        public HS_RoundEvent kind;
        public bool started;
        public int seconds;
    }

    // ---------------------------------------------------------------- closet doors (2026-11-28)

    /// <summary>A player asks to open or shut a hiding spot's door (<see cref="HS_Doors"/> decides).</summary>
    public struct HS_DoorRequest : IBroadcast {
        public int round;
        /// <summary>Index into <see cref="HS_Map.doors"/>.</summary>
        public int door;
        /// <summary>The state asked for, so a request that crossed someone else's is not a toggle back.</summary>
        public bool open;
    }

    /// <summary>Server -> everyone: a door was opened or shut, by whom, and whether a seeker found someone behind it.</summary>
    public struct HS_DoorBroadcast : IBroadcast {
        public int door;
        public bool open;
        public int clientId;
        /// <summary>A seeker opened it with a hider inside.</summary>
        public bool found;
    }

    // ---------------------------------------------------------------- pumpkin hunt (2026-11-28)

    /// <summary>
    /// Server -> everyone: a pumpkin lit up (<see cref="take"/> None) or was taken. Everyone hears a take where it
    /// happened; seekers also see where a hider grabbed one.
    /// </summary>
    public struct HS_PumpkinBroadcast : IBroadcast {
        public int id;
        public UnityEngine.Vector3 position;
        public HS_PumpkinTake take;
        /// <summary>Who took it (-1 when it lit up).</summary>
        public int clientId;
        /// <summary>Pumpkins the taker has taken this round, counting this one (their "2/3" on the HUD).</summary>
        public int count;
    }
}
