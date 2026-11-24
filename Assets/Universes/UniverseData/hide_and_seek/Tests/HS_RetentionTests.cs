using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RyanAssets.Retention;

namespace Universes.UniverseData.hide_and_seek.Tests {
    public sealed class HS_RetentionTests {
        static RetentionRules Rules() => HS_Retention.Rules(new RetentionTuning());

        [Test]
        public void RulesBuildFromTheTables() => Assert.DoesNotThrow(() => Rules());

        [Test]
        public void PoolIsBigEnoughAndUsesOnlyReportedStats() {
            Assert.That(HS_Retention.Challenges.Length, Is.GreaterThanOrEqualTo(8));
            foreach (ChallengeDefinition challenge in HS_Retention.Challenges) {
                Assert.That(HS_Retention.Stats.All, Does.Contain(challenge.statKey), challenge.id);
                Assert.That(challenge.xpReward, Is.InRange(50, 200), challenge.id);
                Assert.That(challenge.Describe(), Does.Not.Contain(ChallengeDefinition.TargetToken), challenge.id);
            }
            int distinctStats = HS_Retention.Challenges.Select(c => c.statKey).Distinct().Count();
            Assert.That(distinctStats, Is.GreaterThanOrEqualTo(new RetentionTuning().dailyChallengeCount * 2), "enough variety for different days");
        }

        [Test]
        public void EveryDayPicksThreeDifferentStatsAndDaysDiffer() {
            RetentionRules rules = Rules();
            int start = RetentionDay.Of(new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc));
            var sets = new HashSet<string>();
            for (int day = start; day < start + 60; day++) {
                IReadOnlyList<ChallengeDefinition> picks = rules.ChallengesFor(day);
                Assert.That(picks.Count, Is.EqualTo(rules.Tuning.dailyChallengeCount));
                Assert.That(picks.Select(p => p.statKey).Distinct().Count(), Is.EqualTo(picks.Count), $"day {day}");
                sets.Add(string.Join(",", picks.Select(p => p.id).OrderBy(id => id)));
            }
            Assert.That(sets.Count, Is.GreaterThan(20), "tomorrow should usually differ from today");
        }

        [Test]
        public void UnlockTableIsOrderedSpacedAndWithinTheTrack() {
            RetentionRules rules = Rules();
            Assert.That(HS_Retention.Unlocks.Length, Is.GreaterThanOrEqualTo(8));
            Assert.That(HS_Retention.Unlocks.Select(u => u.id).Distinct().Count(), Is.EqualTo(HS_Retention.Unlocks.Length));
            foreach (UnlockDefinition unlock in HS_Retention.Unlocks) {
                Assert.That(unlock.level, Is.InRange(1, rules.Track.MaxLevel), unlock.id);
                Assert.That(HS_Retention.UnlockLevel(unlock.id), Is.EqualTo(unlock.level));
            }
            // Past level 1 a new unlock comes at least every three levels up to the last one.
            int[] levels = HS_Retention.Unlocks.Select(u => u.level).Where(l => l > 1).OrderBy(l => l).ToArray();
            for (int i = 1; i < levels.Length; i++) Assert.That(levels[i] - levels[i - 1], Is.LessThanOrEqualTo(3));
            Assert.That(HS_Retention.UnlockLevel("nope"), Is.Zero);
            Assert.That(HS_Retention.UnlockLevel(null), Is.Zero);
        }

        [Test]
        public void TheKitCountsEveryStatTheGameReports() {
            RetentionRules rules = Rules();
            foreach (ChallengeDefinition challenge in HS_Retention.Challenges) Assert.That(rules.UsesStat(challenge.statKey), Is.True, challenge.statKey);
        }

        [Test]
        public void TheTrackEndsAtTheLastUnlock() {
            RetentionRules rules = Rules();
            int last = HS_Retention.Unlocks.Max(u => u.level);
            Assert.That(rules.Track.MaxLevel, Is.EqualTo(last), "no dead levels past the last unlock");
            Assert.That(last, Is.EqualTo(HS_Retention.MaxLevel));
            var generous = new RetentionTuning();
            generous.levels.maxLevel = 50;
            Assert.That(HS_Retention.Rules(generous).Track.MaxLevel, Is.EqualTo(HS_Retention.MaxLevel), "a bigger cap in the inspector is held at the track's end");
            Assert.That(generous.levels.maxLevel, Is.EqualTo(50), "the caller's tuning is not changed");
            // Past the abilities and their upgrades, unlocks change play or show off (not only numbers).
            foreach (string id in new[] { HS_Retention.UnlockIds.DecoyPair, HS_Retention.UnlockIds.TauntMaster, HS_Retention.UnlockIds.GhostTrail, HS_Retention.UnlockIds.ManorLegend })
                Assert.That(HS_Retention.UnlockLevel(id), Is.GreaterThan(HS_Retention.UnlockLevel(HS_Retention.UnlockIds.QuickRecovery)), id);
        }

