using NUnit.Framework;

namespace Universes.UniverseData.tower_defense.Tests {
    /// <summary>Elite specialisations: buying them, their price, and what each kind of elite changes in a fight.</summary>
    public class TD_EliteTests {
        /// <summary>A level 3 <paramref name="kind"/> on old pad 8 with plenty of cash.</summary>
        static TD_Simulation MaxedTower(TD_TowerKind kind, out int cell) {
            var sim = new TD_Simulation { Cash = 100_000 };
            sim.StartWave(1);
            cell = TD_TestCells.At(8);
            for (int i = 0; i < TD_Rules.MaxLevel; i++) sim.Build(cell, kind, 0, TD_TowerPerks.None, out _);
            Assert.AreEqual(TD_Rules.MaxLevel, sim.Towers[cell].level);
            return sim;
        }

        static float RoadAt(int cell) => TD_Rules.LaneTrack.Project(TD_Rules.Slots[cell]);

        [Test]
        public void EveryTowerHasTwoDistinctElites() {
            for (int k = 0; k < TD_Rules.Towers.Length; k++) {
                var kind = (TD_TowerKind)k;
                var a = TD_Elites.Get(kind, TD_TowerPath.First);
                var b = TD_Elites.Get(kind, TD_TowerPath.Second);
                Assert.IsNotEmpty(a.name, $"{kind} first elite");
                Assert.IsNotEmpty(b.name, $"{kind} second elite");
                Assert.AreNotEqual(a.name, b.name);
                Assert.IsNotEmpty(a.blurb);
                Assert.IsNotEmpty(b.blurb);
                // A specialisation is the biggest single purchase on that tower.
                Assert.Greater(a.cost, TD_Rules.UpgradeCost(kind, TD_Rules.MaxLevel - 1), $"{kind} {a.name} cost");
                Assert.Greater(b.cost, TD_Rules.UpgradeCost(kind, TD_Rules.MaxLevel - 1), $"{kind} {b.name} cost");
            }
            Assert.AreEqual("", TD_Elites.Get(TD_TowerKind.Gunner, TD_TowerPath.None).name);
        }

        [Test]
        public void UpgradingPastLevelThreeNeedsAPath() {
            var sim = MaxedTower(TD_TowerKind.Gunner, out int cell);
            Assert.AreEqual(TD_BuildResult.NeedsPath, sim.Build(cell, TD_TowerKind.Gunner, 0, TD_TowerPerks.None, out _));
            int cash = sim.Cash, spent = sim.Towers[cell].spent;
            Assert.AreEqual(TD_BuildResult.Specialised, sim.Specialise(cell, TD_TowerPath.First, TD_TowerPerks.None, out int cost));
            Assert.AreEqual(TD_Elites.Cost(TD_TowerKind.Gunner, TD_TowerPath.First), cost);
            Assert.AreEqual(cash - cost, sim.Cash);
            Assert.AreEqual(spent + cost, sim.Towers[cell].spent, "the elite counts toward the sale value");
            Assert.AreEqual(TD_TowerPath.First, sim.Towers[cell].path);
            Assert.AreEqual(TD_BuildResult.MaxLevel, sim.Specialise(cell, TD_TowerPath.Second, TD_TowerPerks.None, out _), "one elite form per tower");
            Assert.AreEqual(TD_BuildResult.MaxLevel, sim.Build(cell, TD_TowerKind.Gunner, 0, TD_TowerPerks.None, out _));
        }

