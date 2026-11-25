using UnityEngine;

namespace Universes.UniverseData.tower_defense.Tests {
    /// <summary>
    /// The 18 pads the first Tower Defense map had, as nearby grid cells: tests and the balance bot name a spot
    /// by its old number (design metres, before the world was scaled and the pads became a free grid).
    /// </summary>
    static class TD_TestCells {
        static readonly Vector2[] Design = {
            new(-61f, -24f), new(-79f, 6f), new(-53f, 19f), new(-53f, 37f), new(-45f, 4f), new(-27f, 14f), new(-18f, -21f), new(-18f, -39f), new(-9f, 2f),
            new(9f, 20f), new(17f, 49f), new(17f, 31f), new(25f, 0f), new(43f, -18f), new(52f, -35f), new(52f, -53f), new(61f, -20f), new(79f, -24f),
        };

        /// <summary>
        /// The cell index standing in for old pad <paramref name="legacy"/>: among the cells within a cell's reach of it,
        /// the one at the same distance from the road (so a tower there covers the same stretch of road as the old pad did).
        /// </summary>
        public static int At(int legacy) {
            var p = Design[legacy] * TD_Rules.WorldScale;
            var spot = new Vector3(p.x, 0f, p.y);
            float wanted = TD_Rules.DistanceToLane(spot);
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < TD_Rules.Slots.Length; i++) {
                float away = Vector3.Distance(TD_Rules.Slots[i], spot);
                if (away > TD_Rules.CellSize * 1.2f) continue;
                float score = Mathf.Abs(TD_Rules.DistanceToLane(TD_Rules.Slots[i]) - wanted) + away * 0.3f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best >= 0 ? best : TD_Rules.NearestCell(spot);
        }

        /// <summary>The cell closest to the road anywhere (for tests of minimum ranges).</summary>
        public static int NextToRoad() {
            int best = 0;
            for (int i = 1; i < TD_Rules.Slots.Length; i++)
                if (TD_Rules.DistanceToLane(TD_Rules.Slots[i]) < TD_Rules.DistanceToLane(TD_Rules.Slots[best])) best = i;
            return best;
        }
    }
}
