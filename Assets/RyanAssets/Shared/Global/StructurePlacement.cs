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
        // The occupancy test starts just above the ground so a structure standing on the terrain
        // does not register against it, while still covering the whole cell it sits in.
        public const float OverlapGroundClearance = 0.05f;

        /// <summary>
        /// Snaps a structure's centre so its footprint covers whole grid cells.
        /// <para>
        /// A structure an even number of cells wide is centred on a grid line and one an odd number
        /// wide is centred in the middle of a cell. Snapping every centre to a grid line instead put
        /// one- and three-cell structures half a cell out of step with two-cell ones, so buildings of
        /// different sizes never lined up and read as overlapping their neighbours' cells.
        /// </para>
        /// </summary>
        public static Vector3 SnapToGrid(Vector3 position, int footprintCells) {
            float offset = Mathf.Max(1, footprintCells) % 2 == 1 ? GridSize * 0.5f : 0f;
            position.x = Mathf.Round((position.x - offset) / GridSize) * GridSize + offset;
            position.z = Mathf.Round((position.z - offset) / GridSize) * GridSize + offset;
            return position;
        }

        /// <summary>
        /// How many grid cells a structure spans on its wider horizontal side, read from its bounds.
        /// Structures are authored to span whole cells, so rounding absorbs small decorative overhang.
        /// </summary>
        public static int GetFootprintCells(Bounds bounds) =>
            Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(bounds.size.x, bounds.size.z) / GridSize));

        /// <summary>The footprint of an instantiated structure, or one cell when it has no bounds.</summary>
        public static int GetFootprintCells(GameObject instance) =>
            TryGetBounds(instance, out Bounds bounds) ? GetFootprintCells(bounds) : 1;

        public static float SnapRotation(float yRotation) => Mathf.Round(yRotation / GridRotationSnap) * GridRotationSnap;

        public static Vector3 GetOverlapHalfExtents(Bounds bounds) =>
            Vector3.Max(bounds.extents * OverlapBoundsScale, Vector3.one * 0.05f);

        /// <summary>
        /// The volume a structure claims on the build grid, for the occupancy test that rejects a
        /// placement on top of an existing structure.
        /// <para>
        /// Horizontally this is the structure's own extents, pulled in by
        /// <see cref="OverlapBoundsScale"/> so decorative overhang does not make grid-adjacent
        /// structures reject one another. Vertically it is anchored to the ground and spans the
        /// structure's full height. Centring it on the bounds instead would lift the test box clear
        /// of anything short: a watchtower is forty units tall, so its bounds-centred box started
        /// five units above a barracks roof and the two could be stacked in the same cell.
        /// </para>
        /// </summary>
        public static void GetOverlapVolume(
            Bounds bounds, Vector3 groundPoint, out Vector3 center, out Vector3 halfExtents) {
            Vector3 extents = GetOverlapHalfExtents(bounds);

            // Measure from the ground rather than from the bounds' own minimum, so a model whose
            // pivot art floats still tests the cell it was placed on.
            float top = Mathf.Max(bounds.max.y, groundPoint.y + OverlapGroundClearance * 2f);
            float bottom = groundPoint.y + OverlapGroundClearance;

            halfExtents = new Vector3(extents.x, Mathf.Max((top - bottom) * 0.5f, 0.05f), extents.z);
            center = new Vector3(bounds.center.x, bottom + halfExtents.y, bounds.center.z);
        }

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
