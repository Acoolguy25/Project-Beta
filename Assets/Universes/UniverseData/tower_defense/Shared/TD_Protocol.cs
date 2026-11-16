using FishNet.Broadcast;

namespace Universes.UniverseData.tower_defense {
    public enum TD_Phase : byte { Waiting, Build, Wave, Victory, Defeat }

    public struct TD_EnemySnap {
        public ushort id;
        public TD_EnemyKind kind;
        public float distance;
        /// <summary>Health fraction 0..255.</summary>
        public byte health;
        public bool slowed;
    }

    public struct TD_TowerSnap {
        public byte slot;
        public TD_TowerKind kind;
        public byte level;
        public string owner;
    }

    public struct TD_Shot {
        public byte slot;
        public ushort enemy;
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
    }

    public struct TD_ResultBroadcast : IBroadcast {
        public bool accepted;
        public string message;
    }
}
