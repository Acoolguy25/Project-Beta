using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RyanAssets.Progress;

namespace RyanAssets.Retention.Tests {
    /// <summary>EditMode tests of the retention kit's shared rules: curve, unlocks, daily pick, streak, payouts, save repair.</summary>
    public class RetentionRulesTests {
        const string Universe = "test_game";
        /// <summary>2026-11-23 00:00 UTC.</summary>
        static readonly int Day = RetentionDay.Of(new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc));

        static readonly UnlockDefinition[] Unlocks = {
            new(5, "rifle", "Rifle", "A long gun.", "crosshair"),
            new(2, "hat", "Hat", "A hat.", "star"),
            new(5, "scope", "Scope", "See further.", "visibility"),
            new(10, "tank", "Tank", "Big.", "battle_tank"),
        };

        static ChallengeDefinition[] Pool() => new[] {
            new ChallengeDefinition("kills_10", "kills", 10, 100, 20, "Destroy {target} enemies"),
            new ChallengeDefinition("kills_50", "kills", 50, 300, 60, "Destroy {target} enemies"),
            new ChallengeDefinition("wins_1", "wins", 1, 120, 30, "Win {target} match"),
            new ChallengeDefinition("wins_3", "wins", 3, 320, 80, "Win {target} matches"),
            new ChallengeDefinition("wave_10", "wave", 10, 150, 25, "Reach wave {target}"),
            new ChallengeDefinition("builds_20", "builds", 20, 110, 20, "Build {target} towers"),
            new ChallengeDefinition("coins_500", "coins", 500, 90, 15, "Collect {target} coins"),
            new ChallengeDefinition("bosses_1", "bosses", 1, 200, 50, "Destroy {target} boss"),
        };

        static RetentionRules Rules(RetentionTuning tuning = null, string universe = Universe, IEnumerable<ChallengeDefinition> pool = null) =>
            new(universe, tuning ?? new RetentionTuning(), Unlocks, pool ?? Pool());

        static string[] Ids(IEnumerable<ChallengeDefinition> challenges) => challenges.Select(c => c.id).ToArray();

        // ------------------------------------------------------------ level curve

        [Test]
        public void CurveCostsGrowLinearlyByDefault() {
            var curve = new LevelCurve();
            Assert.AreEqual(100, curve.XpToNext(1));
            Assert.AreEqual(125, curve.XpToNext(2));
            Assert.AreEqual(325, curve.XpToNext(10));
        }

        [Test]
        public void GrowthMakesLaterLevelsDearer() {
            var linear = new LevelCurve();
            var steep = new LevelCurve { growth = 1.1f };
            Assert.AreEqual(linear.XpToNext(1), steep.XpToNext(1));
            Assert.Greater(steep.XpToNext(20), linear.XpToNext(20));
        }

        [Test]
        public void LevelForXpFindsTheLevelAtEachThreshold() {
            LevelTrack track = Rules().Track;
            Assert.AreEqual(1, track.LevelForXp(-5));
            Assert.AreEqual(1, track.LevelForXp(0));
            Assert.AreEqual(1, track.LevelForXp(99));
            Assert.AreEqual(2, track.LevelForXp(100));
            Assert.AreEqual(2, track.LevelForXp(224));
            Assert.AreEqual(3, track.LevelForXp(225));
            Assert.AreEqual(track.MaxLevel, track.LevelForXp(long.MaxValue));
            for (int level = 2; level <= track.MaxLevel; level++) {
                Assert.Greater(track.XpForLevel(level), track.XpForLevel(level - 1));
                Assert.AreEqual(level, track.LevelForXp(track.XpForLevel(level)));
                Assert.AreEqual(level - 1, track.LevelForXp(track.XpForLevel(level) - 1));
            }
        }

        [Test]
        public void ProgressReportsXpInsideTheLevelAndMaxLevel() {
            LevelTrack track = Rules().Track;
            LevelProgress mid = track.Progress(150);
            Assert.AreEqual(new LevelProgress(2, 50, 125), mid);
            Assert.AreEqual(0.4f, mid.Fraction, 1e-5f);

            LevelProgress max = track.Progress(track.MaxXp + 12345);
            Assert.AreEqual(track.MaxLevel, max.Level);
            Assert.IsTrue(max.IsMaxLevel);
            Assert.AreEqual(1f, max.Fraction);
        }

