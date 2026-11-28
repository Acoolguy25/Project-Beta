using FishNet.Broadcast;

// Wire contract of Tower Defense. APPEND-ONLY: never reorder, retype or remove a field, never renumber
// an enum; add fields at the end of a struct and new broadcasts at the end of the file.
namespace Universes.UniverseData.tower_defense {
    /// <summary>Vote = players pick the mode before wave 1 (appended after the original phases).</summary>
    public enum TD_Phase : byte { Waiting, Build, Wave, Victory, Defeat, Vote }

    public struct TD_EnemySnap {
        public ushort id;
        public TD_EnemyKind kind;
        public float distance;
        /// <summary>Health fraction 0..255.</summary>
        public byte health;
        /// <summary>Kept on the wire for older clients; <see cref="slow"/> says the same and more.</summary>
        public bool slowed;
        /// <summary>Current speed multiplier 0..255 (255 = not slowed, 0 = stunned), from <see cref="TD_Rules.SlowFactor"/>.</summary>
        public byte slow;
        /// <summary>Shield left as a fraction 0..255 of the enemy's maximum health (0 = none or broken).</summary>
        public byte shield;
    }

    public struct TD_TowerSnap {
        public byte slot;
        public TD_TowerKind kind;
        public byte level;
        /// <summary>The builder's display name (not unique; for showing only).</summary>
        public string owner;
        /// <summary>The builder's connection id while that player is still here; -1 when anyone may sell the tower.</summary>
        public int ownerId;
        /// <summary>What selling it pays now (the builder's perks decide the rate).</summary>
        public int refund;
        /// <summary>Cosmetic: the builder has the gold trim unlock.</summary>
        public bool gold;
        /// <summary>The builder's towers hit harder (Veteran Crews), for the info card.</summary>
        public bool veteran;
        /// <summary>What it shoots at. Appended 2026-11-28; First from older servers.</summary>
        public TD_TargetMode target;
        /// <summary>Enemies it destroyed this game. Appended 2026-11-28.</summary>
        public int kills;
        /// <summary>Damage it dealt this game (health and shield, rounded). Appended 2026-11-28.</summary>
        public int damage;
    }

    public struct TD_Shot {
        public byte slot;
        public ushort enemy;
        /// <summary>A chain jump: the enemy the arc leaps from (0 = it leaves the tower).</summary>
        public ushort from;
        /// <summary>The hit bounced off a shield and did nothing (<see cref="TD_Rules.ShieldBlockBelow"/>). Appended in try 2.</summary>
        public bool blocked;
    }

    /// <summary>Simulation snapshot, about ten times a second.</summary>
    public struct TD_SnapshotBroadcast : IBroadcast {
        public float time;
        public TD_EnemySnap[] enemies;
        public TD_Shot[] shots;
        /// <summary>Enemies destroyed by towers since the last snapshot (explosions and reward popups).</summary>
        public ushort[] killed;
        /// <summary>Enemies that reached the base since the last snapshot (they hit the base instead of exploding for cash).</summary>
        public ushort[] leaked;
        /// <summary>Enemies a medic healed since the last snapshot.</summary>
        public ushort[] healed;
        /// <summary>Enemies whose shield broke since the last snapshot.</summary>
        public ushort[] shieldsBroken;
    }

    /// <summary>Game status and towers, on change and once a second.</summary>
    public struct TD_StateBroadcast : IBroadcast {
        public TD_Phase phase;
        public int wave;
        public int secondsLeft;
        public int cash;
        public int baseHealth;
        public int enemiesLeft;
        public string headline;
        /// <summary>Players who voted to skip the build countdown, out of <see cref="activePlayers"/>.</summary>
        public byte readyPlayers;
        public byte activePlayers;
        public TD_TowerSnap[] towers;
        /// <summary>Cash the team earned by starting the current wave early (0 when the countdown ran out).</summary>
        public int earlyBonus;
        /// <summary>Simulation speed: 1 normally, <see cref="TD_Rules.FastSpeed"/> while most players vote for fast forward.</summary>
        public float speed;
        /// <summary>Players voting for fast forward, out of <see cref="activePlayers"/>.</summary>
        public byte fastPlayers;
        /// <summary>Whether this server lets players vote for fast forward at all.</summary>
        public bool fastForwardAllowed;
        /// <summary>The mode being played (or leading the vote while <see cref="phase"/> is Vote).</summary>
        public TD_Difficulty mode;
        /// <summary>Votes per mode (index = <see cref="TD_Difficulty"/>) during the mode vote.</summary>
        public byte[] modeVotes;
        /// <summary>Waves to win this game; 0 in Endless.</summary>
        public int waveCount;
        /// <summary>The day's modifier this game plays (or, in the vote, today's). Appended in try 2.</summary>
        public TD_Modifier modifier;
    }

