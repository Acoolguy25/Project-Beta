using FishNet.Broadcast;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    public static class DI_Rules {
        public const int MaximumTroops = 50;
        public const float TurretRange = 24f;
        public const float TurretFireInterval = 0.7f;
    }

    // Both builds must use the same protocol version and field order.
    public struct DI_SendRequest : IBroadcast {
        public int sourceBaseId;
        public int targetBaseId;
    }

    public struct DI_StateBroadcast : IBroadcast {
        public int revision;
        public int yourClientId;
        public int yourTeamId;
        public int secondsRemaining;
        public bool matchEnded;
        public int winningTeamId;
        public Vector2[] basePositions;
        public int[] baseTroops;
        public int[] baseOwners;
        public int[] baseTeams;
        public int[] basePendingTroops;
        public int[] linkSources;
        public int[] linkTargets;
        public int[] dotIds;
        public Vector2[] dotPositions;
        public int[] dotTeams;
        public bool[] baseTurrets;
        public int[] turretShotSequences;
        public Vector2[] turretShotPositions;
    }
}