        [Test]
        public void GarbageCurveIsClamped() {
            var curve = new LevelCurve { maxLevel = -3, firstLevelXp = 0, extraXpPerLevel = -10, growth = float.NaN };
            curve.Validate();
            Assert.AreEqual(LevelCurve.MinLevels, curve.maxLevel);
            Assert.AreEqual(1, curve.firstLevelXp);
            Assert.AreEqual(0, curve.extraXpPerLevel);
            Assert.AreEqual(1f, curve.growth);
            Assert.AreEqual(2, new LevelTrack(new LevelCurve { maxLevel = 0 }, null).MaxLevel);
        }

        // ------------------------------------------------------------ unlocks

        [Test]
        public void UnlocksAreSortedByLevelKeepingTableOrder() {
            CollectionAssert.AreEqual(new[] { "hat", "rifle", "scope", "tank" }, Rules().Track.Unlocks.Select(u => u.id).ToArray());
        }

        [Test]
        public void UnlockLookup() {
            LevelTrack track = Rules().Track;
            var earned = new List<UnlockDefinition>();
            track.UnlocksBetween(1, 5, earned);
            CollectionAssert.AreEqual(new[] { "hat", "rifle", "scope" }, earned.Select(u => u.id).ToArray());
            earned.Clear();
            track.UnlocksBetween(5, 9, earned);
            Assert.IsEmpty(earned, "the lower bound is exclusive");

            Assert.AreEqual("hat", track.NextUnlock(1).id);
            Assert.AreEqual("rifle", track.NextUnlock(2).id);
            Assert.AreEqual("tank", track.NextUnlock(5).id);
            Assert.IsNull(track.NextUnlock(10));

            Assert.IsTrue(track.IsUnlocked(5, "rifle"));
            Assert.IsFalse(track.IsUnlocked(4, "rifle"));
            Assert.IsFalse(track.IsUnlocked(50, "no_such_unlock"));
            Assert.AreEqual("Scope", track.FindUnlock("scope").title);
            Assert.IsNull(track.FindUnlock(null));
        }

        [Test]
        public void BadUnlockTablesAreRefused() {
            Assert.Throws<ArgumentException>(() => new LevelTrack(new LevelCurve(), new[] { new UnlockDefinition(2, "a", "A", "", ""), new UnlockDefinition(3, "a", "A2", "", "") }));
            Assert.Throws<ArgumentException>(() => new LevelTrack(new LevelCurve(), new[] { new UnlockDefinition(0, "a", "A", "", "") }));
            Assert.Throws<ArgumentException>(() => new LevelTrack(new LevelCurve { maxLevel = 10 }, new[] { new UnlockDefinition(11, "a", "A", "", "") }));
            Assert.Throws<ArgumentException>(() => new LevelTrack(new LevelCurve(), new[] { new UnlockDefinition(2, " ", "A", "", "") }));
        }

        [Test]
        public void BadChallengePoolsAreRefused() {
            Assert.Throws<ArgumentException>(() => Rules(pool: new[] { new ChallengeDefinition("a", "k", 1, 1, 0, ""), new ChallengeDefinition("a", "k", 2, 1, 0, "") }));
            Assert.Throws<ArgumentException>(() => Rules(pool: new[] { new ChallengeDefinition("a", "k", 0, 1, 0, "") }));
            Assert.Throws<ArgumentException>(() => Rules(pool: new[] { new ChallengeDefinition("a", "", 1, 1, 0, "") }));
            Assert.Throws<ArgumentException>(() => Rules(pool: new[] { new ChallengeDefinition("a", "k", 1, -1, 0, "") }));
            Assert.Throws<ArgumentException>(() => Rules(universe: ""));
        }

        // ------------------------------------------------------------ daily pick

        [Test]
        public void DailyPickIsDeterministicAcrossServers() {
            // Two rules objects stand for two servers (or one server before and after a restart).
            for (int day = Day; day < Day + 30; day++)
                CollectionAssert.AreEqual(Ids(Rules().ChallengesFor(day)), Ids(Rules().ChallengesFor(day)));
        }

        [Test]
        public void DailyPickMatchesTheRecordedPick() {
            // Guards the algorithm: changing it reshuffles everyone's challenges mid-day.
            CollectionAssert.AreEqual(GoldenPick, Ids(Rules().ChallengesFor(Day)));
        }

        /// <summary>The pick of <see cref="Pool"/> for <see cref="Universe"/> on 2026-11-23, recorded when the algorithm was written.</summary>
        static readonly string[] GoldenPick = { "builds_20", "wins_1", "bosses_1" };

