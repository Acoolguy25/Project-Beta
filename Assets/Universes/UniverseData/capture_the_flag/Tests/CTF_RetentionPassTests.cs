using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Universes.UniverseData.capture_the_flag.Tests {
    /// <summary>Rules added in the retention pass: loadouts, unlocks, challenges, medals, bots and the MVP.</summary>
    public sealed class CTF_RetentionPassTests {
        // ---------------------------------------------------------------- loadouts
        [Test]
        public void ANewPlayerOwnsOnlyTheSidearmAndNoPerk() {
            byte guns = CTF_Loadouts.OwnedWeapons(_ => false);
            byte perks = CTF_Loadouts.OwnedPerks(_ => false);
            Assert.That(CTF_Loadouts.CountOwned(guns, CTF_Loadouts.Weapons.Length), Is.EqualTo(1));
            Assert.That(CTF_Loadouts.Has(guns, (int)CTF_Weapon.Sidearm), Is.True);
            Assert.That(CTF_Loadouts.CountOwned(perks, CTF_Loadouts.Perks.Length), Is.EqualTo(1));
            Assert.That(CTF_Loadouts.Has(perks, (int)CTF_Perk.None), Is.True);
        }

        [Test]
        public void CyclingSkipsLockedGunsAndWraps() {
            byte owned = CTF_Loadouts.OwnedWeapons(id => id == CTF_Retention.UnlockMarksman);
            int count = CTF_Loadouts.Weapons.Length;
            Assert.That(CTF_Loadouts.Cycle((int)CTF_Weapon.Sidearm, 1, owned, count), Is.EqualTo((int)CTF_Weapon.Marksman));
            Assert.That(CTF_Loadouts.Cycle((int)CTF_Weapon.Marksman, 1, owned, count), Is.EqualTo((int)CTF_Weapon.Sidearm));
            Assert.That(CTF_Loadouts.Cycle((int)CTF_Weapon.Sidearm, -1, owned, count), Is.EqualTo((int)CTF_Weapon.Marksman));
            byte onlySidearm = CTF_Loadouts.OwnedWeapons(_ => false);
            Assert.That(CTF_Loadouts.Cycle((int)CTF_Weapon.Sidearm, 1, onlySidearm, count), Is.EqualTo((int)CTF_Weapon.Sidearm));
        }

        [Test]
        public void EveryGunAndPerkIsInItsEnumOrderAndUnlockedByALevel() {
            for (int i = 0; i < CTF_Loadouts.Weapons.Length; i++) {
                Assert.That((int)CTF_Loadouts.Weapons[i].weapon, Is.EqualTo(i));
                string unlock = CTF_Loadouts.Weapons[i].unlockId;
                if (unlock != null) Assert.That(CTF_Retention.LevelOf(unlock), Is.GreaterThan(1), unlock);
            }
            for (int i = 0; i < CTF_Loadouts.Perks.Length; i++) {
                Assert.That((int)CTF_Loadouts.Perks[i].perk, Is.EqualTo(i));
                string unlock = CTF_Loadouts.Perks[i].unlockId;
                if (unlock != null) Assert.That(CTF_Retention.LevelOf(unlock), Is.GreaterThan(1), unlock);
            }
            Assert.That(CTF_Loadouts.WeaponById("nope"), Is.EqualTo(CTF_Weapon.Sidearm));
            Assert.That(CTF_Loadouts.PerkById(null), Is.EqualTo(CTF_Perk.None));
            Assert.That(CTF_Loadouts.WeaponById(CTF_Loadouts.Get(CTF_Weapon.Scattergun).id), Is.EqualTo(CTF_Weapon.Scattergun));
        }

        [Test]
        public void GunsAreDistinctAndOnlyAPointBlankScattergunBlastKillsInOnePull() {
            const int health = 200;
            foreach (var gun in CTF_Loadouts.Weapons) {
                var s = gun.stats.Validated();
                if (gun.weapon == CTF_Weapon.Scattergun) Assert.That(s.PullDamage, Is.GreaterThanOrEqualTo(health), "every pellet on target");
                else Assert.That(s.PullDamage, Is.LessThan(health), gun.id);
            }
            var sidearm = CTF_Loadouts.Get(CTF_Weapon.Sidearm).stats;
            var scatter = CTF_Loadouts.Get(CTF_Weapon.Scattergun).stats;
            var marksman = CTF_Loadouts.Get(CTF_Weapon.Marksman).stats;
            Assert.That(scatter.range, Is.LessThan(sidearm.range));
            Assert.That(marksman.range, Is.GreaterThan(sidearm.range));
            Assert.That(scatter.WorstSpreadDegrees, Is.GreaterThan(sidearm.WorstSpreadDegrees));
            Assert.That(marksman.WorstSpreadDegrees, Is.LessThan(sidearm.WorstSpreadDegrees));
        }

        [Test]
        public void GunStatsValidateAndSteadyAimTightensUpToTheCap() {
            var broken = new CTF_GunStats { damage = 0, clipShots = 0, reloadSeconds = 0f, fireInterval = 0f, roundsPerShot = 0, worstAccuracy = 400, bestAccuracy = -5, range = 0f }.Validated();
            Assert.That(broken.damage, Is.EqualTo(1));
            Assert.That(broken.ClipRounds, Is.EqualTo(1));
            Assert.That(broken.bestAccuracy, Is.GreaterThanOrEqualTo(broken.worstAccuracy));
            Assert.That(broken.range, Is.GreaterThanOrEqualTo(1f));
            var aimed = CTF_Loadouts.Get(CTF_Weapon.Marksman).stats.WithAccuracyBonus(25);
            Assert.That(aimed.bestAccuracy, Is.EqualTo(360));
            Assert.That(CTF_Loadouts.Get(CTF_Weapon.BurstRifle).stats.ClipRounds, Is.EqualTo(6));
        }

        // ---------------------------------------------------------------- level track and challenges
        [Test]
        public void RetentionRulesBuildWithEnoughUnlocksAndChallenges() {
            var rules = CTF_Retention.Rules();
            Assert.That(rules.UniverseId, Is.EqualTo("capture_the_flag"));
            Assert.That(rules.Track.Unlocks.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(rules.ChallengePool.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(CTF_Retention.Challenges.Select(c => c.id).Distinct().Count(), Is.EqualTo(CTF_Retention.Challenges.Length));
            // Each unlock is a gun or a perk the loadout card can offer, except the late cosmetics and the title at the cap.
            var items = CTF_Loadouts.WeaponUnlockIds().Concat(CTF_Loadouts.PerkUnlockIds()).Where(id => id != null).ToList();
            var cosmetic = new[] { CTF_Retention.UnlockFlagLegend, CTF_Retention.UnlockFlagTrail, CTF_Retention.UnlockGoldTracers };
            foreach (var unlock in CTF_Retention.Unlocks.Where(u => !cosmetic.Contains(u.id))) Assert.That(items, Does.Contain(unlock.id));
            Assert.That(rules.Track.IsUnlocked(2, CTF_Retention.UnlockSecondWind), Is.True);
            Assert.That(rules.Track.IsUnlocked(12, CTF_Retention.UnlockMarksman), Is.False);
            Assert.That(rules.Track.IsUnlocked(13, CTF_Retention.UnlockMarksman), Is.True);
            // Try 1 ended the track at level 20 on day 3: the last unlocks now sit past level 20.
            Assert.That(CTF_Retention.LevelOf(CTF_Retention.UnlockFlagTrail), Is.GreaterThan(20));
            Assert.That(CTF_Retention.LevelOf(CTF_Retention.UnlockFlagLegend), Is.EqualTo(CTF_Retention.MaxLevel));
        }

        [Test]
        public void TheLockedKeyNamesTheNextUnlock() {
            byte none = CTF_Loadouts.OwnedWeapons(_ => false);
            Assert.That(CTF_Retention.NextLocked(none, CTF_Loadouts.WeaponUnlockIds(), out int index, out int level), Is.True);
            Assert.That((CTF_Weapon)index, Is.EqualTo(CTF_Weapon.BurstRifle));
            Assert.That(level, Is.EqualTo(CTF_Retention.LevelOf(CTF_Retention.UnlockBurstRifle)));
            byte all = CTF_Loadouts.OwnedWeapons(_ => true);
            Assert.That(CTF_Retention.NextLocked(all, CTF_Loadouts.WeaponUnlockIds(), out _, out _), Is.False);
        }

        // ---------------------------------------------------------------- medals
        [Test]
        public void QuickKillsChainIntoMultiKillsButNotAfterTheWindow() {
            var tuning = new CTF_MedalTuning();
            var tracker = new CTF_MedalTracker();
            var medals = new List<CTF_Medal>();
            Assert.That(tracker.Kill(0f, default, tuning, medals), Is.False);
            Assert.That(tracker.Kill(1f, default, tuning, medals), Is.True);
            Assert.That(medals, Does.Contain(CTF_Medal.DoubleKill));
            medals.Clear();
            tracker.Kill(1f + tuning.multiKillWindow + 0.1f, default, tuning, medals);
            Assert.That(medals, Has.No.Member(CTF_Medal.TripleKill));
            // That was the third kill of the life: a killing spree.
            Assert.That(medals, Does.Contain(CTF_Medal.KillingSpree));
            Assert.That(tracker.Streak, Is.EqualTo(3));
        }

        [Test]
        public void DeathEndsTheStreakButKeepsTheMatchBest() {
            var tracker = new CTF_MedalTracker();
            var medals = new List<CTF_Medal>();
            for (int i = 0; i < 4; i++) tracker.Kill(i * 10f, default, null, medals);
            tracker.Died();
            Assert.That(tracker.Streak, Is.Zero);
            Assert.That(tracker.BestStreak, Is.EqualTo(4));
            tracker.Reset();
            Assert.That(tracker.BestStreak, Is.Zero);
        }

        [Test]
        public void FlagContextEarnsTheFlagMedals() {
            var tracker = new CTF_MedalTracker();
            var medals = new List<CTF_Medal>();
            tracker.Kill(0f, new CTF_KillContext { firstOfMatch = true, victimCarriedOurFlag = true, nearOwnStand = true, revenge = true }, null, medals);
            Assert.That(medals, Does.Contain(CTF_Medal.FirstBlood));
            Assert.That(medals, Does.Contain(CTF_Medal.CarrierDown));
            // Stopping the carrier is the defence medal; the plain Defender is not paid twice for one kill.
            Assert.That(medals, Has.No.Member(CTF_Medal.Defender));
            Assert.That(medals, Does.Contain(CTF_Medal.Payback));
            Assert.That(CTF_MedalTracker.Xp(CTF_Medal.Mvp, new CTF_MedalTuning { xpMultiplier = 2f }), Is.EqualTo(2 * CTF_MedalTracker.Get(CTF_Medal.Mvp).xp));
            foreach (CTF_Medal medal in System.Enum.GetValues(typeof(CTF_Medal))) Assert.That(CTF_MedalTracker.Get(medal).medal, Is.EqualTo(medal));
        }

        // ---------------------------------------------------------------- bots
        [Test]
        public void BotsFillEachTeamToTheTeamSize() {
            Assert.That(CTF_BotRules.BotsFor(1, 4, true), Is.EqualTo(3));
            Assert.That(CTF_BotRules.BotsFor(0, 4, true), Is.EqualTo(4));
            Assert.That(CTF_BotRules.BotsFor(5, 4, true), Is.Zero);
            Assert.That(CTF_BotRules.BotsFor(1, 4, false), Is.Zero);
        }

        [Test]
        public void LevelCapsTheBotTier() {
            int[] levels = { 1, 4, 9, 15 };
            Assert.That(CTF_BotRules.SkillFor(1f, levels, 0), Is.EqualTo(CTF_BotSkill.Recruit));
            Assert.That(CTF_BotRules.SkillFor(5f, levels, 0), Is.EqualTo(CTF_BotSkill.Regular));
            Assert.That(CTF_BotRules.SkillFor(30f, levels, 1), Is.EqualTo(CTF_BotSkill.Elite));
            Assert.That(CTF_BotRules.SkillFor(1f, levels, -1), Is.EqualTo(CTF_BotSkill.Recruit));
            Assert.That(CTF_BotRules.Ceiling(3f, levels), Is.EqualTo(0));
            Assert.That(CTF_BotRules.Ceiling(9f, levels), Is.EqualTo(2));
        }

        [Test]
        public void ATeamOfFourGetsAttackersAndADefender() {
            var roles = Enumerable.Range(0, 3).Select(i => CTF_BotRules.RoleFor(i, 3, 1)).ToList();
            Assert.That(roles.Count(r => r == CTF_BotRole.Defender), Is.EqualTo(1));
            Assert.That(roles.Count(r => r == CTF_BotRole.Attacker), Is.EqualTo(2));
            Assert.That(CTF_BotRules.RoleFor(0, 1, 1), Is.EqualTo(CTF_BotRole.Attacker));
            var five = Enumerable.Range(0, 5).Select(i => CTF_BotRules.RoleFor(i, 5, 0)).ToList();
            Assert.That(five, Does.Contain(CTF_BotRole.Midfielder));
        }

        [Test]
        public void BotGoalsPutTheFlagsFirst() {
            var s = new CTF_BotSituation { role = CTF_BotRole.Attacker, ourFlag = CTF_FlagState.Home, theirFlag = CTF_FlagState.Home };
            Assert.That(CTF_BotRules.GoalFor(s, 2, 1), Is.EqualTo(CTF_BotGoal.TakeFlag));
            s.carrying = true;
            Assert.That(CTF_BotRules.GoalFor(s, 2, 1), Is.EqualTo(CTF_BotGoal.CarryHome));
            s.carrying = false;
            s.ourFlag = CTF_FlagState.Carried;
            s.rankToOurFlag = 0;
            Assert.That(CTF_BotRules.GoalFor(s, 2, 1), Is.EqualTo(CTF_BotGoal.ChaseCarrier));
            s.rankToOurFlag = 5;
            Assert.That(CTF_BotRules.GoalFor(s, 2, 1), Is.EqualTo(CTF_BotGoal.TakeFlag));
            s.ourFlag = CTF_FlagState.Dropped;
            s.role = CTF_BotRole.Defender;
            Assert.That(CTF_BotRules.GoalFor(s, 2, 1), Is.EqualTo(CTF_BotGoal.ReturnFlag));
            s.ourFlag = CTF_FlagState.Home;
            Assert.That(CTF_BotRules.GoalFor(s, 2, 1), Is.EqualTo(CTF_BotGoal.Defend));
            s.role = CTF_BotRole.Attacker;
            s.theirFlag = CTF_FlagState.Carried;
            Assert.That(CTF_BotRules.GoalFor(s, 2, 1), Is.EqualTo(CTF_BotGoal.Escort));
            s.wantsHealth = true;
            Assert.That(CTF_BotRules.GoalFor(s, 2, 1), Is.EqualTo(CTF_BotGoal.Heal));
        }

        [Test]
        public void BotsMissMoreAgainstCrossingTargetsAndAtRange() {
            var tier = CTF_BotRules.DefaultTiers()[(int)CTF_BotSkill.Regular];
            float still = CTF_BotRules.AimErrorDegrees(tier, 0f, 21f);
            float sprinting = CTF_BotRules.AimErrorDegrees(tier, 21f, 21f);
            Assert.That(sprinting, Is.EqualTo(still + tier.movingTargetErrorDegrees).Within(1e-4f));
            Assert.That(CTF_BotRules.AimErrorDegrees(tier, 100f, 21f), Is.EqualTo(sprinting).Within(1e-4f));
            Assert.That(CTF_BotRules.MissRadius(still, 40f), Is.GreaterThan(CTF_BotRules.MissRadius(still, 10f)));
            var tiers = CTF_BotRules.DefaultTiers();
            for (int i = 1; i < tiers.Length; i++) {
                Assert.That(tiers[i].aimErrorDegrees, Is.LessThan(tiers[i - 1].aimErrorDegrees));
                Assert.That(tiers[i].reactionSeconds, Is.LessThan(tiers[i - 1].reactionSeconds));
            }
            Assert.That(CTF_BotRules.WeaponFor(tiers[0], 7), Is.EqualTo(CTF_Weapon.Sidearm));
            Assert.That(CTF_BotRules.NameFor(0, "{0} [BOT]"), Does.EndWith("[BOT]"));
        }

        [Test]
        public void BotActorIdsNeverMeetPlayerIds() {
            Assert.That(CTF_Rules.IsBotId(CTF_Rules.BotId(0)), Is.True);
            Assert.That(CTF_Rules.IsBotId(0), Is.False);
            Assert.That(CTF_Rules.IsBotId(-1), Is.False);
            Assert.That(CTF_Rules.BotId(3), Is.Not.EqualTo(CTF_Rules.BotId(4)));
        }

        // ---------------------------------------------------------------- speed and MVP
        [Test]
        public void PerksStackWithCarryingAndTheBoost() {
            Assert.That(CTF_Rules.SpeedMultiplier(true, 1f, false, 1.35f, 1f), Is.EqualTo(1f));
            Assert.That(CTF_Rules.SpeedMultiplier(true, CTF_Rules.CarrierSpeedMultiplier, true, 1.5f, 1.1f),
                Is.EqualTo(CTF_Rules.CarrierSpeedMultiplier * 1.5f * 1.1f).Within(1e-5f));
            Assert.That(CTF_Rules.SpeedMultiplier(false, 0.5f, false, 2f, 1f), Is.EqualTo(1f));
        }

        [Test]
        public void TheMvpIsTheBiggestContributorWithTiesToFewerDeaths() {
            var w = new CTF_MvpWeights();
            // Equal scores (8): fewer deaths wins.
            var stats = new List<CTF_MatchStats> {
                new() { kills = 6, deaths = 2 },
                new() { kills = 5, assists = 2, deaths = 1 },
            };
            Assert.That(CTF_Scoring.Mvp(stats, new[] { true, false }, w), Is.EqualTo(1));
            // Equal scores and deaths: the winning team's.
            var even = new List<CTF_MatchStats> { new() { kills = 4 }, new() { kills = 4 } };
            Assert.That(CTF_Scoring.Mvp(even, new[] { false, true }, w), Is.EqualTo(1));
            // Two captures beat a pile of eliminations.
            stats.Add(new CTF_MatchStats { captures = 2, flagTakes = 2, deaths = 3 });
            Assert.That(CTF_Scoring.Mvp(stats, null, w), Is.EqualTo(2));
            var quiet = new List<CTF_MatchStats> { new() { kills = 1 }, new() { deaths = 3 } };
            Assert.That(CTF_Scoring.Mvp(quiet, null, w), Is.EqualTo(-1));
        }
    }
}
