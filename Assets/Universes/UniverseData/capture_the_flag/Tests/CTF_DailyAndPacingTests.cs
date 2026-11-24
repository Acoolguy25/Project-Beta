using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RyanAssets.Retention;

namespace Universes.UniverseData.capture_the_flag.Tests {
    /// <summary>The daily twist, the late-join win rule, the full level track and the pacing it gives.</summary>
    public sealed class CTF_DailyAndPacingTests {
        // ---------------------------------------------------------------- daily twist
        [Test]
        public void TheTwistIsTheSameAllDayAndNeverRepeatsOnTheNextDay() {
            var morning = new DateTime(2026, 11, 24, 0, 0, 1, DateTimeKind.Utc);
            var night = new DateTime(2026, 11, 24, 23, 59, 59, DateTimeKind.Utc);
            Assert.That(CTF_Daily.Today(night), Is.EqualTo(CTF_Daily.Today(morning)));
            for (int day = -30; day < 400; day++)
                Assert.That(CTF_Daily.Featured(day + 1), Is.Not.EqualTo(CTF_Daily.Featured(day)), $"day {day} and the next play the same twist");
        }

        [Test]
        public void EverySixDaysPlayEveryTwistOnce() {
            for (int block = 0; block < 20; block++) {
                var days = Enumerable.Range(block * 6, 6).Select(CTF_Daily.Featured).ToList();
                Assert.That(days.Distinct().Count(), Is.EqualTo(6), $"block {block}: {string.Join(", ", days)}");
                Assert.That(days, Has.No.Member(CTF_DailyTwist.None));
            }
        }

        [Test]
        public void ALoanDayLendsOnlyItsGunAndPaysForItsKills() {
            byte sidearmOnly = CTF_Loadouts.OwnedWeapons(_ => false);
            byte loaned = CTF_Daily.WithLoan(sidearmOnly, CTF_DailyTwist.MarksmanDay);
            Assert.That(CTF_Loadouts.Has(loaned, (int)CTF_Weapon.Marksman), Is.True);
            Assert.That(CTF_Loadouts.CountOwned(loaned, CTF_Loadouts.Weapons.Length), Is.EqualTo(2));
            Assert.That(CTF_Daily.WithLoan(sidearmOnly, CTF_DailyTwist.FlagFrenzy), Is.EqualTo(sidearmOnly));
            Assert.That(CTF_Daily.KillXp(10, CTF_Weapon.Marksman, CTF_DailyTwist.MarksmanDay, 2), Is.EqualTo(20));
            Assert.That(CTF_Daily.KillXp(10, CTF_Weapon.Sidearm, CTF_DailyTwist.MarksmanDay, 2), Is.EqualTo(10));
            Assert.That(CTF_Daily.KillXp(10, CTF_Weapon.Marksman, CTF_DailyTwist.ToughBots, 2), Is.EqualTo(10));
        }

        [Test]
        public void EachTwistChangesOnlyItsOwnRule() {
            Assert.That(CTF_Daily.FlagXp(50, CTF_DailyTwist.FlagFrenzy, 2), Is.EqualTo(100));
            Assert.That(CTF_Daily.FlagXp(50, CTF_DailyTwist.PowerSurge, 2), Is.EqualTo(50));
            Assert.That(CTF_Daily.PowerUpRespawn(20f, CTF_DailyTwist.PowerSurge, 0.5f), Is.EqualTo(10f));
            Assert.That(CTF_Daily.PowerUpRespawn(20f, CTF_DailyTwist.FlagFrenzy, 0.5f), Is.EqualTo(20f));
            Assert.That(CTF_Daily.BotTierBonus(CTF_DailyTwist.ToughBots, 1), Is.EqualTo(1));
            Assert.That(CTF_Daily.BotTierBonus(CTF_DailyTwist.ScattergunDay, 1), Is.EqualTo(0));
            Assert.That(CTF_Daily.ResultBonusXp(100, CTF_DailyTwist.ToughBots, 0.25f), Is.EqualTo(25));
            Assert.That(CTF_Daily.ResultBonusXp(100, CTF_DailyTwist.PowerSurge, 0.25f), Is.EqualTo(0));
            foreach (CTF_DailyTwist twist in Enum.GetValues(typeof(CTF_DailyTwist))) {
                string text = CTF_Daily.Describe(twist, 2, 0.25f);
                if (twist == CTF_DailyTwist.None) Assert.That(text, Is.Empty);
                else Assert.That(text, Does.StartWith("Today: " + CTF_Daily.Title(twist)));
            }
        }

