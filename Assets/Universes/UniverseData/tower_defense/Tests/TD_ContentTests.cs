using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RyanAssets.Retention;

namespace Universes.UniverseData.tower_defense.Tests {
    /// <summary>The retention pass: new enemies and towers, modes, abilities, stars, unlocks and the balance curve.</summary>
    public class TD_ContentTests {
        static TD_Simulation Sim(TD_Difficulty mode = TD_Difficulty.Normal) {
            var sim = new TD_Simulation(mode);
            sim.StartWave(1);
            return sim;
        }

        // ---------------------------------------------------------------- enemies

        [Test]
        public void WeakHitsBounceOffAShieldUntilItBreaks() {
            var sim = Sim();
            var e = sim.AddEnemy(TD_EnemyKind.Shield, 10f, 1f);
            float shield = e.shield;
            Assert.Greater(shield, 0f);
            var killed = new List<ushort>();
            Assert.AreEqual(TD_HitResult.Blocked, TD_Battle.Strike(sim.Enemies, e, TD_Rules.ShieldBlockBelow - 1f, false, killed));
            Assert.AreEqual(shield, e.shield, "a weak hit does nothing");
            Assert.AreEqual(TD_HitResult.Shield, TD_Battle.Strike(sim.Enemies, e, TD_Rules.ShieldBlockBelow, false, killed));
            Assert.AreEqual(shield - TD_Rules.ShieldBlockBelow, e.shield, 1e-3f);
            float health = e.health;
            Assert.AreEqual(TD_HitResult.ShieldBroken, TD_Battle.Strike(sim.Enemies, e, shield * 10f, false, killed));
            Assert.AreEqual(health, e.health, "the overflow of the breaking hit is lost");
            Assert.AreEqual(TD_HitResult.Health, TD_Battle.Strike(sim.Enemies, e, 5f, false, killed), "once broken, any hit counts");
            Assert.AreEqual(TD_HitResult.Blocked, TD_Battle.Strike(sim.Enemies, sim.AddEnemy(TD_EnemyKind.Shield, 0f, 1f), 1f, false, killed));
            Assert.AreEqual(TD_HitResult.Shield, TD_Battle.Strike(sim.Enemies, sim.AddEnemy(TD_EnemyKind.Shield, 0f, 1f), 1f, false, killed, ignoreBlock: true), "bombs ignore the bounce");
        }

        [Test]
        public void GunnersAndFrostCannotTouchShieldsWithoutVeteranCrews() {
            Assert.Less(TD_Rules.Damage(TD_TowerKind.Gunner, 3), TD_Rules.ShieldBlockBelow, "shields are the Gunner's counter");
            Assert.Less(TD_Rules.Damage(TD_TowerKind.Frost, TD_Rules.MaxLevel), TD_Rules.ShieldBlockBelow);
            Assert.GreaterOrEqual(TD_Rules.Damage(TD_TowerKind.Gunner, 3) * TD_Retention.VeteranDamage, TD_Rules.ShieldBlockBelow, "Veteran Crews lets level-3 Gunners break shields");
            foreach (var breaker in new[] { TD_TowerKind.Cannon, TD_TowerKind.Sniper, TD_TowerKind.Tesla, TD_TowerKind.Mortar })
                Assert.GreaterOrEqual(TD_Rules.Damage(breaker, 1), TD_Rules.ShieldBlockBelow, $"{breaker} breaks shields from level 1");
            StringAssert.Contains("Gunners to break shields", TD_Retention.Find(TD_Retention.VeteranUnlock).description, "the unlock says what it crosses");
        }

        [Test]
        public void ABlockedShotIsFlaggedForTheClient() {
            var sim = Sim();
            var tank = sim.AddEnemy(TD_EnemyKind.Shield, TD_Rules.LaneTrack.Project(TD_Rules.Slots[TD_TestCells.At(8)]), 1f);
            sim.Build(TD_TestCells.At(8), TD_TowerKind.Gunner, 0, TD_TowerPerks.None, out _);
            sim.Tick(0.05f);
            int dbgCell = TD_TestCells.At(8);
            Assert.AreEqual(1, sim.Events.shots.Count, $"cell {dbgCell} at {TD_Rules.Slots[dbgCell]} lane distance {TD_Rules.DistanceToLane(TD_Rules.Slots[dbgCell]):0.0} range {TD_Rules.Range(TD_TowerKind.Gunner, 1):0.0} enemy at {TD_Rules.LaneTrack.Evaluate(tank.distance)} towers {sim.Towers[dbgCell] != null}");
            Assert.IsTrue(sim.Events.shots[0].blocked, "a Gunner hit on a full shield bounces");
            Assert.AreEqual(tank.maxShield, tank.shield);
            sim.Events.Clear();
            tank.shield = 0f;
            sim.Tick(1f);
            Assert.Greater(sim.Events.shots.Count, 0);
            Assert.IsFalse(sim.Events.shots.Any(s => s.blocked), "once the shield is gone nothing bounces");
        }

