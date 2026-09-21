using NUnit.Framework;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>
    /// Covers the shared rules the authoring pass, the client preview, and the authoritative server all
    /// depend on agreeing about. These are the pieces where a silent disagreement would show up as a
    /// building that does not sit on the grid or a squad that piles onto one point.
    /// </summary>
    public sealed class WV_RulesTests {
        [Test]
        public void GridFitScale_ShrinksAModelWiderThanItsFootprint() {
            // The pack's hangar is far larger than a single cell and must be scaled down to fit.
            var bounds = new Bounds(Vector3.zero, new Vector3(40f, 10f, 24f));

            float scale = WV_Rules.GetGridFitScale(bounds, footprintCells: 2);

            Assert.That(scale, Is.LessThan(1f));
            Assert.That(bounds.size.x * scale, Is.EqualTo(2 * WV_Rules.GridSize).Within(0.001f));
        }

        [Test]
        public void GridFitScale_ExpandsAModelSmallerThanItsFootprint() {
            var bounds = new Bounds(Vector3.zero, new Vector3(1f, 2f, 0.5f));

            float scale = WV_Rules.GetGridFitScale(bounds, footprintCells: 1);

            Assert.That(scale, Is.GreaterThan(1f));
            Assert.That(bounds.size.x * scale, Is.EqualTo(WV_Rules.GridSize).Within(0.001f));
        }

        [Test]
        public void GridFitScale_FitsTheWidestHorizontalAxis() {
            // A model that is deep rather than wide must still end up inside its slot.
            var bounds = new Bounds(Vector3.zero, new Vector3(4f, 3f, 20f));

            float scale = WV_Rules.GetGridFitScale(bounds, footprintCells: 1);

            Assert.That(bounds.size.z * scale, Is.EqualTo(WV_Rules.GridSize).Within(0.001f));
            Assert.That(bounds.size.x * scale, Is.LessThanOrEqualTo(WV_Rules.GridSize + 0.001f));
        }

        [Test]
        public void GridFitScale_DoesNotDivideByZeroOnADegenerateImport() {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);

            float scale = WV_Rules.GetGridFitScale(bounds, footprintCells: 3);

            Assert.That(scale, Is.EqualTo(1f));
        }

        [Test]
        public void GroupDestination_PutsTheFirstUnitOnTheClickedPoint() {
            var center = new Vector3(10f, 5f, -4f);

            Assert.That(WV_Rules.GetGroupDestination(center, index: 0, total: 12), Is.EqualTo(center));
            Assert.That(WV_Rules.GetGroupDestination(center, index: 0, total: 1), Is.EqualTo(center));
        }

        [Test]
        public void GroupDestination_SpreadsEveryMemberOfALargeGroupToADistinctPoint() {
            var center = new Vector3(100f, 0f, 100f);
            const int total = 30;
            var seen = new System.Collections.Generic.List<Vector3>();

            for (int i = 0; i < total; i++) {
                Vector3 destination = WV_Rules.GetGroupDestination(center, i, total);
                foreach (Vector3 other in seen)
                    Assert.That(Vector3.Distance(destination, other), Is.GreaterThan(0.5f),
                        $"Unit {i} was sent to a point another unit already holds.");
                seen.Add(destination);
            }

            Assert.That(seen.Count, Is.EqualTo(total));
        }

        [Test]
        public void GroupDestination_KeepsTheFormationOnTheSameHorizontalPlane() {
            var center = new Vector3(0f, 17f, 0f);

            for (int i = 0; i < 20; i++)
                Assert.That(WV_Rules.GetGroupDestination(center, i, 20).y, Is.EqualTo(17f).Within(0.0001f));
        }

        [Test]
        public void GroupDestination_RingsOutwardRatherThanGrowingWithoutBound() {
            var center = Vector3.zero;

            // The 30th unit of a push should still be within a sane radius of the order, not
            // flung across the valley by an unbounded ring index.
            float radius = WV_Rules.GetGroupDestination(center, 29, 30).magnitude;

            Assert.That(radius, Is.GreaterThan(0f));
            Assert.That(radius, Is.LessThanOrEqualTo(4f * WV_Rules.GroupFormationSpacing));
        }

        [Test]
        public void Aircraft_CruiseAboveTheGroundAndGroundUnitsDoNot() {
            foreach (WV_UnitKind kind in new[] {
                WV_UnitKind.Chopper, WV_UnitKind.Jet, WV_UnitKind.Bomber, WV_UnitKind.UAV }) {
                Assert.That(WV_Rules.IsAircraft(kind), Is.True, $"{kind} should be an aircraft.");
                Assert.That(WV_Rules.GetCruiseAltitude(kind), Is.GreaterThan(0f), $"{kind} needs an altitude.");
            }

            foreach (WV_UnitKind kind in new[] {
                WV_UnitKind.Infantry, WV_UnitKind.Tank, WV_UnitKind.APC, WV_UnitKind.Artillery }) {
                Assert.That(WV_Rules.IsAircraft(kind), Is.False, $"{kind} should be a ground unit.");
                Assert.That(WV_Rules.GetCruiseAltitude(kind), Is.EqualTo(0f));
            }
        }

        [Test]
        public void Countdown_ReadsAsMinutesAndSecondsOncePastAMinute() {
            Assert.That(WV_Rules.FormatCountdown(0f), Is.EqualTo("0s"));
            Assert.That(WV_Rules.FormatCountdown(-5f), Is.EqualTo("0s"));
            Assert.That(WV_Rules.FormatCountdown(12.1f), Is.EqualTo("13s"));
            Assert.That(WV_Rules.FormatCountdown(90f), Is.EqualTo("1m 30s"));
        }

        [Test]
        public void EveryUnitKindHasADisplayName() {
            foreach (WV_UnitKind kind in System.Enum.GetValues(typeof(WV_UnitKind))) {
                if (kind == WV_UnitKind.None)
                    continue;
                Assert.That(WV_Rules.GetUnitDisplayName(kind), Is.Not.EqualTo("Unit"),
                    $"{kind} falls through to the generic label.");
            }
        }
    }
}
