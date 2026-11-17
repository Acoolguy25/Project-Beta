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
            Assert.AreEqual(0, TD_Rules.EarlyStartBonus(1, 25), "the first build phase pays no early bonus");
            Assert.AreEqual(TD_Rules.EarlyStartBonus(10), TD_Rules.EarlyStartBonus(2, 10));
        }

        [Test]
        public void AnEnemyThatReachesTheBaseLeaksAndPaysNothing() {
            float end = TD_Rules.LaneTrack.Length;
            var leaker = new TD_SimEnemy { id = 1, kind = TD_EnemyKind.Apc, distance = end - 0.1f, health = 50f, maxHealth = 50f };
            var walker = new TD_SimEnemy { id = 2, kind = TD_EnemyKind.Scout, distance = 0f, health = 50f, maxHealth = 50f };
            var enemies = new System.Collections.Generic.List<TD_SimEnemy> { leaker, walker };
            var leaked = new System.Collections.Generic.List<ushort>();
            var killed = new System.Collections.Generic.List<ushort>();

            int damage = TD_Battle.Advance(enemies, 1f, 0f, leaked);
            Assert.AreEqual(TD_Rules.Enemy(TD_EnemyKind.Apc).leakDamage, damage);
            CollectionAssert.AreEqual(new ushort[] { 1 }, leaked);
            CollectionAssert.IsEmpty(killed, "a leak is not a kill");
            CollectionAssert.AreEqual(new[] { walker }, enemies);
            Assert.AreEqual(0, TD_Battle.Damage(enemies, leaker, 1000f, killed), "a leaked enemy can no longer be shot for its reward");
            CollectionAssert.IsEmpty(killed);

            Assert.AreEqual(0, TD_Battle.Damage(enemies, walker, 10f, killed), "a hit that does not destroy pays nothing");
            Assert.AreEqual(TD_Rules.Enemy(TD_EnemyKind.Scout).reward, TD_Battle.Damage(enemies, walker, 100f, killed));
            CollectionAssert.AreEqual(new ushort[] { 2 }, killed);
            CollectionAssert.IsEmpty(enemies);
            Assert.AreEqual(0, TD_Battle.Damage(enemies, walker, 100f, killed), "a kill pays once");
        }

        [Test]
        public void SlowedEnemiesMoveAtTheTowersSlowFactor() {
            Assert.AreEqual(0.55f, TD_Rules.SlowFactor(TD_TowerKind.Frost, 1), 1e-4f);
            Assert.Less(TD_Rules.SlowFactor(TD_TowerKind.Frost, 3), TD_Rules.SlowFactor(TD_TowerKind.Frost, 2));
            Assert.AreEqual(1f, TD_Rules.SlowFactor(TD_TowerKind.Gunner, 3));
            Assert.AreEqual(45, TD_Rules.SlowPercent(TD_TowerKind.Frost, 1));
            var slowed = new TD_SimEnemy { kind = TD_EnemyKind.Scout, slowUntil = 2f, slowFactor = TD_Rules.SlowFactor(TD_TowerKind.Frost, 1) };
            var leaked = new System.Collections.Generic.List<ushort>();
            TD_Battle.Advance(new System.Collections.Generic.List<TD_SimEnemy> { slowed }, 1f, 1f, leaked);
            Assert.AreEqual(TD_Rules.Enemy(TD_EnemyKind.Scout).speed * 0.55f, slowed.distance, 1e-3f);
            TD_Battle.Advance(new System.Collections.Generic.List<TD_SimEnemy> { slowed }, 1f, 3f, leaked);
            Assert.AreEqual(TD_Rules.Enemy(TD_EnemyKind.Scout).speed * 1.55f, slowed.distance, 1e-3f, "the slow wears off");
        }

        [Test]
        public void WavesGrowWithoutACliff() {
            // From wave 3 on, the ground army's health never more than doubles from one wave to the next,
            // and the head count never jumps by more than ten.
            for (int w = 2; w <= TD_Rules.WaveCount; w++) {
                float Ground(int wave) => TD_Rules.Wave(wave).Where(g => g.kind is TD_EnemyKind.Scout or TD_EnemyKind.Apc or TD_EnemyKind.Tank).Sum(g => g.count * TD_Rules.Enemy(g.kind).health) * TD_Rules.HealthScale(wave);
                if (w >= 3) Assert.LessOrEqual(Ground(w) / Ground(w - 1), 2f, $"wave {w} is a cliff");
                int Ground_count(int wave) => TD_Rules.Wave(wave).Where(g => g.kind is TD_EnemyKind.Scout or TD_EnemyKind.Apc or TD_EnemyKind.Tank).Sum(g => g.count);
                Assert.LessOrEqual(Ground_count(w) - Ground_count(w - 1), 10, $"wave {w} adds too many enemies at once");
            }
        }

        [Test]
        public void CachedWaveCountsMatchTheWavePlan() {
            for (int w = 1; w <= TD_Rules.WaveCount; w++) {
                int[] counts = TD_Rules.WaveCounts(w);
                foreach (TD_EnemyKind kind in System.Enum.GetValues(typeof(TD_EnemyKind)))
                    Assert.AreEqual(TD_Rules.Wave(w).Where(g => g.kind == kind).Sum(g => g.count), counts[(int)kind], $"wave {w} {kind}");
                Assert.AreSame(counts, TD_Rules.WaveCounts(w), "computed once");
            }
            Assert.AreEqual(0, TD_Rules.EnemyCount(0));
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