        [Test]
        public void EveryNewEnemyIsIntroducedWithItsCounter() {
            Assert.AreEqual(1, TD_Rules.FirstWave(TD_EnemyKind.Scout));
            Assert.AreEqual(6, TD_Rules.FirstWave(TD_EnemyKind.Shield));
            Assert.IsTrue(TD_Rules.IsNew(TD_EnemyKind.Shield, 6));
            Assert.IsFalse(TD_Rules.IsNew(TD_EnemyKind.Shield, 7));
            Assert.IsFalse(TD_Rules.IsNew(TD_EnemyKind.Scout, 1), "wave 1 introduces nothing");
            for (int i = 0; i < TD_Rules.Enemies.Length; i++) {
                var kind = (TD_EnemyKind)i;
                int first = TD_Rules.FirstWave(kind);
                Assert.That(first, Is.InRange(1, TD_Rules.WaveCount), $"{kind} comes in a 20-wave game");
                if (first <= 1) continue;
                Assert.IsNotEmpty(TD_Rules.Enemy(kind).trait, kind.ToString());
                Assert.IsNotEmpty(TD_Rules.Enemy(kind).counter, kind.ToString());
            }
        }

        [Test]
        public void FrostCannotSlowAShieldedEnemy() {
            var sim = Sim();
            var tank = sim.AddEnemy(TD_EnemyKind.Shield, TD_Rules.LaneTrack.Project(TD_Rules.Slots[TD_TestCells.At(12)]), 1f);
            sim.Build(TD_TestCells.At(12), TD_TowerKind.Frost, 0, TD_TowerPerks.None, out _);
            sim.Tick(0.05f);
            Assert.AreEqual(1f, tank.Slow(sim.Time), "shielded: no slow");
            tank.shield = 0f;
            sim.Tick(1f);
            Assert.Less(tank.Slow(sim.Time), 1f, "unshielded: slowed");
        }

        [Test]
        public void MedicsHealOthersButNotThemselvesOrPastFull() {
            var enemies = new List<TD_SimEnemy> {
                new() { id = 1, kind = TD_EnemyKind.Medic, distance = 50f, health = 100f, maxHealth = 200f },
                new() { id = 2, kind = TD_EnemyKind.Tank, distance = 55f, health = 100f, maxHealth = 1000f },
                new() { id = 3, kind = TD_EnemyKind.Tank, distance = 50f + TD_Rules.HealRadius + 1f, health = 100f, maxHealth = 1000f },
                new() { id = 4, kind = TD_EnemyKind.Apc, distance = 45f, health = 99.5f, maxHealth = 100f },
            };
            var healed = new List<ushort>();
            TD_Battle.Heal(enemies, 1f, healed);
            float rate = TD_Rules.Enemy(TD_EnemyKind.Medic).heal * 200f;
            Assert.AreEqual(100f, enemies[0].health, "a medic does not heal itself");
            Assert.AreEqual(100f + rate, enemies[1].health, 1e-3f);
            Assert.AreEqual(100f, enemies[2].health, "out of reach");
            Assert.AreEqual(100f, enemies[3].health, 1e-3f, "never above full");
            CollectionAssert.AreEquivalent(new ushort[] { 2, 4 }, healed);
        }

        [Test]
        public void ACarrierReleasesDronesWhenDestroyed() {
            var sim = Sim();
            var carrier = sim.AddEnemy(TD_EnemyKind.Carrier, 40f, 1f);
            carrier.health = 1f;
            Assert.IsTrue(sim.Airstrike(7, out _));
            Assert.IsFalse(sim.Enemies.Contains(carrier));
            var drones = sim.Enemies.Where(e => e.kind == TD_EnemyKind.Drone).ToList();
            Assert.AreEqual(TD_Rules.Enemy(TD_EnemyKind.Carrier).splits, drones.Count);
            Assert.IsTrue(drones.All(d => d.distance <= 40f && d.distance >= 40f - TD_Rules.SplitSpacing * drones.Count));
            Assert.IsTrue(sim.Events.kills.Any(k => k.kind == TD_EnemyKind.Carrier && k.owner == 7 && k.slot == -1), "the caster gets the kill");
            Assert.AreEqual(TD_Rules.EnemyCount(11), TD_Rules.WaveCounts(11).Select((n, i) => n * (1 + TD_Rules.Enemies[i].splits)).Sum(), "the wave count includes the drones");
        }

