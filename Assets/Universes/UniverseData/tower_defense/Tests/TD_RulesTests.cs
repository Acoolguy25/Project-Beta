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
        public void ChoppersFlyAboveTheRoad() {
            Vector3 ground = TD_Rules.EnemyPosition(TD_EnemyKind.Tank, 30f, out _);
            Vector3 air = TD_Rules.EnemyPosition(TD_EnemyKind.Chopper, 30f, out _);
            Assert.AreEqual(TD_Rules.FlyHeight, air.y - ground.y, 1e-4f);
        }
    }
}
