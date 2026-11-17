using System.Collections.Generic;
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

        [Test]
        public void FactoriesDoubleTrainingAndFortressesDoubleDefence() {
            const float x2 = 2f, x3 = 3f;
            Assert.That(DI_Rules.BaseProductionRate(1.25f, false, x2), Is.EqualTo(1.25f));
            Assert.That(DI_Rules.BaseProductionRate(1.25f, true, x2), Is.EqualTo(2.5f));
            Assert.That(DI_Rules.BaseProductionRate(1.25f, true, x3), Is.EqualTo(3.75f));
            Assert.That(DI_Rules.ArrivalDamage(1f, false, x2), Is.EqualTo(1f));
            Assert.That(DI_Rules.ArrivalDamage(1.5f, true, x2), Is.EqualTo(0.75f));
            Assert.That(DI_Rules.AttackersToDefeat(10, false, x2), Is.EqualTo(10));
            Assert.That(DI_Rules.AttackersToDefeat(10, true, x2), Is.EqualTo(20));
            Assert.That(DI_Rules.AttackersToDefeat(10, true, x3), Is.EqualTo(30));
            Assert.That(DI_Rules.AttackersToDefeat(-3, true, x2), Is.Zero);
            Assert.That(DI_Rules.DefendersDefeatedBy(7, false, x2), Is.EqualTo(7));
            Assert.That(DI_Rules.DefendersDefeatedBy(7, true, x2), Is.EqualTo(3));
        }

        static DI_StateBroadcast Snapshot(int revision, int[] baseTeams, int[] dotIds = null, int[] dotTeams = null) {
            var positions = new Vector2[baseTeams.Length];
            for (int i = 0; i < positions.Length; i++) positions[i] = new Vector2(i * 10f, 0f);
            dotIds ??= System.Array.Empty<int>();
            return new DI_StateBroadcast {
                revision = revision,
                basePositions = positions,
                baseTeams = baseTeams,
                baseTroops = new int[baseTeams.Length],
                dotIds = dotIds,
                dotTeams = dotTeams ?? new int[dotIds.Length],
                dotPositions = new Vector2[dotIds.Length]
            };
        }

        [Test]
        public void CapturesAreBasesThatChangedToANewTeamOnTheSameBoard() {
            var captures = new List<DI_Capture>();
            DI_StateBroadcast before = Snapshot(1, new[] { 0, -1, 100, 0 });
            DI_StateBroadcast after = Snapshot(2, new[] { 0, 0, 0, -1 });
            DI_SnapshotEvents.FindCaptures(before, after, captures);
            Assert.That(captures.Count, Is.EqualTo(2));
            Assert.That(captures[0].baseId, Is.EqualTo(1));
            Assert.That(captures[0].previousTeamId, Is.EqualTo(-1));
            Assert.That(captures[1].baseId, Is.EqualTo(2));
            Assert.That(captures[1].previousTeamId, Is.EqualTo(100));

            // A rerolled board or an out-of-order snapshot is not a capture.
            after.basePositions[3] = new Vector2(-5f, 3f);
            DI_SnapshotEvents.FindCaptures(before, after, captures);
            Assert.That(captures, Is.Empty);
            DI_SnapshotEvents.FindCaptures(after, before, captures);
            Assert.That(captures, Is.Empty);
        }

        [Test]
        public void TeamsWithNoBaseAndNoTroopLeftAreEliminated() {
            var eliminated = new List<int>();
            var scratch = new HashSet<int>();
            DI_StateBroadcast before = Snapshot(1, new[] { 0, 1, 100 }, new[] { 5 }, new[] { 2 });
            DI_StateBroadcast after = Snapshot(2, new[] { 0, 0, 100 }, new[] { 6 }, new[] { 1 });
            DI_SnapshotEvents.FindEliminatedTeams(before, after, scratch, eliminated);
            Assert.That(eliminated, Is.EqualTo(new[] { 2 }));
            Assert.That(DI_SnapshotEvents.HasPresence(after, 1), Is.True);
            Assert.That(DI_SnapshotEvents.HasPresence(after, -1), Is.False);
        }

        [Test]
        public void VanishedDotsAreThoseMissingFromTheNextSnapshot() {
            var vanished = new List<int>();
            var scratch = new HashSet<int>();
            DI_StateBroadcast before = Snapshot(1, new[] { 0 }, new[] { 4, 5, 6 });
            DI_StateBroadcast after = Snapshot(2, new[] { 0 }, new[] { 5, 7 });
            DI_SnapshotEvents.FindVanishedDots(before, after, scratch, vanished);
            Assert.That(vanished, Is.EqualTo(new[] { 0, 2 }));
        }

        [Test]
        public void NearestBaseHonoursTheRadius() {
            DI_StateBroadcast state = Snapshot(1, new[] { 0, 0, 0 });
            Assert.That(DI_SnapshotEvents.NearestBase(state, new Vector2(11f, 1f), 3f), Is.EqualTo(1));
            Assert.That(DI_SnapshotEvents.NearestBase(state, new Vector2(15f, 0f), 3f), Is.EqualTo(-1));
        }
    }
}