        [Test]
        public void EveryEnemyHasAReasonToBuildDifferently() {
            Assert.IsTrue(TD_Rules.Enemy(TD_EnemyKind.Drone).flying, "drones beat ground-only Cannons and Mortars");
            Assert.IsFalse(TD_Rules.CanHit(TD_TowerKind.Mortar, TD_EnemyKind.Drone));
            Assert.Greater(TD_Rules.Enemy(TD_EnemyKind.Shield).shield, 0f);
            Assert.Greater(TD_Rules.Enemy(TD_EnemyKind.Medic).heal, 0f);
            Assert.Greater(TD_Rules.Enemy(TD_EnemyKind.Carrier).splits, 0);
            foreach (var stats in TD_Rules.Enemies) Assert.Greater(stats.reward, 0, stats.name);
            for (int kind = (int)TD_EnemyKind.Shield; kind < TD_Rules.Enemies.Length; kind++) {
                int first = Enumerable.Range(1, TD_Rules.WaveCount).FirstOrDefault(w => TD_Rules.WaveCounts(w)[kind] > 0);
                Assert.That(first, Is.InRange(5, 12), $"{TD_Rules.Enemies[kind].name} first comes on wave {first}");
            }
        }

        // ---------------------------------------------------------------- towers

        [Test]
        public void TeslaChainsThroughACrowdLosingDamageEachJump() {
            var sim = Sim();
            float at = TD_Rules.LaneTrack.Project(TD_Rules.Slots[TD_TestCells.At(9)]);
            var crowd = Enumerable.Range(0, 6).Select(i => sim.AddEnemy(TD_EnemyKind.Drone, at + i * 2f, 50f)).ToList();
            sim.Build(TD_TestCells.At(9), TD_TowerKind.Tesla, 0, TD_TowerPerks.None, out _);
            sim.Tick(0.01f);
            int targets = TD_Rules.ChainTargets(TD_TowerKind.Tesla, 1);
            Assert.AreEqual(targets, sim.Events.shots.Count, "one arc per enemy hit");
            Assert.AreEqual(0, sim.Events.shots[0].from, "the first arc leaves the tower");
            Assert.IsTrue(sim.Events.shots.Skip(1).All(s => s.from != 0), "the others jump from an enemy");
            var hurt = crowd.Where(e => e.health < e.maxHealth).OrderBy(e => e.health).ToList();
            Assert.AreEqual(targets, hurt.Count);
            float hit = TD_Rules.Damage(TD_TowerKind.Tesla, 1);
            Assert.AreEqual(hurt[0].maxHealth - hit, hurt[0].health, 1e-2f);
            Assert.AreEqual(hurt[^1].maxHealth - hit * UnityEngine.Mathf.Pow(TD_Rules.ChainFalloff, targets - 1), hurt[^1].health, 1e-2f);
            Assert.Greater(TD_Rules.ChainTargets(TD_TowerKind.Tesla, 3), targets, "upgrades add jumps");
            Assert.AreEqual(1, TD_Rules.ChainTargets(TD_TowerKind.Gunner, 3));
        }

        [Test]
        public void MortarsCannotShellEnemiesTooClose() {
            var sim = Sim();
            int cell = TD_TestCells.NextToRoad();
            float near = TD_Rules.LaneTrack.Project(TD_Rules.Slots[cell]);
            var close = sim.AddEnemy(TD_EnemyKind.Tank, near, 1f);
            sim.Build(cell, TD_TowerKind.Mortar, 0, TD_TowerPerks.None, out _);
            sim.Tick(0.01f);
            Assert.IsEmpty(sim.Events.shots, "inside the minimum range");
            close.distance = near + 25f;
            sim.Tick(0.01f);
            Assert.AreEqual(1, sim.Events.shots.Count);
            Assert.Greater(TD_Rules.Tower(TD_TowerKind.Mortar).range, TD_Rules.Tower(TD_TowerKind.Sniper).range);
        }

