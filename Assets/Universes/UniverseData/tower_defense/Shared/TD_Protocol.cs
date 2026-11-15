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
        /// <summary>Enemies killed since the last snapshot (for explosions).</summary>
        public ushort[] killed;
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
        public TD_TowerSnap[] towers;
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

    public struct TD_ResultBroadcast : IBroadcast {
        public bool accepted;
        public string message;
    }
}
