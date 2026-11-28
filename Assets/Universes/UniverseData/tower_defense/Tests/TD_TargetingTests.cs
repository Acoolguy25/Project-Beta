using NUnit.Framework;

namespace Universes.UniverseData.tower_defense.Tests {
    /// <summary>Tower targeting modes and the per-tower kill and damage record.</summary>
    public class TD_TargetingTests {
        /// <summary>A Gunner on old pad 8 and three scouts in its range, a little apart along the road.</summary>
        static TD_Simulation Setup(out int cell, out TD_SimEnemy back, out TD_SimEnemy middle, out TD_SimEnemy front) {
            var sim = new TD_Simulation();
            sim.StartWave(1);
            cell = TD_TestCells.At(8);
            float at = TD_Rules.LaneTrack.Project(TD_Rules.Slots[cell]);
            back = sim.AddEnemy(TD_EnemyKind.Scout, at - 1.5f, 1f);
            middle = sim.AddEnemy(TD_EnemyKind.Scout, at, 1f);
            front = sim.AddEnemy(TD_EnemyKind.Scout, at + 1.5f, 1f);
            sim.Build(cell, TD_TowerKind.Gunner, 0, TD_TowerPerks.None, out _);
            return sim;
        }

        static ushort FirstShot(TD_Simulation sim) {
            sim.Events.Clear();
            sim.Tick(0.02f);
            Assert.Greater(sim.Events.shots.Count, 0, "the tower fires");
            return sim.Events.shots[0].enemy;
        }

        [Test]
        public void NewTowersAimAtTheirDefault() {
            var sim = Setup(out int cell, out _, out _, out var front);
            Assert.AreEqual(TD_TargetMode.First, sim.Towers[cell].target);
            Assert.AreEqual(front.id, FirstShot(sim), "First shoots the enemy nearest the base");
            Assert.AreEqual(TD_TargetMode.Strong, TD_Targeting.Default(TD_TowerKind.Sniper), "Snipers keep picking the toughest enemy");
        }

        [Test]
        public void LastShootsTheNewestEnemy() {
            var sim = Setup(out int cell, out var back, out _, out _);
            Assert.IsTrue(sim.SetTarget(cell, TD_TargetMode.Last));
            Assert.AreEqual(back.id, FirstShot(sim));
        }

        [Test]
        public void StrongShootsTheMostHealthAndShield() {
            var sim = Setup(out int cell, out _, out var middle, out _);
            middle.health = middle.maxHealth = 500f;
            Assert.IsTrue(sim.SetTarget(cell, TD_TargetMode.Strong));
            Assert.AreEqual(middle.id, FirstShot(sim));
        }

        [Test]
        public void CloseShootsTheNearestEnemy() {
            var sim = Setup(out int cell, out var back, out var middle, out var front);
            // Only the middle scout stays near the tower; the others move to the edges of its range.
            float at = TD_Rules.LaneTrack.Project(TD_Rules.Slots[cell]);
            float reach = TD_Rules.Range(TD_TowerKind.Gunner, 1) * 0.6f;
            back.distance = at - reach;
            front.distance = at + reach;
            Assert.IsTrue(sim.SetTarget(cell, TD_TargetMode.Close));
            Assert.AreEqual(middle.id, FirstShot(sim));
        }

        [Test]
        public void ModesCycleThroughAllFourAndRejectEmptyPads() {
            var mode = TD_TargetMode.First;
            for (int i = 0; i < TD_Targeting.Count; i++) mode = TD_Targeting.Next(mode);
            Assert.AreEqual(TD_TargetMode.First, mode);
            var sim = new TD_Simulation();
            Assert.IsFalse(sim.SetTarget(TD_TestCells.At(8), TD_TargetMode.Last), "no tower there");
            Assert.IsFalse(TD_Targeting.IsValid((TD_TargetMode)TD_Targeting.Count));
        }

        [Test]
        public void TowersRecordTheirKillsAndDamage() {
            var sim = Setup(out int cell, out _, out _, out _);
            float total = 0f;
            foreach (var e in sim.Enemies) {
                total += e.health;
                e.stunUntil = float.MaxValue; // held in range so every kill is the tower's
            }
            for (int i = 0; i < 400 && sim.Enemies.Count > 0; i++) sim.Tick(0.05f);
            var tower = sim.Towers[cell];
            Assert.AreEqual(3, tower.kills);
            Assert.AreEqual(total, tower.damageDealt, 0.01f, "damage counts health taken, not overkill");
        }

        [Test]
        public void ShortNumbersReadWell() {
            Assert.AreEqual("950", TD_Targeting.Short(950));
            Assert.AreEqual("1.2k", TD_Targeting.Short(1240));
            Assert.AreEqual("48k", TD_Targeting.Short(48_200));
            Assert.AreEqual("1.5M", TD_Targeting.Short(1_500_000));
        }
    }
}