        [Test]
        public void SnipersShootTheToughestEnemyInRange() {
            var sim = Sim();
            float at = TD_Rules.LaneTrack.Project(TD_Rules.Slots[TD_TestCells.At(13)]);
            sim.AddEnemy(TD_EnemyKind.Scout, at + 5f, 1f);
            var tank = sim.AddEnemy(TD_EnemyKind.Tank, at - 5f, 1f);
            sim.Build(TD_TestCells.At(13), TD_TowerKind.Sniper, 0, TD_TowerPerks.None, out _);
            sim.Tick(0.01f);
            Assert.AreEqual(tank.id, sim.Events.shots.Single().enemy);
        }

        [Test]
        public void PerksChangeWhatATowerDoesAndPays() {
            var sim = Sim();
            sim.Cash = 10000;
            var perks = TD_Retention.PerksFor(99);
            Assert.AreEqual(TD_TowerPerks.None.refundRate, TD_Retention.PerksFor(1).refundRate);
            Assert.AreEqual(TD_Retention.SalvageRefundRate, perks.refundRate);
            Assert.AreEqual(TD_BuildResult.Built, sim.Build(TD_TestCells.At(3), TD_TowerKind.Gunner, 1, perks, out int built));
            Assert.AreEqual(TD_BuildResult.Upgraded, sim.Build(TD_TestCells.At(3), TD_TowerKind.Gunner, 1, perks, out int upgrade));
            Assert.AreEqual(TD_Rules.UpgradeCost(TD_TowerKind.Gunner, 1, TD_Retention.EngineersDiscount), upgrade);
            Assert.Less(upgrade, TD_Rules.UpgradeCost(TD_TowerKind.Gunner, 1));
            int cash = sim.Cash;
            Assert.IsTrue(sim.Sell(TD_TestCells.At(3), out var sold));
            Assert.AreEqual(UnityEngine.Mathf.RoundToInt((built + upgrade) * TD_Retention.SalvageRefundRate), sim.Cash - cash);
            Assert.IsNull(sim.Towers[TD_TestCells.At(3)]);
            var poor = new TD_Simulation { Cash = 10 };
            Assert.AreEqual(TD_BuildResult.NoCash, poor.Build(TD_TestCells.At(0), TD_TowerKind.Gunner, 0, TD_TowerPerks.None, out int price));
            Assert.AreEqual(TD_Rules.Tower(TD_TowerKind.Gunner).cost, price, "the price needed is reported");
            Assert.AreEqual(10, poor.Cash);
            Assert.AreEqual(TD_BuildResult.BadSlot, poor.Build(TD_Rules.Slots.Length, TD_TowerKind.Gunner, 0, TD_TowerPerks.None, out _));
        }

        // ---------------------------------------------------------------- abilities

        [Test]
        public void AirstrikeBombsTheLeadGroupAndEmpStopsEverything() {
            var sim = Sim();
            var lead = sim.AddEnemy(TD_EnemyKind.Shield, 200f, 1f);
            var beside = sim.AddEnemy(TD_EnemyKind.Apc, 195f, 1f);
            var far = sim.AddEnemy(TD_EnemyKind.Apc, 120f, 1f);
            Assert.IsTrue(sim.Airstrike(3, out float at));
            Assert.AreEqual(200f, at);
            Assert.Less(lead.shield, lead.maxShield, "bombs go through the bounce rule");
            Assert.IsFalse(sim.Enemies.Contains(beside), "an APC in the blast is destroyed");
            Assert.AreEqual(far.maxHealth, far.health, "out of the blast");
            Assert.Greater(sim.Emp(3), 0);
            Assert.AreEqual(0f, lead.Slow(sim.Time), "stunned");
            Assert.AreEqual(0f, lead.shield, "shield stripped");
            sim.Tick(TD_Abilities.Get(TD_Ability.Emp).stunSeconds + 0.1f);
            Assert.AreEqual(1f, far.Slow(sim.Time), "the stun wears off");
            Assert.IsFalse(new TD_Simulation().Airstrike(0, out _), "nothing to bomb on an empty road");
        }

