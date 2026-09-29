using NUnit.Framework;
using RyanAssets.Shared.Globals;
using UnityEditor;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>Covers the build grid: structures of every size must land on whole cells.</summary>
    public sealed class WV_PlacementTests {
        /// <summary>The cell edges a footprint of <paramref name="cells"/> centred at <paramref name="center"/> covers.</summary>
        static void AssertOnCellLines(float center, int cells) {
            float half = cells * StructurePlacement.GridSize * 0.5f;
            float low = (center - half) / StructurePlacement.GridSize;
            Assert.That(low, Is.EqualTo(Mathf.Round(low)).Within(1e-4f), $"{cells}-cell footprint at {center}");
        }

        [Test]
        public void EveryFootprint_SnapsOntoWholeCells() {
            foreach (float raw in new[] { -7.3f, -1.1f, 0f, 0.9f, 2.5f, 5.99f, 13.2f }) {
                for (int cells = 1; cells <= 4; cells++) {
                    Vector3 snapped = StructurePlacement.SnapToGrid(new Vector3(raw, 0f, raw), cells);
                    AssertOnCellLines(snapped.x, cells);
                    AssertOnCellLines(snapped.z, cells);
                }
            }
        }

        [Test]
        public void Snapping_IsStableForAnAlreadySnappedPosition() {
            for (int cells = 1; cells <= 4; cells++) {
                Vector3 once = StructurePlacement.SnapToGrid(new Vector3(9.7f, 0f, -3.2f), cells);
                Vector3 twice = StructurePlacement.SnapToGrid(once, cells);
                Assert.That(twice.x, Is.EqualTo(once.x));
                Assert.That(twice.z, Is.EqualTo(once.z));
            }
        }

        [Test]
        public void Footprint_IsReadFromTheStructuresBounds() {
            Assert.That(StructurePlacement.GetFootprintCells(new Bounds(Vector3.zero, new Vector3(4f, 10f, 4f))), Is.EqualTo(1));
            Assert.That(StructurePlacement.GetFootprintCells(new Bounds(Vector3.zero, new Vector3(8.3f, 3f, 7.6f))), Is.EqualTo(2));
            Assert.That(StructurePlacement.GetFootprintCells(new Bounds(Vector3.zero, new Vector3(12f, 3f, 1f))), Is.EqualTo(3));
            // A thin fence is one cell long, not zero.
            Assert.That(StructurePlacement.GetFootprintCells(new Bounds(Vector3.zero, new Vector3(0.4f, 2f, 0.4f))), Is.EqualTo(1));
        }

        [Test]
        public void DeclaredFootprint_OverridesTheMeasuredOne() {
            // A fence post is narrower than a cell but declares two, so it snaps to the grid point
            // where two fence runs meet instead of to the middle of a cell.
            var post = new GameObject("Post");
            try {
                // An Editor-only test MonoBehaviour cannot be attached in Unity 6. Exercise the
                // runtime component that actually declares placed structures' footprints instead.
                var footprint = new SerializedObject(post.AddComponent<WV_Constructable>());
                footprint.FindProperty("footprintCells").intValue = 2;
                footprint.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(StructurePlacement.GetFootprintCells(post), Is.EqualTo(2));

                // Declaring nothing falls back to measuring - here, nothing to measure.
                footprint.FindProperty("footprintCells").intValue = 0;
                footprint.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(StructurePlacement.GetFootprintCells(post), Is.EqualTo(1));
            } finally {
                Object.DestroyImmediate(post);
            }
        }

        [Test]
        public void WarValleyGrid_MatchesTheSharedPlacementGrid() {
            Assert.That(WV_Rules.GridSize, Is.EqualTo(StructurePlacement.GridSize));
        }

        static GameObject PlacementBox(Vector3 size, Vector3 offset, float yaw = 0f) {
            var root = new GameObject("Placement test") { layer = LayerMask.NameToLayer("Structure") };
            root.AddComponent<WV_Constructable>();
            var box = root.GetComponent<BoxCollider>();
            box.size = size;
            box.center = Vector3.up * size.y * 0.5f;
            root.transform.SetPositionAndRotation(new Vector3(10000f, 1000f, 10000f) + offset,
                Quaternion.Euler(0f, yaw, 0f));
            return root;
        }

        [Test]
        public void PlacementBounds_IgnoreConstructionDecorationsAndBuildingAnimation() {
            var root = PlacementBox(new Vector3(8f, 6f, 2f), Vector3.zero);
            try {
                var decoration = GameObject.CreatePrimitive(PrimitiveType.Cube);
                decoration.transform.SetParent(root.transform, false);
                decoration.transform.localScale = new Vector3(20f, 20f, 20f);
                decoration.transform.localPosition = Vector3.down * 5f;
                StructurePlacement.TryGetBounds(root, out Bounds bounds);
                Assert.That(bounds.size, Is.EqualTo(new Vector3(8f, 6f, 2f)));
                Assert.That(bounds.min.y, Is.EqualTo(root.transform.position.y));
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(8f, 0f, 0f, false)] // End-to-end seam.
        [TestCase(4f, 4f, 90f, false)] // Perpendicular runs meeting at their ends.
        [TestCase(4f, 0f, 0f, true)] // Half of a run placed over its neighbor.
        [TestCase(0f, 0f, 90f, true)] // Crossing through an existing run.
        public void WallPlacement_AcceptsJointsAndRejectsOverlapInEitherOrder(float x, float z, float yaw, bool blocked) {
            var first = PlacementBox(new Vector3(8f, 6f, 1.76f), Vector3.zero);
            var second = PlacementBox(new Vector3(8f, 6f, 1.76f), new Vector3(x, 0f, z), yaw);
            try {
                Physics.SyncTransforms();
                foreach (var candidate in new[] { first, second }) {
                    StructurePlacement.TryGetBounds(candidate, out Bounds bounds);
                    Assert.That(StructurePlacement.HasStructureOverlap(candidate, bounds, candidate.transform.position),
                        Is.EqualTo(blocked));
                }
            } finally {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void CornerPost_CanJoinAnExistingRunInEitherOrder() {
            var post = PlacementBox(new Vector3(1.33f, 6f, 1.33f), Vector3.zero);
            var wall = PlacementBox(new Vector3(8f, 6f, 1.76f), new Vector3(4f, 0f, 0f));
            try {
                Physics.SyncTransforms();
                foreach (var candidate in new[] { post, wall }) {
                    StructurePlacement.TryGetBounds(candidate, out Bounds bounds);
                    Assert.That(StructurePlacement.HasStructureOverlap(candidate, bounds, candidate.transform.position), Is.False);
                }
            } finally {
                Object.DestroyImmediate(post);
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void OpenGate_StillRejectsDuplicatePlacement() {
            var gate = PlacementBox(new Vector3(8f, 6f, 1.76f), Vector3.zero);
            var candidate = PlacementBox(new Vector3(8f, 6f, 1.76f), Vector3.zero);
            try {
                gate.GetComponent<BoxCollider>().isTrigger = true;
                Physics.SyncTransforms();
                StructurePlacement.TryGetBounds(candidate, out Bounds bounds);
                Assert.That(StructurePlacement.HasStructureOverlap(candidate, bounds, candidate.transform.position), Is.True);
            } finally {
                Object.DestroyImmediate(gate);
                Object.DestroyImmediate(candidate);
            }
        }
    }
}
