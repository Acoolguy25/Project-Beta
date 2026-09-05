using FishNet.Broadcast;
using UnityEngine;

namespace RyanAssets.Shared.Requests {
    /// <summary>Client request for the server to validate and place a registered structure prefab.</summary>
    public struct StructurePlacementRequest : IBroadcast {
        public ushort prefabId;
        public Vector3 position;
        public float yRotation;
    }
}