        [Test]
        public void ACalledAirstrikeLandsWhereTheLeadEnemyWillBe() {
            var sim = new TD_Simulation(); // no wave started: no spawns, only the enemies added here
            var lead = sim.AddEnemy(TD_EnemyKind.Apc, 150f, 1f);
            const float delay = 1f;
            Assert.IsTrue(sim.CallAirstrike(2, delay, out float at, out int around));
            Assert.AreEqual(150f + TD_Rules.Enemy(TD_EnemyKind.Apc).speed * delay, at, 0.01f, "aimed ahead, where the APC will be");
            Assert.AreEqual(1, around);
            Assert.AreEqual(lead.maxHealth, lead.health, "nothing lands while the jet is on its way");
            Assert.AreEqual(1, sim.PendingStrikes);
            sim.Tick(delay + 0.01f);
            Assert.AreEqual(0, sim.PendingStrikes);
            Assert.IsFalse(sim.Enemies.Contains(lead), "the bombs land on it");
            Assert.IsTrue(sim.Events.kills.Exists(k => k.enemy == lead.id && k.owner == 2 && k.slot < 0), "the kill is the caller's, from an ability");
            Assert.IsFalse(sim.CallAirstrike(2, delay, out _, out _), "an empty road: nothing to call");
            sim.AddEnemy(TD_EnemyKind.Apc, 40f, 1f);
            sim.CallAirstrike(2, delay, out _, out _);
            sim.StartWave(2);
            Assert.AreEqual(0, sim.PendingStrikes, "a new wave drops strikes left over from the last one");
        }

        [Test]
        public void AbilitiesRechargeByWaves() {
            Assert.IsTrue(TD_Abilities.Ready(TD_Ability.Airstrike, 0, 1), "never used: ready");
            Assert.IsFalse(TD_Abilities.Ready(TD_Ability.Airstrike, 4, 4), "once a wave");
            Assert.IsTrue(TD_Abilities.Ready(TD_Ability.Airstrike, 4, 5));
            int cooldown = TD_Abilities.Get(TD_Ability.Emp).cooldownWaves;
            Assert.Greater(cooldown, 1);
            Assert.AreEqual(cooldown - 1, TD_Abilities.WavesLeft(TD_Ability.Emp, 5, 6));
            Assert.AreEqual(0, TD_Abilities.WavesLeft(TD_Ability.Emp, 5, 5 + cooldown));
            Assert.Greater(TD_Abilities.StrikeDamage(15, TD_Difficulty.Normal), TD_Abilities.StrikeDamage(5, TD_Difficulty.Normal), "keeps pace with the enemies");
        }

        // ---------------------------------------------------------------- modes and stars

        [Test]
        public void TheModeVoteGoesToTheMostVotesAndTiesToTheEasierMode() {
            Assert.AreEqual(TD_Difficulty.Normal, TD_Modes.Winner(new int[3]));
            Assert.AreEqual(TD_Difficulty.Normal, TD_Modes.Winner(null));
            Assert.AreEqual(TD_Difficulty.Hard, TD_Modes.Winner(new[] { 1, 2, 0 }));
            Assert.AreEqual(TD_Difficulty.Normal, TD_Modes.Winner(new[] { 1, 1, 1 }));
            Assert.AreEqual(TD_Difficulty.Hard, TD_Modes.Winner(new[] { 0, 2, 2 }));
            Assert.AreEqual(TD_Difficulty.Endless, TD_Modes.Winner(new[] { 0, 0, 1 }));
        }

        [Test]
        public void HardIsTougherAndEndlessNeverStops() {
            var normal = TD_Modes.Get(TD_Difficulty.Normal);
            var hard = TD_Modes.Get(TD_Difficulty.Hard);
            Assert.Greater(hard.healthScale, normal.healthScale);
            Assert.Less(hard.rewardScale, normal.rewardScale);
            Assert.Greater(hard.xpScale, normal.xpScale, "Hard pays more XP");
            Assert.IsTrue(TD_Modes.Get(TD_Difficulty.Endless).Endless);
            Assert.IsFalse(new TD_Simulation(TD_Difficulty.Endless).IsLastWave);
            Assert.AreEqual(TD_Rules.HealthScale(20, TD_Difficulty.Normal), TD_Rules.HealthScale(20, TD_Difficulty.Endless), 1e-4f, "Endless is Normal up to wave 20");
            Assert.Greater(TD_Rules.HealthScale(25, TD_Difficulty.Endless) / TD_Rules.HealthScale(24, TD_Difficulty.Endless), TD_Rules.EndlessGrowth, "and then it keeps climbing");
            Assert.IsTrue(TD_Rules.HasBoss(30) && TD_Rules.WaveCounts(30)[(int)TD_EnemyKind.Boss] == 3, "a Heavy Tank more every tenth wave");
            Assert.Greater(TD_Rules.EnemyCount(30), TD_Rules.EnemyCount(20));
        }

