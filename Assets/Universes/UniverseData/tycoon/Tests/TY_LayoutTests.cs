using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.tycoon.Tests {
    // Where things stand on a plot: landmark footprints must stay clear of each other, of the pads, of
    // the gate walkway and of the other sized items, and inside the wall.
    public class TY_LayoutTests {
        /// <summary>A named square or rectangle on the lot (plot space x/z).</summary>
        readonly struct Area {
            public readonly string name;
            public readonly Rect rect;
            public Area(string name, Rect rect) { this.name = name; this.rect = rect; }
            public override string ToString() => $"{name} x {rect.xMin:0.##}..{rect.xMax:0.##} z {rect.yMin:0.##}..{rect.yMax:0.##}";
        }

        static Rect Square(Vector3 centre, float size) => Rect(centre, new Vector2(size, size));
        static Rect Rect(Vector3 centre, Vector2 size) => new(centre.x - size.x * 0.5f, centre.z - size.y * 0.5f, size.x, size.y);

        /// <summary>Strict overlap: touching edges are fine, any shared area is not.</summary>
        static bool Overlap(Rect a, Rect b) => a.xMin < b.xMax && b.xMin < a.xMax && a.yMin < b.yMax && b.yMin < a.yMax;

        static List<Area> Landmarks() {
            var list = new List<Area>();
            for (int i = 0; i < TY_Catalog.Count; i++) {
                TY_Item item = TY_Catalog.Items[i];
                if (item.kind == TY_ItemKind.Landmark) list.Add(new Area(item.name, Square(item.itemPosition, item.size)));
            }
            return list;
        }

        /// <summary>Everything else with a fixed place: the pads, the spawn point and the sized items that are not landmarks.</summary>
        static List<Area> FixedAreas() {
            var list = new List<Area>();
            foreach (TY_ItemKind lane in TY_Lanes.PadLanes)
                list.Add(new Area($"{TY_Lanes.Title(lane)} pad", Rect(TY_Catalog.LanePosition(lane), new Vector2(TY_Catalog.ButtonSize.x, TY_Catalog.ButtonSize.z))));
            list.Add(new Area("claim pad", Rect(TY_Catalog.ClaimPosition, new Vector2(TY_Catalog.PadSize.x, TY_Catalog.PadSize.z))));
            list.Add(new Area("collect pad", Rect(TY_Catalog.CollectPosition, new Vector2(TY_Catalog.PadSize.x, TY_Catalog.PadSize.z))));
            list.Add(new Area("daily chest", Square(TY_Catalog.ChestPosition, TY_Catalog.ChestPadSize)));
            list.Add(new Area("pet crate", Square(TY_Catalog.CratePosition, TY_Catalog.ChestPadSize)));
            list.Add(new Area("spawn", Square(TY_Catalog.SpawnPosition, 2f)));
            Rect coins = TY_Catalog.GoldenArea;
            list.Add(new Area("gold coin area", coins));
            for (int i = 0; i < TY_Catalog.Count; i++) {
                TY_Item item = TY_Catalog.Items[i];
                if (item.kind != TY_ItemKind.Landmark && item.size > 0f) list.Add(new Area(item.name, Square(item.itemPosition, item.size)));
            }
            return list;
        }

        [Test]
        public void EveryLandmarkHasAFootprint() {
            foreach (Area landmark in Landmarks()) Assert.Greater(landmark.rect.width, 3f, $"{landmark.name} needs a base to stand on");
            Assert.AreEqual(TY_Catalog.LandmarkCount, Landmarks().Count);
        }

        [Test]
        public void LandmarkFootprintsNeverOverlap() {
            List<Area> landmarks = Landmarks();
            for (int a = 0; a < landmarks.Count; a++)
                for (int b = a + 1; b < landmarks.Count; b++)
                    Assert.IsFalse(Overlap(landmarks[a].rect, landmarks[b].rect), $"{landmarks[a]} overlaps {landmarks[b]}");
        }

        [Test]
        public void LandmarksStayClearOfPadsAndItems() {
            List<Area> fixedAreas = FixedAreas();
            foreach (Area landmark in Landmarks())
                foreach (Area other in fixedAreas)
                    Assert.IsFalse(Overlap(landmark.rect, other.rect), $"{landmark} overlaps {other}");
        }

        [Test]
        public void LandmarksStayInsideTheWallAndOffTheGateWalkway() {
            float wall = TY_Catalog.WallInner;
            var walkway = new Rect(-TY_Catalog.GateHalfWidth, TY_Catalog.SpawnPosition.z, TY_Catalog.GateHalfWidth * 2f, TY_Catalog.PlotSize * 0.5f - TY_Catalog.SpawnPosition.z);
            foreach (Area landmark in Landmarks()) {
                Rect r = landmark.rect;
                Assert.That(r.xMin >= -wall && r.xMax <= wall && r.yMin >= -wall && r.yMax <= wall, $"{landmark} reaches past the wall (|{wall}|)");
                Assert.IsFalse(Overlap(r, walkway), $"{landmark} blocks the walkway from the gate to the spawn");
            }
        }

        [Test]
        public void DecorationsDressTheYardOutsideTheFrontWall() {
            int outside = 0;
            for (int i = 0; i < TY_Catalog.Count; i++) {
                TY_Item item = TY_Catalog.Items[i];
                if (item.kind != TY_ItemKind.Decor) continue;
                // The fairy lights hang over the lot's walls; everything else stands on the lawn past the gate.
                if (item.name == "Fairy Lights") continue;
                outside++;
                Assert.Greater(item.itemPosition.z, TY_Catalog.PlotSize * 0.5f + 0.6f, $"{item.name} stands inside the front wall");
            }
            Assert.Greater(outside, 0);
        }

        [Test]
        public void PadsDoNotOverlapEachOther() {
            var pads = new List<Area>();
            foreach (TY_ItemKind lane in TY_Lanes.PadLanes)
                pads.Add(new Area($"{TY_Lanes.Title(lane)} pad", Rect(TY_Catalog.LanePosition(lane), new Vector2(TY_Catalog.ButtonSize.x, TY_Catalog.ButtonSize.z))));
            pads.Add(new Area("daily chest", Square(TY_Catalog.ChestPosition, TY_Catalog.ChestPadSize)));
            pads.Add(new Area("pet crate", Square(TY_Catalog.CratePosition, TY_Catalog.ChestPadSize)));
            pads.Add(new Area("spawn", Square(TY_Catalog.SpawnPosition, 2f)));
            for (int a = 0; a < pads.Count; a++)
                for (int b = a + 1; b < pads.Count; b++)
                    Assert.IsFalse(Overlap(pads[a].rect, pads[b].rect), $"{pads[a]} overlaps {pads[b]}");
        }
    }
}