        [Test]
        public void SpecialiseRefusesLowLevelsPoorTeamsAndEmptyPads() {
            var sim = new TD_Simulation { Cash = 100_000 };
            int cell = TD_TestCells.At(8);
            Assert.AreEqual(TD_BuildResult.BadSlot, sim.Specialise(cell, TD_TowerPath.First, TD_TowerPerks.None, out _), "empty pad");
            sim.Build(cell, TD_TowerKind.Cannon, 0, TD_TowerPerks.None, out _);
            Assert.AreEqual(TD_BuildResult.BadSlot, sim.Specialise(cell, TD_TowerPath.First, TD_TowerPerks.None, out _), "level 1");
            sim.Build(cell, TD_TowerKind.Cannon, 0, TD_TowerPerks.None, out _);
            sim.Build(cell, TD_TowerKind.Cannon, 0, TD_TowerPerks.None, out _);
            Assert.AreEqual(TD_BuildResult.BadSlot, sim.Specialise(cell, TD_TowerPath.None, TD_TowerPerks.None, out _), "no path");
            sim.Cash = 10;
            Assert.AreEqual(TD_BuildResult.NoCash, sim.Specialise(cell, TD_TowerPath.Second, TD_TowerPerks.None, out int cost));
            Assert.AreEqual(10, sim.Cash);
            Assert.AreEqual(TD_Elites.Cost(TD_TowerKind.Cannon, TD_TowerPath.Second), cost);
            Assert.AreEqual(TD_TowerPath.None, sim.Towers[cell].path);
        }

        [Test]
        public void EngineersDiscountTheElite() {
            var perks = TD_Retention.PerksFor(99);
            int full = TD_Elites.Cost(TD_TowerKind.Sniper, TD_TowerPath.First);
            Assert.Less(TD_Elites.Cost(TD_TowerKind.Sniper, TD_TowerPath.First, perks.upgradeDiscount), full);
        }

        [Test]
        public void MinigunFiresFaster() {
            var sim = MaxedTower(TD_TowerKind.Gunner, out int cell);
            float before = TD_Elites.FireRate(TD_TowerKind.Gunner, 3, TD_TowerPath.None, TD_Modifier.None);
            sim.Specialise(cell, TD_TowerPath.First, TD_TowerPerks.None, out _);
            Assert.AreEqual(before * 2.2f, TD_Elites.FireRate(TD_TowerKind.Gunner, 3, TD_TowerPath.First, TD_Modifier.None), 0.001f);
            // Shots over one second against a tank that will not die.
            var tank = sim.AddEnemy(TD_EnemyKind.Boss, RoadAt(cell), 50f);
            tank.stunUntil = 1e6f; // parked in range
            int shots = 0;
            for (int i = 0; i < 50; i++) {
                sim.Events.Clear();
                sim.Tick(0.02f);
                shots += sim.Events.shots.Count;
            }
            Assert.GreaterOrEqual(shots, UnityEngine.Mathf.FloorToInt(before * 2.2f) - 1);
        }

        [Test]
        public void FlakBurstsHitNearbyFlyers() {
            var sim = MaxedTower(TD_TowerKind.Gunner, out int cell);
            sim.Specialise(cell, TD_TowerPath.Second, TD_TowerPerks.None, out _);
            float at = RoadAt(cell);
            var lead = sim.AddEnemy(TD_EnemyKind.Chopper, at + 0.5f, 10f);
            var wing = sim.AddEnemy(TD_EnemyKind.Chopper, at, 10f);
            var ground = sim.AddEnemy(TD_EnemyKind.Scout, at, 10f);
            sim.Tick(0.02f);
            Assert.Less(lead.health, lead.maxHealth, "the target");
            Assert.Less(wing.health, wing.maxHealth, "a flyer next to it is caught in the burst");
            Assert.AreEqual(ground.maxHealth, ground.health, "bursts are for the air only");
        }

        [Test]
        public void ConcussionStunsButBossesShrugItOff() {
            var sim = MaxedTower(TD_TowerKind.Cannon, out int cell);
            sim.Specialise(cell, TD_TowerPath.Second, TD_TowerPerks.None, out _);
            float at = RoadAt(cell) + 3f;
            var tank = sim.AddEnemy(TD_EnemyKind.Tank, at, 20f);
            var boss = sim.AddEnemy(TD_EnemyKind.Boss, at, 20f);
            sim.Tick(0.02f);
            float now = sim.Time;
            Assert.Greater(tank.stunUntil - now, 0.3f, "a full stun on a tank");
            Assert.Greater(boss.stunUntil, now, "the boss is stunned too");
            Assert.Less(boss.stunUntil - now, tank.stunUntil - now, "but for less time");
            Assert.AreEqual(0f, tank.Slow(now));
        }