        [Test]
        public void StarsCountTheBaseLeft() {
            Assert.AreEqual(3, TD_Rules.Stars(100));
            Assert.AreEqual(3, TD_Rules.Stars(80));
            Assert.AreEqual(2, TD_Rules.Stars(79));
            Assert.AreEqual(2, TD_Rules.Stars(40));
            Assert.AreEqual(1, TD_Rules.Stars(1));
            Assert.AreEqual(0, TD_Rules.Stars(0));
        }

        // ---------------------------------------------------------------- unlocks and challenges

        [Test]
        public void TheUnlockTableIsOrderedAndCoversTheNewContent() {
            var unlocks = TD_Retention.Unlocks;
            Assert.That(unlocks.Length, Is.InRange(6, 14));
            Assert.GreaterOrEqual(unlocks[^1].level, 20, "the top of the track has somewhere to go past level 16");
            for (int i = 1; i < unlocks.Length; i++) Assert.Greater(unlocks[i].level, unlocks[i - 1].level, "one unlock per level, in order");
            Assert.AreEqual(unlocks.Length, unlocks.Select(u => u.id).Distinct().Count());
            Assert.Greater(unlocks[0].level, 1, "level 1 starts with nothing extra");
            foreach (var tower in TD_Rules.Towers.Where(t => t.unlockId.Length > 0)) Assert.IsNotNull(TD_Retention.Find(tower.unlockId), tower.name);
            for (int a = 0; a < TD_Abilities.Count; a++) Assert.IsNotNull(TD_Retention.Find(TD_Abilities.Get((TD_Ability)a).unlockId));
            Assert.IsNotNull(TD_Retention.Find(TD_Retention.EndlessUnlock));
            Assert.AreEqual(TD_Retention.EndlessLevel, TD_Retention.LevelOf(TD_Retention.EndlessUnlock));
            Assert.IsTrue(TD_Retention.CanBuild(1, TD_TowerKind.Gunner));
            Assert.IsFalse(TD_Retention.CanBuild(1, TD_TowerKind.Tesla));
            Assert.IsTrue(TD_Retention.CanBuild(TD_Retention.LevelOf(TD_Retention.TeslaUnlock), TD_TowerKind.Tesla));
        }

        [Test]
        public void TheChallengePoolIsValidAndVaried() {
            var rules = TD_Retention.Rules(new RetentionTuning()); // throws on an invalid entry
            Assert.GreaterOrEqual(rules.ChallengePool.Count, 8);
            Assert.AreEqual(TD_Retention.Challenges.Length, TD_Retention.Challenges.Select(c => c.statKey).Distinct().Count(), "one challenge per stat, so days differ");
            Assert.IsTrue(TD_Retention.Challenges.All(c => c.text.Contains(ChallengeDefinition.TargetToken) || c.target == 1));
            for (int day = 20000; day < 20014; day++) {
                var picks = rules.ChallengesFor(day);
                Assert.AreEqual(rules.Tuning.dailyChallengeCount, picks.Count);
                Assert.AreEqual(picks.Count, picks.Select(c => c.statKey).Distinct().Count());
            }
            Assert.IsTrue(TD_Retention.Challenges.All(c => c.id.Length <= RetentionSave.MaxIdLength));
        }

        // ---------------------------------------------------------------- balance (whole games on the simulation)

        [Test]
        public void AGreedyAllGunnerBuildFallsOnNormalAroundWave15() {
            var result = new TD_BalanceBot().Play(TD_BalanceBot.GreedyGunners, TD_Difficulty.Normal);
            Assert.IsFalse(result.won, result.Report);
            Assert.That(result.lostOnWave, Is.InRange(11, 16), result.Report);
        }

        [Test]
        public void AMixedBuildWinsNormalButFeelsTheSecondHalf() {
            var result = new TD_BalanceBot().Play(TD_BalanceBot.Balanced, TD_Difficulty.Normal);
            Assert.IsTrue(result.won, result.Report);
            // With free placement the bot puts every tower on its best cell, so a mixed build may now hold the whole game.
            Assert.LessOrEqual(result.baseHealth, TD_Rules.BaseHealth, result.Report);
            Assert.That(result.firstLeakWave == 0 || result.firstLeakWave >= 8, "the first leaks come in the second half: " + result.Report);
        }

        [Test]
        public void HardNeedsTheUnlocks() {
            var bot = new TD_BalanceBot();
            var basic = bot.Play(TD_BalanceBot.Balanced, TD_Difficulty.Hard);
            Assert.IsFalse(basic.won && TD_Rules.Stars(basic.baseHealth) == TD_Rules.MaxStars, "no easy 3 stars on Hard without unlocks: " + basic.Report);
            bot.abilityAt = 0.7f;
            var unlocked = bot.Play(TD_BalanceBot.Unlocked, TD_Difficulty.Hard);
            Assert.IsTrue(unlocked.won, "Tesla, Artillery and abilities make Hard winnable: " + unlocked.Report);
        }

