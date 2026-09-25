using NUnit.Framework;
using RyanAssets.Shared.Globals;
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
                post.AddComponent<WV_TestFootprint>().Cells = 2;
                Assert.That(StructurePlacement.GetFootprintCells(post), Is.EqualTo(2));

                // Declaring nothing falls back to measuring - here, nothing to measure.
                post.GetComponent<WV_TestFootprint>().Cells = 0;
                Assert.That(StructurePlacement.GetFootprintCells(post), Is.EqualTo(1));
            } finally {
                Object.DestroyImmediate(post);
            }
        }

        [Test]
        public void WarValleyGrid_MatchesTheSharedPlacementGrid() {
            Assert.That(WV_Rules.GridSize, Is.EqualTo(StructurePlacement.GridSize));
        }
    }
}
