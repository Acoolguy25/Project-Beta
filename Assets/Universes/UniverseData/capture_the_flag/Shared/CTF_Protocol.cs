using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    public enum CTF_Phase : byte { Waiting, Countdown, Playing, Results }
    public enum CTF_EventKind : byte { Taken, Dropped, Returned, Captured, Won, Draw }

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
        /// <summary>
        /// Counts up with every state the server sends. States travel unreliably and can arrive out of
        /// order: a client keeps only states newer than the last (see <see cref="CTF_Rules.IsNewer"/>).
        /// </summary>
        public uint sequence;
        /// <summary>Captures that win the match (the server's setting).</summary>
        public int capturesToWin;
        /// <summary>Time ran out level: the next capture wins (<see cref="secondsLeft"/> counts down the sudden death).</summary>
        public bool suddenDeath;
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
        /// <summary>Actor id of who did it (players: client id; bots: see <see cref="CTF_Rules.BotIdBase"/>); -1 for nobody.</summary>
        public int playerId;
        /// <summary>The score after this event, so a banner never shows a stale score from an older state.</summary>
        public int redScore, blueScore;
        /// <summary>Won and Draw: the match MVP (see <see cref="CTF_Scoring.Mvp"/>); false when nobody earned it.</summary>
        public bool hasMvp;
        public int mvpId;
        public string mvpName;
        public CTF_Team mvpTeam;
        public int mvpKills, mvpCaptures, mvpReturns;
    }

    public struct CTF_StateRequest : IBroadcast {
        public int known;
    }

    /// <summary>A player or bot hurt another (server-confirmed): hit marker, damage number, impact sparks. Ids are actor ids.</summary>
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
        /// <summary>For a gun kill: the gun the killer held (the kill feed names it).</summary>
        public CTF_Weapon weapon;
        /// <summary>The killer has mastered that gun (<see cref="CTF_Retention.MasteryKills"/> eliminations with it): a star in the feed.</summary>
        public bool killerMastered;
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

    /// <summary>
    /// One bot (or, in <see cref="CTF_RosterBroadcast.players"/>, one player) on the field: who it is,
    /// which spawned object is its body right now, the gun it holds and its match line for the board.
    /// </summary>
    public struct CTF_BotView {
        public int actorId;
        /// <summary>NetworkObject id of its current body (changes every life); -1 between lives.</summary>
        public int objectId;
        public string name;
        public CTF_Team team;
        /// <summary>The gun in its hands this life.</summary>
        public CTF_Weapon weapon;
        /// <summary>This match so far (the scoreboard's roster).</summary>
        public int kills, deaths, captures, returns;
        public bool alive;
        /// <summary>A player's late unlocks others see: the carried flag's trail and gold tracers.</summary>
        public bool flagTrail, goldTracers;
    }

    /// <summary>
    /// Everyone in play (bots and players), their guns and match lines, and the bots' tier: sent whenever
    /// someone spawns, respawns, leaves or scores (at most a few times a second), and on request.
    /// </summary>
    public struct CTF_RosterBroadcast : IBroadcast {
        public CTF_BotView[] bots;
        public CTF_BotSkill skill;
        /// <summary>Players per team the server fills to with bots (0 = no bots).</summary>
        public int teamSize;
        /// <summary>Today's twist (<see cref="CTF_Daily"/>) and what it does with the server's numbers, for the HUD chip.</summary>
        public CTF_DailyTwist twist;
        public string twistText;
        /// <summary>
        /// Whether <see cref="twist"/> and <see cref="twistText"/> are filled in: a bot's respawn re-sends
        /// the roster to everyone, and the twist only travels when it changed or to a client that asked.
        /// </summary>
        public bool hasTwist;
        /// <summary>Each team's bot tier (a team helping new players may play one tier above the other).</summary>
        public CTF_BotSkill redSkill, blueSkill;
        /// <summary>Bots each team plays short of the team size (facing a player who has not won yet).</summary>
        public int redShort, blueShort;
        /// <summary>The players in the match, in the same form (actor id = client id), for the board and their guns' look.</summary>
        public CTF_BotView[] players;
    }

    /// <summary>Client asks to switch to the next (step +1) or previous (-1) owned gun or perk.</summary>
    public struct CTF_LoadoutRequest : IBroadcast {
        public CTF_LoadoutSlot slot;
        public int step;
    }

    /// <summary>
    /// Your loadout: the gun and perk you picked, the gun you hold this life (the pick applies from your
    /// next life during a match) and the gun's stats, which your client applies to your weapon.
    /// </summary>
    public struct CTF_LoadoutBroadcast : IBroadcast {
        public CTF_Weapon weapon;
        public CTF_Perk perk;
        /// <summary>The gun in your hands now (differs from <see cref="weapon"/> until you respawn).</summary>
        public CTF_Weapon liveWeapon;
        public CTF_Perk livePerk;
        /// <summary>Stats of <see cref="liveWeapon"/>, with perks applied.</summary>
        public CTF_GunStats liveStats;
        /// <summary>Bit i = <see cref="CTF_Weapon"/> i owned (see <see cref="CTF_Loadouts.OwnedWeapons"/>).</summary>
        public byte ownedWeapons;
        public byte ownedPerks;
        /// <summary>The player changed a slot (show what changed), rather than a respawn re-sending it.</summary>
        public bool changed;
        public CTF_LoadoutSlot changedSlot;
        /// <summary>Today's twist; a gun it lends is in <see cref="ownedWeapons"/> for today only.</summary>
        public CTF_DailyTwist twist;
        /// <summary>The picked gun is only owned today, lent by the twist (not unlocked yet).</summary>
        public bool weaponLoaned;
    }

    /// <summary>Someone earned a medal. Sent to everyone; the HUD makes a fuss over your own.</summary>
    public struct CTF_MedalBroadcast : IBroadcast {
        public int actorId;
        public string name;
        public CTF_Team team;
        public CTF_Medal medal;
        /// <summary>XP paid (0 for bots).</summary>
        public int xp;
        /// <summary>The earner's kill streak after it.</summary>
        public int streak;
    }

    /// <summary>A perk just did something (Adrenaline's burst, Field Medic's heal). Sent to everyone; the HUD shows it on the player.</summary>
    public struct CTF_PerkBroadcast : IBroadcast {
        public int actorId;
        public CTF_Perk perk;
        public Vector3 position;
        /// <summary>How long the effect lasts (0 for an instant one).</summary>
        public float seconds;
    }

    /// <summary>
    /// Your own result, sent to each player just before the match's Won or Draw event: whether your
    /// team's win counts for you (a late joiner's does not, see <see cref="CTF_Rules.WinCounts"/>).
    /// </summary>
    public struct CTF_ResultNoteBroadcast : IBroadcast {
        public bool lateJoin;
        public bool winCounts;
    }

    /// <summary>A player's career in Flag Wars (what the save counts), sent to them for the standing goals.</summary>
    public struct CTF_CareerBroadcast : IBroadcast {
        public int level, maxLevel;
        public int wins, mvps, bestStreak, captures, matches, kills;
        /// <summary>Eliminations with each gun, in <see cref="CTF_Weapon"/> order (mastery at <see cref="CTF_Retention.MasteryKills"/>).</summary>
        public int[] gunKills;
    }
}