        [Test]
        public void MoreCastersHelpButCannotCarryAGreedyBuild() {
            Assert.AreEqual(1f, TD_Abilities.RepeatScale(0), "a lone caster is never weakened");
            Assert.Less(TD_Abilities.RepeatScale(3), TD_Abilities.RepeatScale(1));
            foreach (int casters in new[] { 2, 4 }) {
                var bot = new TD_BalanceBot { abilityAt = 0.7f, casters = casters };
                var greedy = bot.Play(TD_BalanceBot.GreedyGunners, TD_Difficulty.Normal);
                Assert.IsFalse(greedy.won, $"{casters} casters: " + greedy.Report);
                Assert.LessOrEqual(greedy.lostOnWave, 18, $"{casters} casters carried Gunners too far: " + greedy.Report);
                var hard = bot.Play(TD_BalanceBot.Balanced, TD_Difficulty.Hard);
                Assert.IsTrue(hard.won, $"{casters} casters on Hard: " + hard.Report);
            }
        }

        [Test]
        public void AStrikeAfterTheFirstInAWaveHitsSofter() {
            var sim = Sim();
            var first = sim.AddEnemy(TD_EnemyKind.Scout, 100f, 100f); // no armour, too tough to die
            sim.Airstrike(0, out _);
            float one = first.maxHealth - first.health;
            first.health = first.maxHealth;
            sim.Airstrike(1, out _);
            float two = first.maxHealth - first.health;
            Assert.Greater(one, 0f);
            Assert.AreEqual(one * TD_Abilities.RepeatFalloff, two, one * 0.02f);
            sim.StartWave(2);
            Assert.AreEqual(0, sim.StrikesThisWave, "a new wave starts fresh");
        }

        [Test]
        public void ALateJoinerDoesNotWinTheGame() {
            Assert.IsTrue(TD_Rules.WinCounts(1, 20));
            Assert.IsTrue(TD_Rules.WinCounts(TD_Rules.LastWinJoinWave(20), 20));
            Assert.IsFalse(TD_Rules.WinCounts(TD_Rules.LastWinJoinWave(20) + 1, 20));
            Assert.IsFalse(TD_Rules.WinCounts(20, 20), "joined on the last wave");
            Assert.IsTrue(TD_Rules.WinCounts(0, 7), "a game joined in the vote counts from wave 1");
            Assert.AreEqual(11, TD_Rules.LastWinJoinWave(20));
        }

        // ---------------------------------------------------------------- daily modifier

        [Test]
        public void TheDailyModifierChangesEveryDayAndVisitsThemAll() {
            var seen = new HashSet<TD_Modifier>();
            for (int day = 20000; day < 22000; day++) {
                var today = TD_Daily.For(day);
                Assert.AreNotEqual(TD_Modifier.None, today);
                Assert.IsTrue(TD_Daily.IsValid(today));
                Assert.AreNotEqual(TD_Daily.For(day - 1), today, $"day {day} repeats the day before");
                Assert.AreEqual(today, TD_Daily.For(day), "the same all day, on every server");
                seen.Add(today);
            }
            Assert.AreEqual(TD_Daily.Count - 1, seen.Count, "every modifier comes up");
            for (int i = 1; i < TD_Daily.Count; i++) {
                var stats = TD_Daily.Get((TD_Modifier)i);
                Assert.IsNotEmpty(stats.name);
                Assert.IsNotEmpty(stats.blurb);
            }
            Assert.AreEqual(TD_Modifier.None, new TD_Simulation().Modifier, "tests and old callers play the plain game");
        }

