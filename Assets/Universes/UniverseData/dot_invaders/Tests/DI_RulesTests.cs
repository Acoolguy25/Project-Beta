using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Tests {
    public class DI_RulesTests {
        [Test]
        public void ManualRouteRetainsWaypointsBacktracksAndCannotLoop() {
            Vector2[] positions = { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            int[] sources = { 0, 1, 2, 3 };
            int[] targets = { 1, 2, 3, 0 };
            int[] route = DI_Rules.AppendRouteWaypoint(positions, sources, targets, new[] { 0 }, 1);
            route = DI_Rules.AppendRouteWaypoint(positions, sources, targets, route, 3);
            Assert.That(route, Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(DI_Rules.AppendRouteWaypoint(positions, sources, targets, route, -1), Is.EqualTo(route));
            Assert.That(DI_Rules.AppendRouteWaypoint(positions, sources, targets, route, 1), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(DI_Rules.AppendRouteWaypoint(positions, sources, targets, route, 0), Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void ManualRouteKeepsItsPrefixWhenOnlyConnectionWouldLoop() {
            Vector2[] positions = { Vector2.zero, Vector2.right, Vector2.up };
            int[] route = { 0, 1 };
            Assert.That(DI_Rules.AppendRouteWaypoint(positions, new[] { 0, 0 }, new[] { 1, 2 }, route, 2),
                Is.EqualTo(route));
        }

        [Test]
        public void ProductionRampsFromNormalSpeedAndStopsAtConfiguredPeak() {
            Assert.That(DI_Rules.SuperProductionRate(0f, 12.5f, 20f), Is.EqualTo(1.25f));
            Assert.That(DI_Rules.SuperProductionRate(10f, 12.5f, 20f), Is.EqualTo(Mathf.Sqrt(1.25f * 12.5f)).Within(0.001f));
            Assert.That(DI_Rules.SuperProductionRate(40f, 12.5f, 20f), Is.EqualTo(12.5f));
            Assert.That(DI_Rules.SuperProductionRate(0f, 0.5f, 20f), Is.EqualTo(0.5f));
            Assert.That(DI_Rules.SuperProductionOverInterval(0f, 20f, 12.5f, 20f),
                Is.EqualTo(DI_Rules.SuperProductionOverInterval(0f, 10f, 12.5f, 20f) +
                    DI_Rules.SuperProductionOverInterval(10f, 10f, 12.5f, 20f)).Within(0.001f));
            Assert.That(DI_Rules.SuperProductionOverInterval(20f, 2f, 12.5f, 20f), Is.EqualTo(25f).Within(0.001f));
        }

        [Test]
        public void PowerIncludesMovingTroopsAndBasesWithoutCountingQueuedTroopsTwice() {
            var state = new DI_StateBroadcast {
                baseTeams = new[] { 0, 0, 100, -1 },
                baseTroops = new[] { 12, 8, 30, 200 },
                basePendingTroops = new[] { 10, 0, 0, 0 },
                dotTeams = new[] { 0, 100, 100, 101 }
            };
            var powers = DI_Rules.CalculatePower(state);
            Assert.That(powers.Count, Is.EqualTo(3));
            Assert.That(powers[0].troops, Is.EqualTo(21));
            Assert.That(powers[0].bases, Is.EqualTo(2));
            Assert.That(powers[0].Power, Is.EqualTo(71));
            Assert.That(powers[100].Power, Is.EqualTo(57));
            Assert.That(powers[101].Power, Is.EqualTo(1));
            Assert.That(powers.ContainsKey(-1), Is.False);
        }

        [Test]
        public void EmptyMatchHasNoPowerShares() {
            Assert.That(DI_Rules.CalculatePower(default), Is.Empty);
        }
    }
}