        // ---------------------------------------------------------------- late join
        [Test]
        public void AWinCountsForEarlyJoinersAndForLateOnesWhoMadeAFlagPlay() {
            Assert.That(CTF_Rules.WinCounts(0f, false, 0.5f), Is.True, "dealt in at the start");
            Assert.That(CTF_Rules.WinCounts(0.5f, false, 0.5f), Is.True, "joined exactly at the cut-off");
            Assert.That(CTF_Rules.WinCounts(0.9f, false, 0.5f), Is.False, "joined in the last minutes and did nothing");
            Assert.That(CTF_Rules.WinCounts(0.9f, true, 0.5f), Is.True, "joined late but grabbed or returned a flag");
            Assert.That(CTF_Rules.ShareGone(100f, 580f, 340f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(CTF_Rules.ShareGone(100f, 580f, 50f), Is.EqualTo(0f));
            Assert.That(CTF_Rules.ShareGone(100f, 580f, 900f), Is.EqualTo(1f));
            Assert.That(CTF_Rules.ShareGone(0f, 0f, 5f), Is.EqualTo(0f), "no match yet");
        }

        // ---------------------------------------------------------------- bots
        [Test]
        public void BotsWithAFlagToPlayForShootOnTheMoveAndHoldersStopToFight() {
            Assert.That(CTF_BotRules.ShootsOnTheMove(CTF_BotGoal.TakeFlag), Is.True, "attackers push past a fight (try-1 shakedown: a stand-off in the middle, no grab in 3 minutes)");
            Assert.That(CTF_BotRules.ShootsOnTheMove(CTF_BotGoal.CarryHome), Is.True);
            Assert.That(CTF_BotRules.ShootsOnTheMove(CTF_BotGoal.ReturnFlag), Is.True);
            Assert.That(CTF_BotRules.ShootsOnTheMove(CTF_BotGoal.ChaseCarrier), Is.True);
            Assert.That(CTF_BotRules.ShootsOnTheMove(CTF_BotGoal.Escort), Is.True);
            Assert.That(CTF_BotRules.ShootsOnTheMove(CTF_BotGoal.Defend), Is.False);
            Assert.That(CTF_BotRules.ShootsOnTheMove(CTF_BotGoal.HoldMiddle), Is.False);
        }

        // ---------------------------------------------------------------- track
        [Test]
        public void TheTrackUnlocksSomethingEveryFewLevelsUpToTheCap() {
            var levels = CTF_Retention.Unlocks.Select(u => u.level).ToList();
            Assert.That(levels, Is.Ordered);
            Assert.That(levels.Last(), Is.EqualTo(CTF_Retention.MaxLevel), "the cap is the last unlock: no dead levels at the top");
            int previous = 1;
            foreach (int level in levels) {
                Assert.That(level - previous, Is.LessThanOrEqualTo(3), $"a gap of more than three levels before level {level}");
                previous = level;
            }
            var rules = CTF_Retention.Rules(new RetentionTuning());
            Assert.That(rules.Track.MaxLevel, Is.EqualTo(CTF_Retention.MaxLevel), "the default 50-level curve is held at the last unlock");
        }

        [Test]
        public void EveryGunAndPerkHasAnUnlockOnTheTrackAndPerksFitTheOwnedMask() {
            Assert.That(CTF_Loadouts.Perks.Length, Is.LessThanOrEqualTo(8), "owned perks travel as a byte");
            Assert.That(CTF_Loadouts.Weapons.Length, Is.LessThanOrEqualTo(8), "owned guns travel as a byte");
            var ids = CTF_Retention.Unlocks.Select(u => u.id).ToHashSet();
            foreach (var weapon in CTF_Loadouts.Weapons.Where(w => w.unlockId != null)) Assert.That(ids, Does.Contain(weapon.unlockId), weapon.title);
            foreach (var perk in CTF_Loadouts.Perks.Where(p => p.unlockId != null)) Assert.That(ids, Does.Contain(perk.unlockId), perk.title);
            for (int i = 0; i < CTF_Loadouts.Perks.Length; i++) Assert.That((int)CTF_Loadouts.Perks[i].perk, Is.EqualTo(i), "perk table in enum order");
        }

        [Test]
        public void LongChallengesHaveAShortCardLabel() {
            foreach (var challenge in CTF_Retention.Challenges) {
                Assert.That(challenge.CardLabel().Length, Is.LessThanOrEqualTo(26), challenge.id);
                Assert.That(challenge.CardLabel(), Does.Not.Contain("{"), challenge.id);
            }
        }

        // ---------------------------------------------------------------- pacing
        /// <summary>
        /// Match XP measured live in try 1 (the summary cards of seven fresh-save and early-level matches,
        /// try1/captures-index.txt): 210 to 435, mean 308. Try 1's model assumed about 200, so the whole
        /// track landed on day 3. The model now runs on the measured numbers.
        /// </summary>
        static readonly int[] MeasuredMatchXp = { 210, 225, 250, 300, 345, 390, 435 };
        const int MatchesPerDay = 6, ChallengesDonePerDay = 2;

        static Dictionary<string, (int day, int match)> Landing(RetentionRules rules, int matchXp, int challengeXp) {
            var landing = new Dictionary<string, (int day, int match)>();
            long xp = 0;
            int match = 0;
            for (int day = 1; day <= 60 && landing.Count < CTF_Retention.Unlocks.Length; day++) {
                for (int m = 1; m <= MatchesPerDay; m++) {
                    match++;
                    xp += matchXp;
                    // The first match of the day claims the streak; two challenges finish across the day.
                    if (m == 1) xp += rules.Tuning.streak.BonusXp(day);
                    if (m == MatchesPerDay / 2 || m == MatchesPerDay) xp += challengeXp * ChallengesDonePerDay / 2;
                    foreach (var unlock in CTF_Retention.Unlocks)
                        if (!landing.ContainsKey(unlock.id) && xp >= rules.Track.XpForLevel(unlock.level)) landing[unlock.id] = (day, match);
                }
            }
            return landing;
        }

        [Test]
        public void PacingOnTheMeasuredMatchXpSpreadsTheUnlocksOverTwoWeeks() {
            var rules = CTF_Retention.Rules(CTF_Retention.DefaultTuning());
            int mean = (int)Math.Round(MeasuredMatchXp.Average());
            int challengeXp = (int)Math.Round(CTF_Retention.Challenges.Average(c => c.xpReward));
            Console.WriteLine($"Measured match XP {string.Join(", ", MeasuredMatchXp)} (mean {mean}); {MatchesPerDay} matches a day, {ChallengesDonePerDay} challenges ({challengeXp} XP each), the streak bonus.");
            Console.WriteLine($"Curve: level 2 at {rules.Track.XpForLevel(2)} XP, level {CTF_Retention.MaxLevel} at {rules.Track.XpForLevel(CTF_Retention.MaxLevel)} XP.");
            foreach (int matchXp in new[] { MeasuredMatchXp.Min(), mean, MeasuredMatchXp.Max() }) {
                var landing = Landing(rules, matchXp, challengeXp);
                Console.WriteLine($"At {matchXp} XP a match:");
                foreach (var unlock in CTF_Retention.Unlocks) {
                    var (day, at) = landing[unlock.id];
                    Console.WriteLine($"  level {unlock.level,2} {unlock.title,-15} day {day,2}, match {at,3} ({rules.Track.XpForLevel(unlock.level)} XP)");
                }
                Assert.That(landing.Count, Is.EqualTo(CTF_Retention.Unlocks.Length), $"{matchXp}: every unlock is reached within 60 days");
            }
            var typical = Landing(rules, mean, challengeXp);
            Assert.That(typical[CTF_Retention.UnlockSecondWind].match, Is.EqualTo(1), "the first unlock comes in the first match");
            Assert.That(typical[CTF_Retention.UnlockBurstRifle].match, Is.InRange(2, 4), "the first new gun comes within the first sitting");
            Assert.That(typical[CTF_Retention.UnlockMarksman].day, Is.GreaterThanOrEqualTo(3), "the last gun waits for day three");
            Assert.That(typical[CTF_Retention.UnlockFlagLegend].day, Is.GreaterThanOrEqualTo(10), "the title at the cap takes most of two weeks");
            Assert.That(CTF_Retention.Unlocks.Select(u => typical[u.id].day).Distinct().Count(), Is.GreaterThanOrEqualTo(8), "unlocks land on at least eight different days");
            // Even the best measured match keeps the last gun off day one and the cap past a week.
            var fast = Landing(rules, MeasuredMatchXp.Max(), challengeXp);
            Assert.That(fast[CTF_Retention.UnlockMarksman].day, Is.GreaterThanOrEqualTo(2));
            Assert.That(fast[CTF_Retention.UnlockFlagLegend].day, Is.GreaterThanOrEqualTo(7));
        }
    }
}