        [Test]
        public void AModifierChangesTheNumbersBothSidesUse() {
            var fog = new TD_Simulation(TD_Difficulty.Normal, TD_Modifier.Fog);
            Assert.Less(TD_Daily.Range(TD_TowerKind.Gunner, 1, TD_Modifier.Fog), TD_Rules.Range(TD_TowerKind.Gunner, 1));
            Assert.Greater(TD_Daily.KillReward(TD_EnemyKind.Tank, TD_Difficulty.Normal, TD_Modifier.Fog), TD_Rules.KillReward(TD_EnemyKind.Tank, TD_Difficulty.Normal));
            Assert.AreEqual(TD_Rules.StartCash, fog.Cash);
            var iron = new TD_Simulation(TD_Difficulty.Normal, TD_Modifier.IronHulls);
            Assert.Greater(iron.Cash, TD_Rules.StartCash);
            iron.StartWave(1);
            Assert.Greater(iron.AddEnemy(TD_EnemyKind.Tank).maxHealth, new TD_Simulation().AddEnemy(TD_EnemyKind.Tank).maxHealth);
            var plain = new TD_Simulation();
            var blitz = new TD_Simulation(TD_Difficulty.Normal, TD_Modifier.Blitz);
            var a = plain.AddEnemy(TD_EnemyKind.Scout);
            var b = blitz.AddEnemy(TD_EnemyKind.Scout);
            plain.Tick(1f);
            blitz.Tick(1f);
            Assert.Greater(b.distance, a.distance, "Blitz enemies are faster");
        }

        [Test]
        public void EveryDailyModifierKeepsTheBalanceBar() {
            for (int i = 1; i < TD_Daily.Count; i++) {
                var modifier = (TD_Modifier)i;
                var bot = new TD_BalanceBot();
                var greedy = bot.Play(TD_BalanceBot.GreedyGunners, TD_Difficulty.Normal, modifier: modifier);
                Assert.IsFalse(greedy.won, $"{modifier}: greedy Gunners " + greedy.Report);
                Assert.That(greedy.lostOnWave, Is.InRange(11, 16), $"{modifier}: " + greedy.Report);
                var mixed = bot.Play(TD_BalanceBot.Balanced, TD_Difficulty.Normal, modifier: modifier);
                Assert.IsTrue(mixed.won, $"{modifier}: a mixed build wins Normal " + mixed.Report);
                bot.abilityAt = 0.7f;
                var hard = bot.Play(TD_BalanceBot.Unlocked, TD_Difficulty.Hard, modifier: modifier);
                Assert.IsTrue(hard.won, $"{modifier}: unlocks still win Hard " + hard.Report);
            }
        }

        [Test]
        public void EndlessGoesPastWaveTwentyAndThenFalls() {
            var bot = new TD_BalanceBot { abilityAt = 0.7f };
            var result = bot.Play(TD_BalanceBot.Unlocked, TD_Difficulty.Endless, 60);
            Assert.Greater(result.wavesCleared, TD_Rules.WaveCount, result.Report);
            Assert.Greater(result.lostOnWave, 0, "the growth catches up with any build: " + result.Report);
        }

        [Test]
        public void TheBalanceDoesNotDependOnTheTickRate() {
            var coarse = new TD_BalanceBot { tick = 0.05f }.Play(TD_BalanceBot.Balanced, TD_Difficulty.Normal);
            var fine = new TD_BalanceBot { tick = 0.02f }.Play(TD_BalanceBot.Balanced, TD_Difficulty.Normal);
            Assert.AreEqual(coarse.won, fine.won, coarse.Report + fine.Report);
            Assert.AreEqual(coarse.baseHealth, fine.baseHealth, 25, coarse.Report + fine.Report);
        }

        [Test]
        public void EveryKindModeAndAbilityHasAColourAndIcon() {
            for (int i = 0; i < TD_Rules.Towers.Length; i++) {
                Assert.DoesNotThrow(() => TD_Palette.Tower((TD_TowerKind)i));
                StringAssert.StartsWith("tower_", TD_Palette.TowerIcon((TD_TowerKind)i));
            }
            for (int i = 0; i < TD_Rules.Enemies.Length; i++) Assert.DoesNotThrow(() => TD_Palette.Enemy((TD_EnemyKind)i));
            for (int i = 0; i < TD_Modes.Count; i++) Assert.DoesNotThrow(() => TD_Palette.Mode((TD_Difficulty)i));
            for (int i = 0; i < TD_Abilities.Count; i++) Assert.DoesNotThrow(() => TD_Palette.Ability((TD_Ability)i));
        }

        [Test]
        public void ModesOpenFromTheProfile() {
            Assert.IsTrue(TD_Modes.Open(null, TD_Difficulty.Normal), "Normal before any profile");
            Assert.IsFalse(TD_Modes.Open(null, TD_Difficulty.Hard));
            Assert.IsTrue(TD_Modes.Open(new[] { true, true, false }, TD_Difficulty.Hard));
            Assert.IsFalse(TD_Modes.Open(new[] { true, true, false }, TD_Difficulty.Endless));
            Assert.IsFalse(TD_Modes.Open(new[] { true }, TD_Difficulty.Endless), "a short array from an older server");
        }
    }
}
