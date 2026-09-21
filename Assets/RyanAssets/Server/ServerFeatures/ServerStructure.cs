using System;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Globals;
using RyanAssets.Shared.Requests;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RyanAssets.Server.ServerFeatures {
    /// <summary>Authoritative receiver and validator for client structure-placement broadcasts.</summary>
    public static class ServerStructure {
        /// <summary>
        /// Optional per-game gate applied before anything is instantiated. Return false to reject the
        /// placement outright - a game mode uses this to enforce costs, build limits, or territory.
        /// </summary>
        public static Func<NetworkConnection, StructureComponent, bool> CanPlaceFunction;

        /// <summary>
        /// Raised once a validated structure has spawned, with the prefab's component and the live
        /// instance. A game mode uses this to charge the placement, record its owner, and start any
        /// construction phase.
        /// </summary>
        public static Action<NetworkConnection, StructureComponent> StructurePlaced;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHooks() {
            // Cleared before any scene loads so a previous session's game mode cannot keep gating
            // placements after a domain reload.
            CanPlaceFunction = null;
            StructurePlaced = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init() {
            InstanceFinder.ServerManager.RegisterBroadcast<StructurePlacementRequest>(OnPlacementRequest, true);
        }

        private static void OnPlacementRequest(
            NetworkConnection sender,
            StructurePlacementRequest request,
            Channel channel) {
            SharedGlobalEvents sharedEvents = SharedGlobalEvents.Instance;
            if (sharedEvents == null ||
                !sharedEvents.CanBuild.Value ||
                sender == null ||
                !sharedEvents.Builds.Contains(request.prefabId) ||
                InstanceFinder.NetworkManager == null)
                return;

            NetworkObject structurePrefab = InstanceFinder.NetworkManager.SpawnablePrefabs.GetObject(
                asServer: true,
                request.prefabId);
            if (structurePrefab == null || !structurePrefab.TryGetComponent(out StructureComponent prefabStructure))
                return;

            if (CanPlaceFunction != null && !CanPlaceFunction(sender, prefabStructure))
                return;

            Vector3 snappedPosition = StructurePlacement.SnapToGrid(request.position);
            if (!IsFinite(snappedPosition) ||
                !StructurePlacement.TryFindGround(snappedPosition, out Vector3 groundPoint) ||
                Mathf.Abs(groundPoint.y - request.position.y) > StructurePlacement.GroundProbeHeight)
                return;

            GameObject clone = Object.Instantiate(structurePrefab.gameObject);
            if (!StructurePlacement.TryPositionOnGround(clone, groundPoint, request.yRotation, out Bounds bounds)) {
                Object.Destroy(clone);
                return;
            }

            Collider[] cloneColliders = clone.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in cloneColliders)
                collider.enabled = false;
            Physics.SyncTransforms();

            StructurePlacement.GetOverlapVolume(
                bounds, groundPoint, out Vector3 overlapCenter, out Vector3 overlapHalfExtents);
            bool overlapsStructure = Physics.CheckBox(
                overlapCenter,
                overlapHalfExtents,
                Quaternion.identity,
                LayerMask.GetMask("Structure"),
                QueryTriggerInteraction.Ignore);

            foreach (Collider collider in cloneColliders)
                collider.enabled = true;

            if (overlapsStructure) {
                Object.Destroy(clone);
                return;
            }

            InstanceFinder.ServerManager.Spawn(clone);
            // Raised after the spawn so the game mode acts on live SyncVars rather than on a prefab
            // instance that has not been initialized by FishNet yet.
            if (clone.TryGetComponent(out StructureComponent placedStructure))
                StructurePlaced?.Invoke(sender, placedStructure);
        }

        private static bool IsFinite(Vector3 position) =>
            float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z);
    }
}