        [Test]
        public void DailyPickChangesWithTheDayAndTheUniverse() {
            RetentionRules rules = Rules();
            string first = string.Join(",", Ids(rules.ChallengesFor(Day)));
            Assert.IsTrue(Enumerable.Range(Day + 1, 10).Any(day => string.Join(",", Ids(rules.ChallengesFor(day))) != first), "the set changes from day to day");
            RetentionRules other = Rules(universe: "other_game");
            Assert.IsTrue(Enumerable.Range(Day, 10).Any(day => !Ids(rules.ChallengesFor(day)).SequenceEqual(Ids(other.ChallengesFor(day)))), "games get different sets");
        }

        [Test]
        public void DailyPickIsDistinctPrefersDifferentStatsAndOrdersByReward() {
            RetentionRules rules = Rules(new RetentionTuning { dailyChallengeCount = 4 });
            for (int day = Day; day < Day + 60; day++) {
                IReadOnlyList<ChallengeDefinition> picks = rules.ChallengesFor(day);
                Assert.AreEqual(4, picks.Count);
                Assert.AreEqual(4, picks.Select(c => c.id).Distinct().Count());
                Assert.AreEqual(4, picks.Select(c => c.statKey).Distinct().Count(), "six stat keys are available, so no key repeats");
                for (int i = 1; i < picks.Count; i++) Assert.LessOrEqual(picks[i - 1].xpReward, picks[i].xpReward);
            }
        }

        [Test]
        public void DailyPickNeverExceedsThePool() {
            var small = new[] { new ChallengeDefinition("a", "k", 1, 10, 0, "A"), new ChallengeDefinition("b", "k", 2, 20, 0, "B") };
            Assert.AreEqual(2, Rules(pool: small).ChallengesFor(Day).Count, "same stat key is allowed when nothing else is left");
            Assert.AreEqual(0, Rules(pool: Array.Empty<ChallengeDefinition>()).ChallengesFor(Day).Count);
        }

