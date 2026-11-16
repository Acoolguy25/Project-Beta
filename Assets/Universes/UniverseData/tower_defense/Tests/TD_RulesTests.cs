using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.tower_defense.Tests {
    public class TD_RulesTests {
        [Test]
        public void SlotsSitBesideTheLaneNotOnIt() {
            var lane = TD_Rules.LaneTrack;
            for (int i = 0; i < TD_Rules.Slots.Length; i++) {
                Vector3 slot = TD_Rules.Slots[i];
                float d = Vector3.Distance(lane.Evaluate(lane.Project(slot)), slot);
                Assert.That(d, Is.InRange(6.5f, 14f), $"slot {i} is {d:0.0} m from the road");
                for (int j = 0; j < i; j++) Assert.Greater(Vector3.Distance(slot, TD_Rules.Slots[j]), 6f, $"slots {i} and {j} overlap");
            }
        }

        [Test]
        public void EveryWaveHasEnemiesAndGrows() {
            // Air (every 4th) and boss waves are deliberate spikes; the regular ground army must keep growing.
            float previous = 0f;
            for (int w = 1; w <= TD_Rules.WaveCount; w++) {
                Assert.Greater(TD_Rules.EnemyCount(w), 0);
                float threat = TD_Rules.Wave(w).Where(g => g.kind is not (TD_EnemyKind.Chopper or TD_EnemyKind.Boss)).Sum(g => g.count * TD_Rules.Enemy(g.kind).health) * TD_Rules.HealthScale(w);
                Assert.GreaterOrEqual(threat, previous * 0.85f, $"wave {w} got much easier");
                previous = threat;
            }
            Assert.IsTrue(TD_Rules.Wave(10).Any(g => g.kind == TD_EnemyKind.Boss));
            Assert.IsTrue(TD_Rules.Wave(4).Any(g => g.kind == TD_EnemyKind.Chopper));
        }

        [Test]
        public void TowersHaveARole() {
            Assert.IsFalse(TD_Rules.CanHit(TD_TowerKind.Cannon, TD_EnemyKind.Chopper));
            Assert.IsTrue(TD_Rules.CanHit(TD_TowerKind.Gunner, TD_EnemyKind.Chopper));
            Assert.Greater(TD_Rules.HitDamage(TD_TowerKind.Sniper, 1, TD_EnemyKind.Tank), TD_Rules.HitDamage(TD_TowerKind.Gunner, 3, TD_EnemyKind.Tank));
            Assert.Greater(TD_Rules.Damage(TD_TowerKind.Gunner, 3), TD_Rules.Damage(TD_TowerKind.Gunner, 1) * 2.5f);
            Assert.AreEqual(1f, TD_Rules.HitDamage(TD_TowerKind.Frost, 1, TD_EnemyKind.Boss));
        }

        [Test]
        public void SellingRefundsPartOfEverythingSpent() {
            foreach (TD_TowerKind kind in System.Enum.GetValues(typeof(TD_TowerKind))) {
                int built = TD_Rules.Tower(kind).cost;
                Assert.AreEqual(built, TD_Rules.Invested(kind, 1));
                Assert.AreEqual(built + TD_Rules.UpgradeCost(kind, 1) + TD_Rules.UpgradeCost(kind, 2), TD_Rules.Invested(kind, TD_Rules.MaxLevel));
                for (int level = 1; level <= TD_Rules.MaxLevel; level++) {
                    int refund = TD_Rules.SellRefund(kind, level);
                    Assert.Less(refund, TD_Rules.Invested(kind, level), $"{kind} L{level} must not refund everything");
                    Assert.Greater(refund, 0);
                    if (level > 1) Assert.Greater(refund, TD_Rules.SellRefund(kind, level - 1), "upgrades raise the sale value");
                }
            }
        }

        [Test]
        public void CallingAWaveEarlyPaysPerSecondSkipped() {
            Assert.AreEqual(0, TD_Rules.EarlyStartBonus(0));
            Assert.AreEqual(0, TD_Rules.EarlyStartBonus(-3));
            Assert.AreEqual(10 * TD_Rules.EarlyStartBonusPerSecond, TD_Rules.EarlyStartBonus(10));
        }

        [Test]
        public void FastForwardNeedsAMajority() {
            Assert.IsFalse(TD_Rules.FastVotePasses(0, 0));
            Assert.IsTrue(TD_Rules.FastVotePasses(1, 1));
            Assert.IsFalse(TD_Rules.FastVotePasses(1, 2));
            Assert.IsTrue(TD_Rules.FastVotePasses(2, 3));
        }

        [Test]
        public void BossWavesAreKnown() {
            Assert.IsTrue(TD_Rules.HasBoss(10));
            Assert.IsTrue(TD_Rules.HasBoss(TD_Rules.WaveCount));
            Assert.IsFalse(TD_Rules.HasBoss(1));
        }

        [Test]
        public void ChoppersFlyAboveTheRoad() {
            Vector3 ground = TD_Rules.EnemyPosition(TD_EnemyKind.Tank, 30f, out _);
            Vector3 air = TD_Rules.EnemyPosition(TD_EnemyKind.Chopper, 30f, out _);
            Assert.AreEqual(TD_Rules.FlyHeight, air.y - ground.y, 1e-4f);
        }
    }
}