        /// <summary>
        /// Pacing with the default tuning and the kit's default curve (100 XP, +25 a level), day by day:
        /// <list type="bullet">
        /// <item>8 rounds a day (about 27 minutes), as hider and seeker in turn: a hider who survives with a
        /// close call, a hider found halfway, a seeker whose side found everyone, a seeker who found nobody;
        /// the MVP bonus every other round;</item>
        /// <item>two of the day's three challenges done (after rounds 4 and 8), at the pool's average reward;</item>
        /// <item>the daily streak bonus before the day's first round.</item>
        /// </list>
        /// Live, the first natural rounds paid 15, 100, 210 and 280 XP with challenges (phase B, try 1). The
        /// test prints the day and round each unlock lands on, and holds: the second keys on day one, Blend
        /// In on day two, the Flashlight on day three, the rest spread over weeks.
        /// </summary>
        [Test]
        public void PacingSpreadsTheUnlocksOverManyRounds() {
            var score = new HS_ScoreTuning();
            score.Validate();
            var tuning = new RetentionTuning();
            LevelTrack track = Rules().Track;
            const int roundsPerDay = 8;
            int hiderSurvived = score.Xp(score.SurvivalPointsFor(150f) + score.closeCallPoints + score.survivedPoints) + score.playedXp;
            int hiderFound = score.Xp(score.SurvivalPointsFor(75f)) + score.playedXp;
            int seekerWon = score.Xp(score.tagPoints + score.firstFindPoints + score.seekerWinPoints) + score.playedXp;
            int seekerLost = score.playedXp;
            int perRound = (hiderSurvived + hiderFound + seekerWon + seekerLost) / 4 + score.mvpXp / 2;
            int challenge = (int)HS_Retention.Challenges.Average(c => c.xpReward);
            Assert.That(seekerLost, Is.GreaterThan(0), "a round never ends at +0 XP");
            Assert.That(hiderSurvived, Is.InRange(120, 220));
            // The test log (TestContext.Out; the console when run outside NUnit's context).
            System.IO.TextWriter log = TestContext.Out ?? Console.Out;
            log.WriteLine($"Per round: survived {hiderSurvived}, found {hiderFound}, seeker won {seekerWon}, seeker lost {seekerLost}; " +
                                  $"average with MVP {perRound}. Challenges {challenge} each (2 a day), streak {tuning.streak.BonusXp(1)}+{tuning.streak.xpPerExtraDay}/day.");

            var landed = new Dictionary<string, (int day, int round, long xp)>();
            long xp = 0;
            for (int day = 1; day <= 60 && landed.Count < HS_Retention.Unlocks.Length; day++) {
                xp += tuning.streak.BonusXp(day);
                for (int round = 1; round <= roundsPerDay; round++) {
                    xp += perRound;
                    if (round == roundsPerDay / 2 || round == roundsPerDay) xp += challenge;
                    foreach (UnlockDefinition unlock in HS_Retention.Unlocks)
                        if (!landed.ContainsKey(unlock.id) && xp >= track.XpForLevel(unlock.level)) landed[unlock.id] = (day, round, xp);
                }
            }
            foreach (UnlockDefinition unlock in HS_Retention.Unlocks.OrderBy(u => u.level))
                log.WriteLine(landed.TryGetValue(unlock.id, out var at)
                    ? $"Lv {unlock.level,2} {unlock.title,-15} {track.XpForLevel(unlock.level),6} XP: day {at.day}, round {at.round} (round {(at.day - 1) * roundsPerDay + at.round} overall)"
                    : $"Lv {unlock.level,2} {unlock.title,-15} not within 60 days");
            int Day(string id) => landed.TryGetValue(id, out var at) ? at.day : int.MaxValue;
            Assert.That(Day(HS_Retention.UnlockIds.Decoy), Is.EqualTo(1));
            Assert.That(Day(HS_Retention.UnlockIds.Radar), Is.EqualTo(1));
            Assert.That(Day(HS_Retention.UnlockIds.Blend), Is.EqualTo(2), "a new key on day two");
            Assert.That(Day(HS_Retention.UnlockIds.Flashlight), Is.EqualTo(3), "and another on day three");
            Assert.That(Day(HS_Retention.UnlockIds.QuickRecovery), Is.InRange(5, 14));
            Assert.That(Day(HS_Retention.UnlockIds.ManorLegend), Is.InRange(9, 30), "the cap is weeks away, not months");
        }
    }
}