        [Test]
        public void DayBoundariesAreUtcMidnight() {
            var lastSecond = new DateTime(2026, 11, 23, 23, 59, 59, DateTimeKind.Utc);
            Assert.AreEqual(Day, RetentionDay.Of(lastSecond));
            Assert.AreEqual(Day + 1, RetentionDay.Of(lastSecond.AddSeconds(1)));
            Assert.AreEqual(1, RetentionDay.SecondsUntilReset(lastSecond));
            Assert.AreEqual(RetentionDay.SecondsPerDay, RetentionDay.SecondsUntilReset(lastSecond.AddSeconds(1)));
            Assert.AreEqual(new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc), RetentionDay.Start(Day));
        }

        [Test]
        public void RolloverGivesTheNewDaysChallengesAtZero() {
            RetentionRules rules = Rules();
            var save = new RetentionSave();
            Assert.IsTrue(rules.Refresh(save, Day));
            string key = rules.ChallengesFor(Day)[0].statKey;
            rules.Count(save, Day, key, 1, null);
            Assert.AreEqual(1, save.challenges[0].progress);

            Assert.IsFalse(rules.Refresh(save, Day), "same day: nothing changes");
            Assert.AreEqual(1, save.challenges[0].progress);

            Assert.IsTrue(rules.Refresh(save, Day + 1));
            Assert.AreEqual(Day + 1, save.challengeDay);
            CollectionAssert.AreEqual(Ids(rules.ChallengesFor(Day + 1)), save.challenges.Select(c => c.id).ToArray());
            Assert.IsTrue(save.challenges.All(c => c.progress == 0 && !c.done));
        }

        // ------------------------------------------------------------ challenges

        [Test]
        public void ChallengePaysExactlyOnce() {
            RetentionRules rules = Rules();
            var save = new RetentionSave();
            rules.Refresh(save, Day);
            ChallengeDefinition challenge = rules.ChallengesFor(Day)[0];
            var updates = new List<ChallengeUpdate>();

            rules.Count(save, Day, challenge.statKey, challenge.target - 1, updates);
            Assert.AreEqual(1, updates.Count);
            Assert.IsFalse(updates[0].Completed);

            updates.Clear();
            rules.Count(save, Day, challenge.statKey, int.MaxValue, updates);
            Assert.AreEqual(1, updates.Count);
            Assert.IsTrue(updates[0].Completed);
            Assert.AreEqual(challenge.target, updates[0].Progress, "progress stops at the target, without overflow");
            Assert.AreEqual(1, save.challengesCompleted);

            updates.Clear();
            rules.Count(save, Day, challenge.statKey, 5, updates);
            rules.Reach(save, Day, challenge.statKey, challenge.target * 2, updates);
            rules.Refresh(save, Day);
            rules.Count(save, Day, challenge.statKey, 5, updates);
            Assert.IsEmpty(updates, "a done challenge never moves or pays again");
            Assert.IsTrue(save.challenges[0].done);
            Assert.AreEqual(1, save.challengesCompleted);
        }

        [Test]
        public void ReachKeepsTheBestAndCountAdds() {
            var pool = new[] { new ChallengeDefinition("wave_10", "wave", 10, 100, 0, "Reach wave {target}"), new ChallengeDefinition("kills_10", "kills", 10, 100, 0, "") };
            RetentionRules rules = Rules(pool: pool);
            var save = new RetentionSave();
            int wave = Ids(rules.ChallengesFor(Day)).ToList().IndexOf("wave_10");
            rules.Reach(save, Day, "wave", 6, null);
            rules.Reach(save, Day, "wave", 4, null);
            Assert.AreEqual(6, save.challenges[wave].progress, "a lower value does not lower the best");
            rules.Count(save, Day, "unrelated", 50, null);
            rules.Count(save, Day, "wave", 0, null);
            rules.Count(save, Day, "wave", -3, null);
            Assert.AreEqual(6, save.challenges[wave].progress);
            Assert.AreEqual("Reach wave 10", pool[0].Describe());
        }

        // ------------------------------------------------------------ streak

        [Test]
        public void StreakGrowsOnConsecutiveDaysAndCaps() {
            var tuning = new RetentionTuning { streak = new StreakRules { firstDayXp = 20, xpPerExtraDay = 10, maxBonusDays = 7 } };
            RetentionRules rules = Rules(tuning);
            var save = new RetentionSave();
            for (int i = 0; i < 10; i++) {
                StreakClaim claim = rules.ClaimStreak(save, Day + i);
                Assert.IsTrue(claim.Paid);
                Assert.AreEqual(i + 1, claim.Days);
                Assert.AreEqual(20 + 10 * (Math.Min(i + 1, 7) - 1), claim.Xp);
            }
            Assert.AreEqual(10, save.bestStreak);
            Assert.AreEqual(10, rules.CurrentStreak(save, Day + 9));
            Assert.AreEqual(10, rules.CurrentStreak(save, Day + 10), "still alive the day after");
        }

        [Test]
        public void StreakPaysOncePerDay() {
            RetentionRules rules = Rules();
            var save = new RetentionSave();
            Assert.IsTrue(rules.ClaimStreak(save, Day).Paid);
            StreakClaim again = rules.ClaimStreak(save, Day);
            Assert.IsFalse(again.Paid);
            Assert.AreEqual(0, again.Xp);
            Assert.AreEqual(1, save.streakDays);
        }

        [Test]
        public void MissedDayRestartsTheStreak() {
            RetentionRules rules = Rules();
            var save = new RetentionSave();
            rules.ClaimStreak(save, Day);
            rules.ClaimStreak(save, Day + 1);
            rules.ClaimStreak(save, Day + 2);
            Assert.AreEqual(0, rules.CurrentStreak(save, Day + 4), "lapsed after a missed day");
            StreakClaim claim = rules.ClaimStreak(save, Day + 4);
            Assert.IsTrue(claim.Paid);
            Assert.AreEqual(1, claim.Days);
            Assert.IsTrue(claim.Restarted);
            Assert.AreEqual(rules.Tuning.streak.BonusXp(1), claim.Xp);
            Assert.AreEqual(3, save.bestStreak);
        }

        [Test]
        public void StreakDatedInTheFutureIsReset() {
            RetentionRules rules = Rules();
            var save = new RetentionSave { streakDays = 9, streakDay = Day + 100, bestStreak = 9 };
            StreakClaim claim = rules.ClaimStreak(save, Day);
            Assert.IsTrue(claim.Paid, "a clock that went back must not block today's bonus forever");
            Assert.AreEqual(1, claim.Days);
        }

        // ------------------------------------------------------------ XP and snapshot

        [Test]
        public void AddXpReportsLevelUps() {
            RetentionRules rules = Rules();
            var save = new RetentionSave();
            XpChange change = rules.AddXp(save, 230);
            Assert.AreEqual(1, change.Before.Level);
            Assert.AreEqual(3, change.After.Level);
            Assert.IsTrue(change.LeveledUp);
            Assert.IsFalse(rules.AddXp(save, -50).LeveledUp);
            Assert.AreEqual(230, save.xp);
            save.xp = RetentionSave.MaxXp - 1;
            rules.AddXp(save, int.MaxValue);
            Assert.AreEqual(RetentionSave.MaxXp, save.xp);
        }

        [Test]
        public void SnapshotDescribesTheDay() {
            RetentionRules rules = Rules();
            var save = new RetentionSave { xp = 150 };
            rules.ClaimStreak(save, Day - 1);
            DateTime noon = RetentionDay.Start(Day).AddHours(12);
            RetentionSnapshotBroadcast snapshot = rules.Snapshot(save, noon, true);
            Assert.AreEqual(2, snapshot.level);
            Assert.AreEqual(50, snapshot.levelXp);
            Assert.AreEqual(125, snapshot.levelXpNeeded);
            Assert.AreEqual(12 * 3600, snapshot.secondsToReset);
            Assert.AreEqual(3, snapshot.challenges.Length);
            Assert.IsFalse(snapshot.challenges[0].text.Contains(ChallengeDefinition.TargetToken));
            Assert.AreEqual(1, snapshot.streakDays);
            Assert.IsFalse(snapshot.streakClaimedToday);
            Assert.AreEqual(rules.Tuning.streak.BonusXp(2), snapshot.streakNextXp);
            Assert.IsTrue(snapshot.hasNextUnlock);
            Assert.AreEqual("rifle", snapshot.nextUnlock.id);
            Assert.IsFalse(snapshot.notSaved);
            Assert.IsTrue(rules.Snapshot(save, noon, false).notSaved);
        }

        // ------------------------------------------------------------ match summary

        [Test]
        public void SummaryMergesLinesAndFoldsOverflow() {
            var summary = new MatchSummary(new LevelProgress(1, 0, 100));
            summary.Add(new RewardLine("Kill", 10));
            summary.Add(new RewardLine("Kill", 10, 2));
            summary.Add(new RewardLine("Nothing", 0));
            Assert.AreEqual(1, summary.Lines.Count);
            Assert.AreEqual(2, summary.Lines[0].Count);
            Assert.AreEqual(20, summary.Lines[0].Xp);
            Assert.AreEqual(2, summary.TotalGold);

            for (int i = 0; i < MatchSummary.MaxLines + 3; i++) summary.Add(new RewardLine("Line " + i, 1, 0, 3));
            Assert.AreEqual(MatchSummary.MaxLines, summary.Lines.Count);
            RewardLine folded = summary.Lines[MatchSummary.MaxLines - 1];
            // 12 lines: Kill and the first six 1-XP lines stay, the last five fold.
            Assert.AreEqual("+5 more rewards", folded.Label);
            Assert.AreEqual(1, folded.Count, "the folded line never sums unrelated counts");
            Assert.AreEqual(5, folded.Xp);
            Assert.AreEqual("Line 5", summary.Lines[MatchSummary.MaxLines - 2].Label);
            Assert.AreEqual(20 + MatchSummary.MaxLines + 3, summary.TotalXp, "folding never loses XP");
            int shownXp = 0;
            foreach (RewardLine line in summary.Lines) shownXp += line.Xp;
            Assert.AreEqual(summary.TotalXp, shownXp, "the card's lines add up to the total");
        }

        [Test]
        public void ChallengeCardLabelPrefersTheShortText() {
            var challenge = new ChallengeDefinition("first_finds", "first_find", 2, 150, 25, "Find the first hider of a round {target} times");
            Assert.AreEqual("Find the first hider of a round 2 times", challenge.CardLabel(), "no short text: the full text");
            challenge.shortText = "First finds x{target}";
            Assert.AreEqual("First finds x2", challenge.CardLabel());
            Assert.AreEqual("Find the first hider of a round 2 times", challenge.Describe(), "the panel and toasts keep the full text");
        }

        [Test]
        public void HeadlinesShowFirstAndTheLeastValuableLinesFold() {
            var summary = new MatchSummary(new LevelProgress(1, 0, 100));
            summary.Add(RewardLine.Info("Waves", "12 / 12"));
            for (int i = 0; i < 6; i++) summary.Add(new RewardLine("Small " + i, 1 + i));
            summary.Add(new RewardLine("Kills", 50, 0, 40));
            summary.Add(new RewardLine("Boss", 30, 5));
            // Added last, as games do once the result is known.
            summary.Add(RewardLine.Headline("3-star defence", 100, 0, "3 stars"));

            IReadOnlyList<RewardLine> lines = summary.Lines;
            Assert.AreEqual(MatchSummary.MaxLines, lines.Count);
            Assert.AreEqual("3-star defence", lines[0].Label, "the headline shows first");
            Assert.IsTrue(lines[0].Pinned);
            Assert.AreEqual("3 stars", lines[0].Detail);
            // Room for six other lines: Kills, Boss and Small 2..5 stay (in the order added); the info
            // line and Small 0, 1 (the least valuable) fold.
            CollectionAssert.AreEqual(
                new[] { "3-star defence", "Small 2", "Small 3", "Small 4", "Small 5", "Kills", "Boss", "+3 more rewards" },
                lines.Select(l => l.Label).ToArray());
            Assert.AreEqual(40, lines[5].Count, "kept lines keep their own count");
            Assert.AreEqual(1 + 2, lines[7].Xp);
            Assert.AreEqual(1, lines[7].Count);
            Assert.IsNull(lines[7].Detail);

            // More headlines than room: every headline shows, the rest folds into one line.
            for (int i = 0; i < MatchSummary.MaxLines; i++) summary.Add(RewardLine.Headline("Head " + i, 1));
            lines = summary.Lines;
            Assert.AreEqual(MatchSummary.MaxLines + 2, lines.Count);
            Assert.IsTrue(lines.Take(MatchSummary.MaxLines + 1).All(l => l.Pinned));
            Assert.AreEqual("+9 more rewards", lines[lines.Count - 1].Label);
        }

        [Test]
        public void AHeadlineMergedWithAPlainLineStaysPinned() {
            var summary = new MatchSummary(new LevelProgress(1, 0, 100));
            summary.Add(new RewardLine("Rebirth 4", 10));
            summary.Add(new RewardLine("Rebirth 4", 5).AsHeadline().WithDetail("x2 income"));
            for (int i = 0; i < MatchSummary.MaxLines + 4; i++) summary.Add(new RewardLine("Sale " + i, 100));
            Assert.AreEqual("Rebirth 4", summary.Lines[0].Label);
            Assert.IsTrue(summary.Lines[0].Pinned, "WithDetail keeps the pin, merging keeps it");
            Assert.AreEqual(15, summary.Lines[0].Xp);
            Assert.AreEqual("x2 income", summary.Lines[0].Detail);
            Assert.IsFalse(new RewardLine("Plain", 1).Pinned);
        }

        // ------------------------------------------------------------ save repair

        [Test]
        public void ValidateRepairsGarbage() {
            var save = new RetentionSave {
                xp = -40, challengeDay = -77, streakDays = -3, streakDay = Day, bestStreak = -1,
                matchesFinished = -2, challengesCompleted = -9,
                challenges = new List<ChallengeSave> {
                    null, new("") , new("a") { progress = -5 }, new("a") { progress = 3 },
                    new(new string('x', RetentionSave.MaxIdLength + 1)),
                    new("b"), new("c"), new("d"), new("e"), new("f"), new("g"), new("h"), new("i"),
                },
            };
            save.Validate();
            Assert.AreEqual(0, save.xp);
            Assert.AreEqual(RetentionDay.None, save.challengeDay);
            Assert.AreEqual(0, save.streakDays);
            Assert.AreEqual(0, save.bestStreak);
            Assert.AreEqual(0, save.matchesFinished);
            Assert.AreEqual(0, save.challengesCompleted);
            Assert.AreEqual(RetentionSave.MaxChallenges, save.challenges.Count);
            Assert.AreEqual("a", save.challenges[0].id);
            Assert.AreEqual(0, save.challenges[0].progress, "the first 'a' is kept, repaired");
            Assert.AreEqual(1, save.challenges.Count(c => c.id == "a"));

            var huge = new RetentionSave { xp = long.MaxValue, streakDays = 5, streakDay = RetentionDay.None, challenges = null };
            huge.Validate();
            Assert.AreEqual(RetentionSave.MaxXp, huge.xp);
            Assert.AreEqual(0, huge.streakDays, "a streak with no day is no streak");
            Assert.IsNotNull(huge.challenges);
            Assert.IsNotNull(RetentionSave.Repair(null));
        }

        [Test]
        public void RefreshDropsUnknownIdsAndKeepsKnownProgress() {
            RetentionRules rules = Rules();
            IReadOnlyList<ChallengeDefinition> today = rules.ChallengesFor(Day);
            var save = new RetentionSave {
                challengeDay = Day,
                challenges = new List<ChallengeSave> {
                    new("removed_from_pool") { progress = 4 },
                    new(today[1].id) { progress = 1_000_000, done = false },
                },
            };
            Assert.IsTrue(rules.Refresh(save, Day));
            CollectionAssert.AreEqual(Ids(today), save.challenges.Select(c => c.id).ToArray());
            Assert.AreEqual(today[1].target, save.challenges[1].progress, "known progress is kept, clamped to the target");
            Assert.AreEqual(0, save.challenges[0].progress);

            var updates = new List<ChallengeUpdate>();
            rules.Count(save, Day, today[1].statKey, 1, updates);
            Assert.IsTrue(updates.Any(u => u.Index == 1 && u.Completed), "a clamped full challenge pays on its next count");
        }

        [Test]
        public void OldDayKeysStartOver() {
            RetentionRules rules = Rules();
            string id = rules.ChallengesFor(Day)[0].id;
            var save = new RetentionSave { challengeDay = Day - 3, challenges = new List<ChallengeSave> { new(id) { progress = 2, done = true } } };
            rules.Refresh(save, Day);
            Assert.AreEqual(Day, save.challengeDay);
            Assert.IsTrue(save.challenges.All(c => c.progress == 0 && !c.done), "yesterday's progress never counts today");

            var future = new RetentionSave { challengeDay = int.MaxValue, challenges = new List<ChallengeSave> { new(id) { progress = 2 } } };
            rules.Refresh(future, Day);
            Assert.AreEqual(Day, future.challengeDay);
            Assert.AreEqual(0, future.challenges[0].progress);
        }

        // ------------------------------------------------------------ clock skew and pick changes

        [Test]
        public void ServerABitBehindKeepsTheStreakAndPaysNoSecondBonus() {
            RetentionRules rules = Rules();
            var save = new RetentionSave();
            Assert.IsTrue(rules.ClaimStreak(save, Day - 1).Paid);
            // Server A, a few seconds after midnight: day 2 of the streak.
            Assert.IsTrue(rules.ClaimStreak(save, Day).Paid);
            Assert.AreEqual(2, save.streakDays);

            // Server B still reads the previous day: a read changes nothing, a claim pays nothing.
            RetentionSnapshotBroadcast snapshot = rules.Snapshot(save, RetentionDay.Start(Day) - TimeSpan.FromSeconds(3), true);
            Assert.AreEqual(2, snapshot.streakDays);
            Assert.IsTrue(snapshot.streakClaimedToday);
            Assert.AreEqual(Day, snapshot.challengeDay, "the snapshot shows the save's (newer) day");
            Assert.Greater(snapshot.secondsToReset, RetentionDay.SecondsPerDay - 10, "it resets at the end of the save's day");
            Assert.IsFalse(rules.ClaimStreak(save, Day - 1).Paid);
            Assert.AreEqual(2, save.streakDays);
            Assert.AreEqual(Day, save.streakDay);

            // Back on the right day: still paid once; tomorrow continues the streak.
            Assert.IsFalse(rules.ClaimStreak(save, Day).Paid);
            StreakClaim next = rules.ClaimStreak(save, Day + 1);
            Assert.IsTrue(next.Paid);
            Assert.AreEqual(3, next.Days);
        }

        [Test]
        public void ServerABitBehindNeverPaysAChallengeAgain() {
            RetentionRules rules = Rules();
            ChallengeDefinition first = rules.ChallengesFor(Day)[0];
            var save = new RetentionSave();
            var updates = new List<ChallengeUpdate>();
            rules.Count(save, Day, first.statKey, first.target, updates);
            Assert.IsTrue(updates.Any(u => u.Completed));

            // A server one day behind: the list is left as it is and the done challenge does not pay again.
            updates.Clear();
            Assert.IsFalse(rules.Refresh(save, Day - 1));
            rules.Count(save, Day - 1, first.statKey, first.target, updates);
            Assert.IsFalse(updates.Any(u => u.Completed));
            Assert.AreEqual(Day, save.challengeDay);
            Assert.IsTrue(save.challenges[0].done);
            Assert.AreEqual(StatEffect.None, rules.Preview(save, Day - 1, first.statKey, first.target, true));
        }

        [Test]
        public void SaveMoreThanADayAheadIsReset() {
            RetentionRules rules = Rules();
            var save = new RetentionSave { streakDays = 4, streakDay = Day + 2, bestStreak = 4, challengeDay = Day + 2 };
            StreakClaim claim = rules.ClaimStreak(save, Day);
            Assert.IsTrue(claim.Paid);
            Assert.AreEqual(1, claim.Days);
            rules.Refresh(save, Day);
            Assert.AreEqual(Day, save.challengeDay);
        }

        [Test]
        public void SameDayPickChangeKeepsDoneChallenges() {
            RetentionRules before = Rules();
            ChallengeDefinition done = before.ChallengesFor(Day)[1];
            var save = new RetentionSave();
            var updates = new List<ChallengeUpdate>();
            before.Count(save, Day, done.statKey, done.target, updates);
            Assert.IsTrue(updates.Single(u => u.Challenge.id == done.id).Completed);

            // Another build (or a pool edit) picks differently the same day: the done id stays behind the pick.
            RetentionRules other = Rules(pool: Pool().Where(c => c.id != done.id));
            Assert.IsTrue(other.Refresh(save, Day));
            CollectionAssert.AreEqual(Ids(other.ChallengesFor(Day)), save.challenges.Take(other.ChallengesFor(Day).Count).Select(c => c.id).ToArray());
            Assert.IsTrue(save.challenges.Any(c => c.id == done.id && c.done));

            // Back on the first build it is done, not fresh, and pays nothing.
            before.Refresh(save, Day);
            Assert.IsTrue(save.challenges[1].done);
            updates.Clear();
            before.Count(save, Day, done.statKey, done.target, updates);
            Assert.IsFalse(updates.Any(u => u.Completed));
            Assert.AreEqual(1, save.challengesCompleted);
        }

        [Test]
        public void PreviewTellsWhatAReportWouldDoWithoutChangingTheSave() {
            RetentionRules rules = Rules();
            IReadOnlyList<ChallengeDefinition> today = rules.ChallengesFor(Day);
            ChallengeDefinition first = today[0];
            string unpicked = Pool().Select(c => c.statKey).First(key => today.All(c => c.statKey != key));
            var save = new RetentionSave();

            Assert.IsFalse(rules.UsesStat("not_a_stat"));
            Assert.IsFalse(rules.CountsToday(Day, unpicked));
            Assert.IsTrue(rules.CountsToday(Day, first.statKey));
            Assert.AreEqual(StatEffect.None, rules.Preview(save, Day, unpicked, 1000, true));
            Assert.AreEqual(StatEffect.None, rules.Preview(save, Day, "not_a_stat", 1000, true));
            if (first.target > 1) Assert.AreEqual(StatEffect.Progress, rules.Preview(save, Day, first.statKey, 1, true));
            Assert.AreEqual(StatEffect.Completes, rules.Preview(save, Day, first.statKey, first.target, true));
            Assert.AreEqual(RetentionDay.None, save.challengeDay, "a preview never writes");

            rules.Count(save, Day, first.statKey, first.target, null);
            Assert.AreEqual(StatEffect.None, rules.Preview(save, Day, first.statKey, 1, true), "a done challenge takes no more reports");
        }

        // ------------------------------------------------------------ save document

        public sealed class SampleSave : ProgressData {
            public override int LatestVersion => 1;
            public int gamesPlayed;
            public RetentionSave retention = new();

            public override void Validate() => retention = RetentionSave.Repair(retention);
        }

        [Test]
        public void BlockRoundTripsInsideAGameDocument() {
            RetentionRules rules = Rules();
            var save = new SampleSave { gamesPlayed = 3 };
            rules.AddXp(save.retention, 640);
            rules.ClaimStreak(save.retention, Day);
            rules.Count(save.retention, Day, rules.ChallengesFor(Day)[0].statKey, 1, null);

            JObject json = ProgressCodec.Serialize(save);
            Assert.Less(ProgressCodec.SizeInBytes(json), 1024, "the block stays far below the 64 KiB document cap");
            SampleSave back = ProgressCodec.Deserialize<SampleSave>(json, 1);
            Assert.AreEqual(640, back.retention.xp);
            Assert.AreEqual(1, back.retention.streakDays);
            Assert.AreEqual(Day, back.retention.challengeDay);
            CollectionAssert.AreEqual(save.retention.challenges.Select(c => c.progress), back.retention.challenges.Select(c => c.progress));
        }

        [Test]
        public void OlderDocumentsWithoutTheBlockAreRepaired() {
            SampleSave missing = ProgressCodec.Deserialize<SampleSave>(JObject.Parse("{\"version\":1,\"gamesPlayed\":7}"), 1);
            Assert.AreEqual(7, missing.gamesPlayed);
            Assert.IsNotNull(missing.retention);
            SampleSave nulled = ProgressCodec.Deserialize<SampleSave>(JObject.Parse("{\"version\":1,\"retention\":null}"), 1);
            Assert.IsNotNull(nulled.retention);
            SampleSave garbage = ProgressCodec.Deserialize<SampleSave>(JObject.Parse("{\"version\":1,\"retention\":{\"xp\":-9,\"challenges\":[null,{\"id\":\"x\",\"progress\":-1}]}}"), 1);
            Assert.AreEqual(0, garbage.retention.xp);
            Assert.AreEqual(1, garbage.retention.challenges.Count);
            Assert.AreEqual(0, garbage.retention.challenges[0].progress);
        }
    }
}
