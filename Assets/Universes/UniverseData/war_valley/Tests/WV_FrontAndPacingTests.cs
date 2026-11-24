using System;
using System.Collections.Generic;
using NUnit.Framework;
using RyanAssets.Retention;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>
    /// Covers what keeps War Valley fresh and fair from day to day: the daily front, the featured
    /// doctrine card, the late-joiner rule and the summary card's lines, and prints the pacing of the
    /// level track run by run (rewards, curve and wave sizes all read from the real code).
    /// </summary>
    public sealed class WV_FrontAndPacingTests {
        /// <summary>WV_CommanderBonuses is static server state: every test starts and ends with none.</summary>
        [SetUp, TearDown]
        public void ResetBonuses() => WV_CommanderBonuses.Clear();

        // --- The daily front ----------------------------------------------------

        [Test]
        public void Front_IsTheSameAllDay_AndChangesEveryDay() {
            var morning = new DateTime(2026, 11, 24, 0, 5, 0, DateTimeKind.Utc);
            var night = new DateTime(2026, 11, 24, 23, 55, 0, DateTimeKind.Utc);
            Assert.That(WV_DailyFront.Today(night), Is.EqualTo(WV_DailyFront.Today(morning)));
            for (int day = -50; day < 400; day++) {
                WV_Front today = WV_DailyFront.Featured(day);
                Assert.That(today, Is.Not.EqualTo(WV_Front.None));
                Assert.That(WV_DailyFront.Featured(day + 1), Is.Not.EqualTo(today), $"day {day} repeats the day before");
            }
        }

        [Test]
        public void EveryFront_ComesUpInEachBlockOfDays() {
            for (int block = 0; block < 20; block++) {
                var seen = new HashSet<WV_Front>();
                for (int i = 0; i < WV_DailyFront.Count; i++)
                    seen.Add(WV_DailyFront.Featured(block * WV_DailyFront.Count + i));
                Assert.That(seen.Count, Is.EqualTo(WV_DailyFront.Count), $"block {block}");
            }
        }

        [Test]
        public void EveryFront_ChangesPlay_AndSaysHow() {
            var tuning = new WV_FrontTuning();
            foreach (WV_Front front in Enum.GetValues(typeof(WV_Front))) {
                if (front == WV_Front.None)
                    continue;
                WV_FrontEffect effect = WV_DailyFront.Effect(front, tuning);
                WV_CommanderModifiers m = effect.Modifiers;
                bool changes = effect.EnemyHealthMultiplier > 1f || effect.XpBonus > 0f || effect.StartingFunds > 0
                               || m.BountyBonus > 0f || m.IncomeBonus > 0f || m.ProductionSpeedBonus > 0f
                               || m.StructureHealthBonus > 0f || m.VehicleHealthBonus > 0f;
                Assert.That(changes, Is.True, front.ToString());
                Assert.That(WV_DailyFront.Describe(front, tuning), Does.StartWith("Today: " + WV_DailyFront.Title(front)));
                WV_DoctrineDefinition featured = WV_DoctrineCatalog.Get(effect.Featured);
                Assert.That(featured.UnlockId, Is.Null, $"{front} features a card a new commander cannot be dealt");
            }
            Assert.That(WV_DailyFront.Describe(WV_Front.None, tuning), Is.Empty);
        }

        [Test]
        public void PreviewOffset_MovesTheDay() {
            var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
            var tuning = new WV_FrontTuning { PreviewDayOffset = 1 };
            Assert.That(WV_DailyFront.Today(tuning.Now(now)), Is.EqualTo(WV_DailyFront.Today(now.AddDays(1))));
            Assert.That(new WV_FrontTuning().Now(now), Is.EqualTo(now));
        }

        [Test]
        public void FrontBaseline_AddsToEveryCommander() {
            WV_CommanderBonuses.Clear();
            WV_CommanderBonuses.Baseline = new WV_CommanderModifiers { BountyBonus = 0.25f };
            WV_CommanderBonuses.Set(3, new WV_CommanderModifiers { BountyBonus = 0.3f });
            Assert.That(WV_CommanderBonuses.For(3).BountyMultiplier, Is.EqualTo(1.55f).Within(1e-4f));
            Assert.That(WV_CommanderBonuses.For(99).BountyMultiplier, Is.EqualTo(1.25f).Within(1e-4f));
            WV_CommanderBonuses.Clear();
            Assert.That(WV_CommanderBonuses.For(3).BountyMultiplier, Is.EqualTo(1f));
        }

        // --- The featured card ------------------------------------------------------

        [Test]
        public void Feature_PutsTheCardFirst_AndKeepsTheHandSize() {
            byte[] ranks = WV_DoctrineCatalog.NewRanks();
            var hand = new List<WV_DoctrineId> { WV_DoctrineId.WarChest, WV_DoctrineId.Bastion, WV_DoctrineId.DeepVeins };
            Assert.That(WV_DoctrineCatalog.Feature(hand, WV_DoctrineId.Sharpshooters, ranks), Is.True);
            Assert.That(hand, Is.EqualTo(new[] { WV_DoctrineId.Sharpshooters, WV_DoctrineId.WarChest, WV_DoctrineId.Bastion }));

            var already = new List<WV_DoctrineId> { WV_DoctrineId.WarChest, WV_DoctrineId.Sharpshooters };
            Assert.That(WV_DoctrineCatalog.Feature(already, WV_DoctrineId.Sharpshooters, ranks), Is.True);
            Assert.That(already, Is.EqualTo(new[] { WV_DoctrineId.Sharpshooters, WV_DoctrineId.WarChest }));
        }

        [Test]
        public void Feature_SkipsMaxedAndLockedCards() {
            byte[] ranks = WV_DoctrineCatalog.NewRanks();
            ranks[(int)WV_DoctrineId.WarBonds] = (byte)WV_DoctrineCatalog.Get(WV_DoctrineId.WarBonds).MaxRank;
            var hand = new List<WV_DoctrineId> { WV_DoctrineId.WarChest };
            Assert.That(WV_DoctrineCatalog.Feature(hand, WV_DoctrineId.WarBonds, ranks), Is.False);
            Assert.That(WV_DoctrineCatalog.Feature(hand, WV_DoctrineId.TotalWar, ranks), Is.False);
            Assert.That(hand, Is.EqualTo(new[] { WV_DoctrineId.WarChest }));
            var empty = new List<WV_DoctrineId>();
            Assert.That(WV_DoctrineCatalog.Feature(empty, WV_DoctrineId.Bastion, ranks), Is.True);
            Assert.That(empty, Is.EqualTo(new[] { WV_DoctrineId.Bastion }));
        }

        // --- Late joiners and the summary card --------------------------------------

        [TestCase(20, 20, true)]
        [TestCase(10, 20, true)]
        [TestCase(9, 20, false)]
        [TestCase(1, 20, false)]
        [TestCase(0, 1, false)]
        [TestCase(1, 1, true)]
        public void Attendance_HalfTheRoundWinsIt(int present, int waves, bool counts) =>
            Assert.That(WV_Attendance.WinCounts(present, waves, 0.5f), Is.EqualTo(counts));

        static WV_RoundOutcome Win(WV_DifficultyTier tier, int present, bool firstWin = false) => new() {
            Tier = tier, Victory = true, WaveReached = 20, WaveCount = 20, Held = 20, WavesPresent = present,
            Kills = 300, Stars = 2, LowestFlagFraction = 0.6f, FirstWin = firstWin, Doctrines = 9,
            NextGoal = "Win Veteran for Elite + Endless", Front = WV_Front.WarProfiteers
        };

        [Test]
        public void Summary_PinsTheWinAndStarsFirst() {
            List<RewardLine> lines = WV_RoundSummary.Build(Win(WV_DifficultyTier.Veteran, 20, firstWin: true),
                new WV_CampaignRewards(), new WV_DifficultyTuning(), new WV_FrontTuning());
            Assert.That(lines[0].Label, Is.EqualTo("Victory"));
            Assert.That(lines[0].Pinned, Is.True);
            Assert.That(lines[1].Label, Is.EqualTo("Stars"));
            Assert.That(lines[1].Pinned, Is.True);
            Assert.That(lines.Exists(l => l.Label == "First Veteran win"), Is.True);
            Assert.That(lines.Exists(l => l.Label == "Veteran bonus" && l.Xp > 0), Is.True);
            Assert.That(lines.Count, Is.LessThanOrEqualTo(MatchSummary.MaxLines), "the card should not need to fold");
        }

        [Test]
        public void Summary_ALateJoinerGetsTheirXpButNotTheWin() {
            var rewards = new WV_CampaignRewards();
            WV_RoundOutcome late = Win(WV_DifficultyTier.Recruit, 3, firstWin: true);
            Assert.That(WV_RoundSummary.Counts(late, rewards), Is.False);
            List<RewardLine> lines = WV_RoundSummary.Build(late, rewards, new WV_DifficultyTuning(), new WV_FrontTuning());
            Assert.That(lines[0].Label, Is.EqualTo("Joined late"));
            Assert.That(lines[0].Detail, Does.Contain("3 of 20"));
            Assert.That(lines.Exists(l => l.Label == "Victory" || l.Label == "Stars" || l.Label.StartsWith("First ")), Is.False);
            Assert.That(lines.Exists(l => l.Label == "Enemies you defeated"), Is.True);
            Assert.That(WV_RoundSummary.Title(late, new WV_DifficultyTuning(), false), Does.Contain("joined late"));
        }

        [Test]
        public void Summary_ALossNamesTheWave_AndIronStormPaysItsBonus() {
            var outcome = new WV_RoundOutcome {
                Tier = WV_DifficultyTier.Recruit, WaveReached = 9, WaveCount = 20, Held = 8, WavesPresent = 8, Kills = 70,
                Front = WV_Front.IronStorm
            };
            List<RewardLine> lines = WV_RoundSummary.Build(outcome, new WV_CampaignRewards(), new WV_DifficultyTuning(), new WV_FrontTuning());
            Assert.That(lines[0].Label, Is.EqualTo("Fell on wave"));
            Assert.That(lines[0].Detail, Is.EqualTo("9 of 20"));
            Assert.That(lines[0].Pinned, Is.True);
            RewardLine storm = lines.Find(l => l.Label == "Iron Storm bonus");
            // (8 waves x 15 + 70 kills) x 25%.
            Assert.That(storm.Xp, Is.EqualTo(48));
        }

        // --- Pacing -------------------------------------------------------------------

        /// <summary>One round of the modelled commander: tier, result, the wave it ended on and the stars.</summary>
        readonly struct ModelRound {
            public readonly WV_DifficultyTier Tier;
            public readonly bool Victory;
            public readonly int Reached;
            public readonly int Stars;

            public ModelRound(WV_DifficultyTier tier, bool victory, int reached, int stars = 0) {
                Tier = tier;
                Victory = victory;
                Reached = reached;
                Stars = stars;
            }
        }

        static ModelRound Loss(WV_DifficultyTier tier, int wave) => new(tier, false, wave);
        static ModelRound Won(WV_DifficultyTier tier, int stars) => new(tier, true, 20, stars);

        /// <summary>
        /// A new commander's first rounds: two Recruit losses, a first win, then each tier lost a
        /// time or two before it falls, with Endless runs mixed in once it opens.
        /// </summary>
        static readonly ModelRound[] Career = {
            Loss(WV_DifficultyTier.Recruit, 7), Loss(WV_DifficultyTier.Recruit, 12), Won(WV_DifficultyTier.Recruit, 2),
            Loss(WV_DifficultyTier.Veteran, 13), Won(WV_DifficultyTier.Veteran, 1), Loss(WV_DifficultyTier.Elite, 11),
            Loss(WV_DifficultyTier.Elite, 15), Won(WV_DifficultyTier.Elite, 1), Won(WV_DifficultyTier.Veteran, 3),
            Loss(WV_DifficultyTier.Nightmare, 10), Loss(WV_DifficultyTier.Endless, 24), Loss(WV_DifficultyTier.Nightmare, 14),
            Won(WV_DifficultyTier.Elite, 2), Loss(WV_DifficultyTier.Nightmare, 16), Loss(WV_DifficultyTier.Endless, 26),
            Won(WV_DifficultyTier.Elite, 3), Won(WV_DifficultyTier.Nightmare, 1), Loss(WV_DifficultyTier.Endless, 28),
            Loss(WV_DifficultyTier.Nightmare, 17), Loss(WV_DifficultyTier.Endless, 27), Won(WV_DifficultyTier.Nightmare, 2),
            Loss(WV_DifficultyTier.Endless, 30), Loss(WV_DifficultyTier.Nightmare, 18), Loss(WV_DifficultyTier.Endless, 31),
        };

        const int RoundsPerDay = 2;
        /// <summary>Share of a wave's enemies the commander's forces defeat (turret and troop kills).</summary>
        const float KillShare = 0.8f;
        /// <summary>Daily challenges completed per day (of 3), at the pool's average reward.</summary>
        const int ChallengesPerDay = 2;

        static int Bodies(int waves) {
            var tuning = new WV_WaveTuning();
            int total = 0;
            for (int wave = 1; wave <= waves; wave++)
                total += WV_WavePlan.Build(wave, tuning).TotalCount;
            return total;
        }

        static int RoundXp(ModelRound round, HashSet<WV_DifficultyTier> won) {
            int held = round.Victory ? 20 : round.Reached - 1;
            bool first = round.Victory && won.Add(round.Tier);
            var outcome = new WV_RoundOutcome {
                Tier = round.Tier, Victory = round.Victory, WaveReached = round.Reached, WaveCount = 20, Held = held,
                WavesPresent = held, Kills = (int)(Bodies(held) * KillShare), Stars = round.Stars, LowestFlagFraction = 0.5f,
                FirstWin = first, Front = WV_Front.None
            };
            return WV_RoundSummary.TotalXp(outcome, new WV_CampaignRewards(), new WV_DifficultyTuning(), new WV_FrontTuning());
        }

        static int DailyXp(int day) {
            int challenges = 0;
            foreach (ChallengeDefinition challenge in WV_Retention.ChallengePool)
                challenges += challenge.xpReward;
            var streak = new StreakRules();
            int streakXp = streak.firstDayXp + streak.xpPerExtraDay * Math.Min(day - 1, streak.maxBonusDays - 1);
            return challenges * ChallengesPerDay / WV_Retention.ChallengePool.Length + streakXp;
        }

        /// <summary>
        /// Real Recruit rounds measured in try 1: run A lost on wave 7 for 270 XP, runs B and C lost on
        /// wave 10 for 234 and 249 XP. The model leaves out everything a round can add on top of waves and
        /// kills (doctrine and challenge XP), so it must stay at or under the measured rounds: the pacing
        /// below is then a slow-player floor, not a fiction.
        /// </summary>
        static readonly (int wave, int xp)[] MeasuredLosses = { (7, 270), (10, 234), (10, 249) };

        /// <summary>A commander who cannot win yet: six Recruit losses before the first win.</summary>
        static readonly ModelRound[] SlowStart = {
            Loss(WV_DifficultyTier.Recruit, 5), Loss(WV_DifficultyTier.Recruit, 6), Loss(WV_DifficultyTier.Recruit, 7),
            Loss(WV_DifficultyTier.Recruit, 8), Loss(WV_DifficultyTier.Recruit, 9), Loss(WV_DifficultyTier.Recruit, 10),
            Won(WV_DifficultyTier.Recruit, 1), Loss(WV_DifficultyTier.Veteran, 9), Loss(WV_DifficultyTier.Veteran, 12),
        };

        static Dictionary<int, (int round, int day)> Simulate(ModelRound[] career, LevelCurve curve) {
            var won = new HashSet<WV_DifficultyTier>();
            var reachedAt = new Dictionary<int, (int round, int day)>();
            int total = 0, level = 1, toNext = curve.XpToNext(1);
            for (int i = 0; i < career.Length; i++) {
                int day = i / RoundsPerDay + 1;
                if (i % RoundsPerDay == 0)
                    total += DailyXp(day);
                int xp = RoundXp(career[i], won);
                total += xp;
                while (level < curve.maxLevel && total >= toNext) {
                    level++;
                    toNext += curve.XpToNext(level);
                    reachedAt[level] = (i + 1, day);
                }
                Console.WriteLine($"round {i + 1,2} day {day,2} {career[i].Tier,-9} {(career[i].Victory ? "won " + career[i].Stars + "*" : "lost w" + career[i].Reached),-8} +{xp,4} XP  total {total,6}  level {level}");
            }
            return reachedAt;
        }

        [Test]
        public void Pacing_ModelLosses_AreCloseToTheMeasuredRounds() {
            foreach ((int wave, int xp) in MeasuredLosses) {
                int model = RoundXp(Loss(WV_DifficultyTier.Recruit, wave), new HashSet<WV_DifficultyTier>());
                Console.WriteLine($"lost on wave {wave,2}: measured {xp,4} XP, model {model,4} XP");
                Assert.That(model, Is.InRange(xp / 3, xp), $"the model's wave-{wave} loss is not a floor under the measured round");
            }
        }

        [Test]
        public void Pacing_ASlowStart_StillGetsTheFirstFourUnlocksInThreeDays() {
            LevelCurve curve = WV_Retention.DefaultTuning().levels;
            Dictionary<int, (int round, int day)> reachedAt = Simulate(SlowStart, curve);
            for (int i = 0; i < 4; i++) {
                UnlockDefinition unlock = WV_Retention.Unlocks[i];
                Assert.That(reachedAt.ContainsKey(unlock.level), Is.True, $"{unlock.title} is never reached by a slow starter");
                Console.WriteLine($"level {unlock.level,2} {unlock.title,-16} round {reachedAt[unlock.level].round,2}  day {reachedAt[unlock.level].day,2}");
                Assert.That(reachedAt[unlock.level].day, Is.InRange(1, 3), $"{unlock.title} comes too late for a commander still losing on Recruit");
            }
        }

        [Test]
        public void Pacing_SpreadsTheUnlocksOverTenDays_AndTheTrackEndsAtTheLastUnlock() {
            RetentionTuning tuning = WV_Retention.DefaultTuning();
            Assert.That(tuning.levels.maxLevel, Is.EqualTo(WV_Retention.Unlocks[^1].level), "levels above the last unlock would be empty");
            Dictionary<int, (int round, int day)> reachedAt = Simulate(Career, tuning.levels);

            foreach (UnlockDefinition unlock in WV_Retention.Unlocks) {
                Assert.That(reachedAt.ContainsKey(unlock.level) || unlock.level == 1, Is.True, $"{unlock.title} is never reached");
                (int round, int day) = reachedAt[unlock.level];
                Console.WriteLine($"level {unlock.level,2} {unlock.title,-16} round {round,2}  day {day,2}");
            }

            // The first round always gives the first unlock; the last takes more than a week of two rounds a day.
            Assert.That(reachedAt[WV_Retention.Unlocks[0].level].round, Is.EqualTo(1));
            Assert.That(reachedAt[WV_Retention.Unlocks[^1].level].day, Is.InRange(8, 14));
            // No day brings more than three unlocks.
            var perDay = new Dictionary<int, int>();
            foreach (UnlockDefinition unlock in WV_Retention.Unlocks) {
                int day = reachedAt[unlock.level].day;
                perDay.TryGetValue(day, out int n);
                perDay[day] = n + 1;
                Assert.That(perDay[day], Is.LessThanOrEqualTo(3), $"day {day} brings too many unlocks");
            }
        }
    }
}
