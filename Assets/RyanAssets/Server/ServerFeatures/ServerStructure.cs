using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Globals;
using RyanAssets.Shared.Requests;
using UnityEngine;

namespace RyanAssets.Server.ServerFeatures {
    /// <summary>Authoritative receiver and validator for client structure-placement broadcasts.</summary>
    public static class ServerStructure {
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
            if (structurePrefab == null || !structurePrefab.TryGetComponent(out StructureComponent _))
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

            bool overlapsStructure = Physics.CheckBox(
                bounds.center,
                StructurePlacement.GetOverlapHalfExtents(bounds),
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
        }

        private static bool IsFinite(Vector3 position) =>
            float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z);
    }
}
