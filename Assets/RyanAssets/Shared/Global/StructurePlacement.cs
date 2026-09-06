using UnityEngine;

namespace RyanAssets.Shared.Globals {
    /// <summary>Shared placement math used by the client preview and authoritative server spawn.</summary>
    public static class StructurePlacement {
        public const float GridSize = 4f;
        public const float GridRotationSnap = 90f;
        public const float GroundProbeHeight = 32f;
        public const float GroundProbeDistance = 64f;
        // Leave enough seam tolerance for grid-aligned structures with decorative
        // geometry that extends beyond their intended footprint (such as wall caps).
        public const float OverlapBoundsScale = 0.75f;

        public static Vector3 SnapToGrid(Vector3 position) {
            position.x = Mathf.Round(position.x / GridSize) * GridSize;
            position.z = Mathf.Round(position.z / GridSize) * GridSize;
            return position;
        }

        public static float SnapRotation(float yRotation) => Mathf.Round(yRotation / GridRotationSnap) * GridRotationSnap;

        public static Vector3 GetOverlapHalfExtents(Bounds bounds) =>
            Vector3.Max(bounds.extents * OverlapBoundsScale, Vector3.one * 0.05f);

        public static int GroundMask => ~LayerMask.GetMask("Character", "LocalCharacter", "Structure", "Ignore Raycast");

        public static bool TryFindGround(Vector3 snappedPosition, out Vector3 groundPoint) {
            Vector3 origin = snappedPosition + Vector3.up * GroundProbeHeight;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, GroundProbeDistance, GroundMask, QueryTriggerInteraction.Ignore)) {
                groundPoint = hit.point;
                return true;
            }

            groundPoint = default;
            return false;
        }

        public static bool TryPositionOnGround(GameObject instance, Vector3 groundPoint, float yRotation, out Bounds bounds) {
            if (instance == null) {
                bounds = default;
                return false;
            }

            Transform instanceTransform = instance.transform;
            instanceTransform.SetPositionAndRotation(groundPoint, Quaternion.Euler(0f, SnapRotation(yRotation), 0f));
            Physics.SyncTransforms();

            if (!TryGetBounds(instance, out bounds))
                return false;

            instanceTransform.position += Vector3.up * (groundPoint.y - bounds.min.y);
            Physics.SyncTransforms();
            return TryGetBounds(instance, out bounds);
        }

        public static bool TryGetBounds(GameObject instance, out Bounds bounds) {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            bounds = default;

            foreach (Renderer renderer in renderers) {
                if (!renderer.enabled)
                    continue;

                if (!hasBounds) {
                    bounds = renderer.bounds;
                    hasBounds = true;
                } else {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (hasBounds)
                return true;

            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) {
                if (!collider.enabled)
                    continue;

                if (!hasBounds) {
                    bounds = collider.bounds;
                    hasBounds = true;
                } else {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds;
        }
    }
}