    /// <summary>Build (on an empty slot) or upgrade (on a tower).</summary>
    public struct TD_BuildRequest : IBroadcast {
        public byte slot;
        public TD_TowerKind kind;
    }

    public struct TD_StateRequest : IBroadcast {
        public int known;
    }

    /// <summary>Wave countdowns can be skipped once everyone has voted ready.</summary>
    public struct TD_ReadyRequest : IBroadcast {
        public bool ready;
    }

    /// <summary>Sell the tower on a pad for <see cref="TD_Rules.SellRefund"/>.</summary>
    public struct TD_SellRequest : IBroadcast {
        public byte slot;
    }

    /// <summary>Vote for (or against) running the simulation at <see cref="TD_Rules.FastSpeed"/>.</summary>
    public struct TD_SpeedRequest : IBroadcast {
        public bool fast;
    }

    public enum TD_TowerEvent : byte { Built, Upgraded, Sold }

    /// <summary>A tower was built, upgraded or sold (sent to everyone, for effects and popups).</summary>
    public struct TD_TowerEventBroadcast : IBroadcast {
        public TD_TowerEvent kind;
        public byte slot;
        public TD_TowerKind tower;
        /// <summary>The tower's level after the event (before it, for a sale).</summary>
        public byte level;
        /// <summary>Cash spent (built, upgraded) or refunded (sold).</summary>
        public int amount;
    }

    /// <summary>The receiving player's saved tower defense record, sent when it loads and when a record falls.</summary>
    public struct TD_ProfileBroadcast : IBroadcast {
        public int bestWave;
        public long totalKills;
        public int gamesWon;
        public int gamesPlayed;
        /// <summary>True when <see cref="bestWave"/> was just beaten.</summary>
        public bool newBestWave;
        /// <summary>Best stars won per mode (index = <see cref="TD_Difficulty"/>; Endless has none).</summary>
        public byte[] bestStars;
        /// <summary>Furthest wave reached in Endless.</summary>
        public int bestEndlessWave;
        /// <summary>Modes this player may vote for (index = <see cref="TD_Difficulty"/>).</summary>
        public bool[] modeUnlocked;
        /// <summary>True when <see cref="bestEndlessWave"/> was just beaten.</summary>
        public bool newBestEndless;
    }

    public struct TD_ResultBroadcast : IBroadcast {
        public bool accepted;
        public string message;
    }
}

namespace Universes.UniverseData.tower_defense {
    /// <summary>Vote for the mode of the next game (during the Vote phase; only modes the player has unlocked count).</summary>
    public struct TD_ModeVoteRequest : IBroadcast {
        public TD_Difficulty mode;
    }

    /// <summary>Use one of the player's abilities (during a wave).</summary>
    public struct TD_AbilityRequest : IBroadcast {
        public TD_Ability ability;
    }

    /// <summary>A player called an ability (sent to everyone, for the jet, blasts and banner).</summary>
    public struct TD_AbilityBroadcast : IBroadcast {
        public TD_Ability ability;
        /// <summary>Airstrike: road distance of the strike's centre.</summary>
        public float distance;
        public string caster;
        /// <summary>Enemies around the target when called (airstrike) or stunned (EMP).</summary>
        public int hits;
        /// <summary>Simulation seconds until the Airstrike's bombs land (the client times the jet to it; 0 for EMP).</summary>
        public float delay;
        /// <summary>Share of full power (<see cref="TD_Abilities.RepeatScale"/>: lower for a second call in the same wave). Appended in try 2; 0 from older servers = full.</summary>
        public float power;
    }

    /// <summary>The receiving player's abilities: which they own and how many waves until each is ready.</summary>
    public struct TD_AbilityStateBroadcast : IBroadcast {
        /// <summary>Index = <see cref="TD_Ability"/>.</summary>
        public bool[] unlocked;
        /// <summary>Waves until ready, index = <see cref="TD_Ability"/> (0 = ready).</summary>
        public byte[] wavesLeft;
    }
}

namespace Universes.UniverseData.tower_defense {
    /// <summary>Change what the tower on a pad shoots at (its builder, or anyone for an unclaimed tower).</summary>
    public struct TD_TargetRequest : IBroadcast {
        public byte slot;
        public TD_TargetMode mode;
    }
}