        [Test]
        public void BrittleIceMakesEveryHitHurtMore() {
            var plain = new TD_Simulation();
            var brittle = new TD_Simulation();
            var a = plain.AddEnemy(TD_EnemyKind.Tank, 0f, 10f);
            var b = brittle.AddEnemy(TD_EnemyKind.Tank, 0f, 10f);
            b.brittleUntil = 1e6f;
            int cell = TD_TestCells.At(0);
            float at = RoadAt(cell);
            a.distance = b.distance = at;
            a.stunUntil = b.stunUntil = 1e6f;
            plain.Cash = brittle.Cash = 10_000;
            plain.Build(cell, TD_TowerKind.Sniper, 0, TD_TowerPerks.None, out _);
            brittle.Build(cell, TD_TowerKind.Sniper, 0, TD_TowerPerks.None, out _);
            plain.Tick(0.02f);
            brittle.Tick(0.02f);
            float lostPlain = a.maxHealth - a.health, lostBrittle = b.maxHealth - b.health;
            Assert.Greater(lostPlain, 0f);
            Assert.AreEqual(lostPlain * (1f + TD_Elites.BrittleBonus), lostBrittle, 0.5f);
        }

        [Test]
        public void BrittleIceTowersMarkWhatTheyChill() {
            var sim = MaxedTower(TD_TowerKind.Frost, out int cell);
            sim.Specialise(cell, TD_TowerPath.Second, TD_TowerPerks.None, out _);
            var tank = sim.AddEnemy(TD_EnemyKind.Tank, RoadAt(cell), 10f);
            sim.Tick(0.02f);
            Assert.Greater(tank.brittleUntil, sim.Time);
        }

        [Test]
        public void DeepFreezeSlowsHarderButNeverFreezesSolid() {
            float plain = TD_Elites.SlowFactor(TD_TowerKind.Frost, 3, TD_TowerPath.None);
            float deep = TD_Elites.SlowFactor(TD_TowerKind.Frost, 3, TD_TowerPath.First);
            Assert.AreEqual(TD_Rules.SlowFactor(TD_TowerKind.Frost, 3), plain, 0.0001f);
            Assert.Less(deep, plain);
            Assert.GreaterOrEqual(deep, 1f - TD_Elites.MaxSlow - 0.0001f);
            Assert.Greater(TD_Elites.Range(TD_TowerKind.Frost, 3, TD_TowerPath.First, TD_Modifier.None), TD_Daily.Range(TD_TowerKind.Frost, 3, TD_Modifier.None));
        }

        [Test]
        public void StormCoilChainsFurther() {
            Assert.AreEqual(TD_Rules.ChainTargets(TD_TowerKind.Tesla, 3) + 3, TD_Elites.ChainTargets(TD_TowerKind.Tesla, 3, TD_TowerPath.First));
            Assert.AreEqual(1, TD_Elites.ChainTargets(TD_TowerKind.Gunner, 3, TD_TowerPath.First), "single-target towers stay single-target");
        }

        [Test]
        public void ElitesOutDamageLevelThree() {
            // Damage per second on one armourless target: every elite is a real step up from level 3.
            for (int k = 0; k < TD_Rules.Towers.Length; k++) {
                var kind = (TD_TowerKind)k;
                float baseDps = TD_Rules.Damage(kind, 3) * TD_Rules.FireRate(kind, 3);
                for (int p = 1; p <= TD_Elites.PathCount; p++) {
                    var path = (TD_TowerPath)p;
                    var e = TD_Elites.Get(kind, path);
                    float dps = TD_Elites.Damage(kind, 3, path) * TD_Elites.FireRate(kind, 3, path, TD_Modifier.None);
                    bool control = e.stunSeconds > 0f || e.brittle || e.slowScale > 1f || e.extraChains > 0 || e.airBurst > 0f;
                    if (control) Assert.GreaterOrEqual(dps, baseDps * 0.99f, $"{kind} {e.name} keeps its damage");
                    else Assert.Greater(dps, baseDps * 1.4f, $"{kind} {e.name} hits much harder");
                }
            }
        }
    }
}
