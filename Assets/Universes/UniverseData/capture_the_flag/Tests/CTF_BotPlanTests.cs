using NUnit.Framework;

namespace Universes.UniverseData.capture_the_flag.Tests {
    /// <summary>Try 2: the bots' tier moves with results, new players get help, and the solo side is random.</summary>
    public sealed class CTF_BotPlanTests {
        static readonly CTF_BotPlanTuning Tuning = new() {
            skillLevels = new[] { 1, 4, 9, 15 }, assistBelowLevel = 6, assistAfterLosses = 2, newcomerEnemyShort = 1,
        };

        static CTF_TeamStanding Solo(float level, int tier, int losses = 0, bool lostLast = false, bool newcomer = false) => new() {
            humans = 1, level = level, tier = tier, lossStreak = losses, lostLast = lostLast, newcomer = newcomer,
        };

        [Test]
        public void TierStartsAtRecruitAndMovesOnlyOnClearResults() {
            Assert.That(CTF_BotRules.NextTier(0, 3, 2, 3), Is.EqualTo(1), "a clear win steps up");
            Assert.That(CTF_BotRules.NextTier(1, 1, 2, 3), Is.EqualTo(1), "a close win holds");
            Assert.That(CTF_BotRules.NextTier(2, -2, 2, 3), Is.EqualTo(1), "a clear loss steps down");
            Assert.That(CTF_BotRules.NextTier(0, -3, 2, 3), Is.EqualTo(0), "never below Recruit");
            Assert.That(CTF_BotRules.NextTier(1, 3, 2, 1), Is.EqualTo(1), "level caps it");
            Assert.That(CTF_BotRules.NextTier(3, 0, 2, 1), Is.EqualTo(1), "a lower ceiling pulls a stored tier down");
            Assert.That(new CTF_TeamStanding().tier, Is.EqualTo(0), "a new save plays Recruit");
        }

        [Test]
        public void LossStreakCountsLossesAndDrawsHoldIt() {
            Assert.That(CTF_BotRules.NextLossStreak(0, -1), Is.EqualTo(1));
            Assert.That(CTF_BotRules.NextLossStreak(1, -3), Is.EqualTo(2));
            Assert.That(CTF_BotRules.NextLossStreak(2, 0), Is.EqualTo(2));
            Assert.That(CTF_BotRules.NextLossStreak(2, 1), Is.Zero);
        }

        [Test]
        public void NewPlayerGetsStrongerTeammatesAndAShortEnemy() {
            var plan = CTF_BotRules.Plan(Solo(1f, 0, newcomer: true), default, Tuning, 0);
            Assert.That(plan.red, Is.EqualTo(CTF_BotSkill.Regular), "the bots beside a new player play a tier up");
            Assert.That(plan.blue, Is.EqualTo(CTF_BotSkill.Recruit));
            Assert.That(plan.blueShort, Is.EqualTo(1), "until the first win the enemy is a bot short");
            Assert.That(plan.redShort, Is.Zero);
            Assert.That(CTF_BotRules.BotsFor(0, 4, true, plan.blueShort), Is.EqualTo(3));
            Assert.That(CTF_BotRules.BotsFor(1, 4, true, plan.redShort), Is.EqualTo(3));
        }

        [Test]
        public void HelpEndsWithLevelAndComesBackAfterTwoLosses() {
            var plan = CTF_BotRules.Plan(default, Solo(8f, 1), Tuning, 0);
            Assert.That(plan.blue, Is.EqualTo(plan.red), "a level-8 winner plays even bots");
            Assert.That(plan.red, Is.EqualTo(CTF_BotSkill.Regular));
            Assert.That(plan.redShort + plan.blueShort, Is.Zero);
            plan = CTF_BotRules.Plan(default, Solo(8f, 1, losses: 2, lostLast: true), Tuning, 0);
            Assert.That(plan.blue, Is.EqualTo(CTF_BotSkill.Veteran), "two losses in a row: help again");
            Assert.That(plan.red, Is.EqualTo(CTF_BotSkill.Regular));
        }

        [Test]
        public void ToughBotsSkipsAPlayerWhoJustLost() {
            Assert.That(CTF_BotRules.Plan(Solo(10f, 2), default, Tuning, 1).blue, Is.EqualTo(CTF_BotSkill.Elite));
            Assert.That(CTF_BotRules.Plan(Solo(10f, 2, losses: 1, lostLast: true), default, Tuning, 1).blue, Is.EqualTo(CTF_BotSkill.Veteran));
            Assert.That(CTF_Daily.BotTierBonus(CTF_DailyTwist.ToughBots, 1), Is.EqualTo(1));
        }

        [Test]
        public void TierIsTheLowestEarnedAndCappedByLevel() {
            // A stored Elite at level 5 plays Regular (the level's ceiling).
            Assert.That(CTF_BotRules.Plan(Solo(5f, 3), default, Tuning, 0).blue, Is.EqualTo(CTF_BotSkill.Regular));
            // Players on both sides: the weaker one's tier, and nobody gets help when both qualify.
            var plan = CTF_BotRules.Plan(Solo(2f, 0, newcomer: true), Solo(3f, 0, newcomer: true), Tuning, 0);
            Assert.That(plan.red, Is.EqualTo(plan.blue));
            Assert.That(plan.redShort + plan.blueShort, Is.Zero);
            // Nobody playing: Recruit.
            Assert.That(CTF_BotRules.Plan(default, default, Tuning, 1).baseSkill, Is.EqualTo(CTF_BotSkill.Recruit));
        }

        [Test]
        public void SoloSideIsEitherColourAndTiesUseIt() {
            Assert.That(CTF_BotRules.FirstSide(0), Is.EqualTo(CTF_Team.Red));
            Assert.That(CTF_BotRules.FirstSide(7), Is.EqualTo(CTF_Team.Blue));
            Assert.That(CTF_Rules.PickTeam(0, 0, 0, 0, CTF_Team.Blue), Is.EqualTo(CTF_Team.Blue));
            Assert.That(CTF_Rules.PickTeam(1, 0, 0, 0, CTF_Team.Red), Is.EqualTo(CTF_Team.Blue), "numbers still come first");
        }

        [Test]
        public void ChipSaysWhenTheSidesDiffer() {
            Assert.That(CTF_BotRules.ChipText(4, CTF_BotSkill.Regular, CTF_BotSkill.Regular, 0, 0), Is.EqualTo("4v4 · Regular bots"));
            Assert.That(CTF_BotRules.ChipText(4, CTF_BotSkill.Regular, CTF_BotSkill.Recruit, 0, 1), Is.EqualTo("4v3 · Recruit bots · Regular with you"));
        }
    }
}
