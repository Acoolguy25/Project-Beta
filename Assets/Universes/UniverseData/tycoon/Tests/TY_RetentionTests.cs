using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RyanAssets.Retention;

namespace Universes.UniverseData.tycoon.Tests {
    // Reasons to come back: goals, offline earnings, landmarks behind rebirths, the retention kit's rules.
    public class TY_RetentionTests {
        static readonly TY_PerkLevels NoPerks = default;

        // ---------------------------------------------------------------- goals
        [Test]
        public void TheGoalChainNeverEndsAndAlwaysAsksForMore() {
            var lastTarget = new Dictionary<TY_GoalStat, long>();
            for (int i = 0; i < TY_Goals.AuthoredCount + 40; i++) {
                TY_Goal goal = TY_Goals.Get(i);
                Assert.IsFalse(string.IsNullOrWhiteSpace(goal.Describe()), $"goal {i} has no text");
                Assert.Greater(goal.target, 0, $"goal {i}");
                Assert.IsTrue(goal.cash > 0 || goal.incomeSeconds > 0 || goal.tokens > 0, $"goal {i} pays nothing");
                Assert.Greater(goal.xp, 0, $"goal {i} pays no XP");
                // A goal on the same stat never asks for less than an earlier one (or it would complete at once).
                if (lastTarget.TryGetValue(goal.stat, out long previous))
                    Assert.GreaterOrEqual(goal.target, previous, $"goal {i} ({goal.Describe()}) asks for less than before");
                lastTarget[goal.stat] = goal.target;
            }
            // The tail keeps going: deterministic and growing.
            Assert.AreEqual(TY_Goals.Get(500).target, TY_Goals.Get(500).target);
            Assert.Greater(TY_Goals.Get(TY_Goals.AuthoredCount + 10).target, TY_Goals.Get(TY_Goals.AuthoredCount + 8).target);
            Assert.Greater(TY_Goals.Get(TY_Goals.AuthoredCount + 11).target, TY_Goals.Get(TY_Goals.AuthoredCount + 9).target);
            Assert.DoesNotThrow(() => TY_Goals.Get(100_000).Describe(), "a huge tail index must not overflow");
        }

        [Test]
        public void BuildGoalsFitTheCatalog() {
            for (int i = 0; i < TY_Goals.AuthoredCount; i++) {
                TY_Goal goal = TY_Goals.Get(i);
                long most = goal.stat switch {
                    TY_GoalStat.Droppers => TY_Catalog.OwnedOfKind(TY_Catalog.AllButRebirth, TY_ItemKind.Dropper),
                    TY_GoalStat.Upgraders => TY_Catalog.OwnedOfKind(TY_Catalog.AllButRebirth, TY_ItemKind.Upgrader),
                    TY_GoalStat.Buildings => TY_Catalog.OwnedOfKind(TY_Catalog.AllButRebirth, TY_ItemKind.Building),
                    TY_GoalStat.CollectorUpgrades => TY_Catalog.OwnedOfKind(TY_Catalog.AllButRebirth, TY_ItemKind.Utility),
                    TY_GoalStat.Built => TY_Catalog.BaseCount,
                    TY_GoalStat.Landmarks => TY_Catalog.LandmarkCount,
                    _ => long.MaxValue,
                };
                Assert.LessOrEqual(goal.target, most, $"goal {i} ({goal.Describe()}) can never be met");
            }
            Assert.AreEqual(TY_Catalog.LandmarkCount, TY_Goals.Get(TY_Goals.AuthoredCount - 1).target, "the chain ends on every landmark");
        }

        [Test]
        public void GoalsReadTheirStats() {
            var stats = new TY_GoalStats { purchased = TY_Catalog.StarterMask };
            Assert.AreEqual(0, TY_Goals.FirstUnmet(stats), "a new player starts at the first goal");
            Assert.IsFalse(TY_Goals.IsMet(0, stats));
            stats.collects = 1;
            Assert.IsTrue(TY_Goals.IsMet(0, stats));
            Assert.AreEqual(1, TY_Goals.FirstUnmet(stats));
            // A veteran's save (everything built, many rebirths) skips the goals already behind them.
            var veteran = new TY_GoalStats {
                purchased = TY_Catalog.AllButRebirth, collects = 1000, coins = 1000, lifetime = 10_000_000_000,
                income = 1_000_000, rebirths = 25, perkLevels = 10,
            };
            int start = TY_Goals.FirstUnmet(veteran);
            Assert.GreaterOrEqual(start, TY_Goals.AuthoredCount, "a finished veteran starts in the tail");
            Assert.IsFalse(TY_Goals.IsMet(start, veteran));
            Assert.AreEqual(TY_Catalog.LandmarkCount, veteran.Get(TY_GoalStat.Landmarks));
            Assert.AreEqual(TY_Catalog.BaseCount, veteran.Get(TY_GoalStat.Built), "landmarks do not count as factory items");
        }

        [Test]
        public void GoalCashScalesWithIncome() {
            var flat = new TY_Goal(TY_GoalStat.Coins, 1, "x", cash: 300);
            Assert.AreEqual(300, flat.CashReward(0));
            Assert.AreEqual(300, flat.CashReward(1e9), "flat goals ignore income");
            var scaled = new TY_Goal(TY_GoalStat.Coins, 1, "x", cash: 100, incomeSeconds: 60f);
            Assert.AreEqual(100, scaled.CashReward(1));
            Assert.AreEqual(60_000, scaled.CashReward(1000));
            Assert.LessOrEqual(scaled.CashReward(double.MaxValue), (long)(long.MaxValue / 4d), "no overflow");
            Assert.AreEqual("$25/s", new TY_Goal(TY_GoalStat.Income, 25, "{0}").Describe());
        }

        // ---------------------------------------------------------------- offline earnings
        [Test]
        public void OfflineEarningsPayAShareUpToTheCap() {
            var offline = new TY_OfflineEarnings();
            offline.Validate();
            const double income = 100;
            Assert.AreEqual(0, offline.Payout(income, 60, false, false), "a short reconnect pays nothing");
            Assert.AreEqual(0, offline.Payout(0, 3600, false, false), "no income, no pay");
            Assert.AreEqual(0, offline.Payout(double.NaN, 3600, false, false));
            Assert.AreEqual((long)(income * offline.share * 3600), offline.Payout(income, 3600, false, false));
            long capped = offline.Payout(income, 30 * 3600, false, false);
            Assert.AreEqual((long)(income * offline.share * offline.capHours * 3600), capped, "pay stops at the cap");
            Assert.AreEqual(capped, offline.Payout(income, 300 * 3600, false, false));
            // Level unlocks raise the cap, then the share.
            long nightShift = offline.Payout(income, 30 * 3600, true, false);
            long overtime = offline.Payout(income, 30 * 3600, true, true);
            Assert.Greater(nightShift, capped);
            Assert.Greater(overtime, nightShift);
            Assert.AreEqual(offline.overtimeCapHours * 3600d, offline.PaidSeconds(1e9, false, true));
            Assert.LessOrEqual(offline.Payout(1e300, 1e9, true, true), (long)(long.MaxValue / 4d), "no overflow");
        }

        [Test]
        public void TimeAwayIgnoresFirstVisitsAndClockJumps() {
            Assert.AreEqual(0d, TY_OfflineEarnings.AwaySeconds(0, 1_700_000_000), "a save that never recorded a visit");
            Assert.AreEqual(0d, TY_OfflineEarnings.AwaySeconds(1_700_000_100, 1_700_000_000), "the clock went back");
            Assert.AreEqual(3600d, TY_OfflineEarnings.AwaySeconds(1_700_000_000, 1_700_003_600));
            Assert.AreEqual("45s", TY_OfflineEarnings.Duration(45));
            Assert.AreEqual("14m", TY_OfflineEarnings.Duration(14 * 60 + 5));
            Assert.AreEqual("2h 05m", TY_OfflineEarnings.Duration(2 * 3600 + 5 * 60));
        }

        [Test]
        public void OfflineTuningStaysSane() {
            var offline = new TY_OfflineEarnings { share = 2f, capHours = 3f, nightShiftCapHours = 1f, overtimeCapHours = 0f, minAwayMinutes = -4f };
            offline.Validate();
            Assert.AreEqual(1f, offline.share);
            Assert.GreaterOrEqual(offline.nightShiftCapHours, offline.capHours, "an unlock never lowers the cap");
            Assert.GreaterOrEqual(offline.overtimeCapHours, offline.capHours);
            Assert.AreEqual(0f, offline.minAwayMinutes);
        }

        // ---------------------------------------------------------------- landmarks and rebirths
        [Test]
        public void LandmarksWaitForTheirRebirth() {
            ulong everything = TY_Catalog.BaseMask | TY_Catalog.StarterMask;
            int seen = 0;
            for (int i = 0; i < TY_Catalog.Count; i++) {
                TY_Item item = TY_Catalog.Items[i];
                if (item.kind != TY_ItemKind.Landmark) {
                    Assert.AreEqual(0, item.rebirths, $"{item.name} needs no rebirth");
                    continue;
                }
                seen++;
                Assert.Greater(item.rebirths, 0, $"{item.name} is a rebirth reward");
                Assert.Greater(item.value, 1f, $"{item.name} raises income");
                Assert.AreEqual(i, TY_Catalog.LandmarkOpenedBy(item.rebirths));
                // Prerequisites aside, the rebirth count gates it.
                ulong withPrereq = everything | (item.prereq >= 0 ? 1ul << item.prereq : 0ul);
                Assert.IsFalse(TY_Catalog.Available(withPrereq, i, item.rebirths - 1));
                Assert.AreEqual($"Unlocks at rebirth {item.rebirths}", TY_Catalog.LockReason(withPrereq, i, item.rebirths - 1));
                Assert.IsTrue(TY_Catalog.Available(withPrereq, i, item.rebirths));
                Assert.AreEqual("", TY_Catalog.LockReason(withPrereq, i, item.rebirths));
            }
            Assert.AreEqual(TY_Catalog.LandmarkCount, seen);
            Assert.GreaterOrEqual(seen, 3, "rebirths need visible rewards");
            Assert.AreEqual(-1, TY_Catalog.LandmarkOpenedBy(0));
        }

        [Test]
        public void RebirthsKeepLandmarksAndOnlyHeadOfficeKeepsTheCollector() {
            ulong built = TY_Catalog.AllButRebirth;
            ulong kept = TY_Catalog.RebirthKeeps(built, false);
            Assert.AreEqual(TY_Catalog.StarterMask | TY_Catalog.LandmarkMask | TY_Catalog.DecorMask, kept);
            ulong office = TY_Catalog.RebirthKeeps(built, true);
            Assert.AreEqual(kept | TY_Catalog.UtilityMask, office);
            Assert.Greater(TY_Catalog.Multiplier(kept, 1, NoPerks), TY_Catalog.Multiplier(TY_Catalog.StarterMask, 1, NoPerks),
                "kept landmarks still pay after the rebirth");
        }

        [Test]
        public void RebirthPricesGrowAndReadCleanly() {
            long previous = 0;
            for (int r = 0; r < 30; r++) {
                long price = TY_Catalog.RebirthPrice(r);
                Assert.Greater(price, previous, $"rebirth {r + 1} must cost more than the one before");
                Assert.AreEqual(price, TY_Catalog.Price(TY_Catalog.RebirthIndex, r));
                previous = price;
            }
            Assert.AreEqual(TY_Catalog.Items[TY_Catalog.RebirthIndex].price, TY_Catalog.RebirthPrice(0));
            Assert.AreEqual(88_000, TY_Catalog.RoundPrice(87_654));
            Assert.AreEqual(1_500_000, TY_Catalog.RoundPrice(1_490_000));
            Assert.AreEqual(0, TY_Catalog.RoundPrice(double.NaN));
            Assert.LessOrEqual(TY_Catalog.RebirthPrice(10_000), (long)(long.MaxValue / 4d), "no overflow");
        }

        [Test]
        public void LaterRunsStayShortWithTheLandmarks() {
            // The greedy player of FirstRebirthTakesMinutesNotHours (no perks, levels or coins), through its
            // first eleven rebirths, buying each landmark as it opens. Every run (rebirth to rebirth) should stay
            // a session-sized chunk; later runs lean on perks and levels (TY_PacingTests models those).
            const int Runs = 11;
            ulong purchased = TY_Catalog.StarterMask;
            var runs = new List<double>();
            for (int rebirths = 0; rebirths < Runs; rebirths++) {
                double cash = 0, seconds = 0;
                while (true) {
                    int next = TY_Catalog.AvailableItems(purchased, rebirths).OrderBy(i => TY_Catalog.Price(i, rebirths)).First();
                    long price = TY_Catalog.Price(next, rebirths);
                    double need = price - cash;
                    if (need > 0) { seconds += need / TY_Catalog.Income(purchased, rebirths, NoPerks); cash += need; }
                    cash -= price;
                    if (next == TY_Catalog.RebirthIndex) break;
                    purchased |= 1ul << next;
                }
                runs.Add(seconds);
                purchased = TY_Catalog.RebirthKeeps(purchased, false);
            }
            string log = string.Join(", ", runs.Select(s => $"{s / 60:0.0}"));
            ulong opened = 0;
            for (int i = 0; i < TY_Catalog.Count; i++)
                if (TY_Catalog.Items[i].kind == TY_ItemKind.Landmark && TY_Catalog.Items[i].rebirths < Runs) opened |= 1ul << i;
            Assert.AreNotEqual(0ul, opened);
            Assert.AreEqual(opened, purchased & TY_Catalog.LandmarkMask, $"every landmark open by rebirth {Runs - 1} gets built ({log} min)");
            foreach (double run in runs) Assert.That(run, Is.InRange(60, 30 * 60), $"runs in minutes: {log}");
        }

        // ---------------------------------------------------------------- retention kit
        [Test]
        public void RetentionRulesBuild() {
            RetentionRules rules = null;
            Assert.DoesNotThrow(() => rules = TY_Retention.Rules(new RetentionTuning(), new TY_UnlockTuning(), new TY_OfflineEarnings()));
            Assert.AreEqual(TY_Retention.Universe, rules.UniverseId);
            Assert.AreEqual(TY_SaveUniverse, rules.UniverseId, "the kit and the save use one document");
            Assert.DoesNotThrow(() => TY_Retention.Rules(null, null, null), "defaults are filled in");
        }

        // The save's universe id (TY_SaveData lives in the server assembly, which tests cannot reference).
        const string TY_SaveUniverse = "tycoon";

        [Test]
        public void UnlocksAreSpreadOutAndDescribed() {
            UnlockDefinition[] unlocks = TY_Retention.Unlocks(new TY_UnlockTuning(), new TY_OfflineEarnings());
            Assert.That(unlocks.Length, Is.InRange(6, 10));
            Assert.AreEqual(unlocks.Length, unlocks.Select(u => u.id).Distinct().Count(), "unique ids");
            Assert.AreEqual(unlocks.Length, unlocks.Select(u => u.level).Distinct().Count(), "one unlock per level");
            for (int i = 1; i < unlocks.Length; i++) Assert.Greater(unlocks[i].level, unlocks[i - 1].level, "authored in level order");
            Assert.AreEqual(2, unlocks[0].level, "the first unlock comes in the first session");
            foreach (UnlockDefinition unlock in unlocks) {
                Assert.IsFalse(string.IsNullOrWhiteSpace(unlock.title));
                Assert.IsFalse(string.IsNullOrWhiteSpace(unlock.description));
                Assert.IsFalse(string.IsNullOrWhiteSpace(unlock.icon));
                StringAssert.DoesNotContain("{", unlock.description);
            }
            string[] ids = { TY_Retention.LuckyCoins, TY_Retention.NightShift, TY_Retention.BigHopper, TY_Retention.CoinRain, TY_Retention.Overtime,
                TY_Retention.RushVip, TY_Retention.TokenPress, TY_Retention.HeadOffice, TY_Retention.Legend };
            CollectionAssert.AreEquivalent(ids, unlocks.Select(u => u.id), "every unlock the server checks is on the track");
        }

        [Test]
        public void TheChallengePoolIsBigAndUsesReportedStats() {
            ChallengeDefinition[] pool = TY_Retention.ChallengePool();
            Assert.GreaterOrEqual(pool.Length, 8);
            Assert.AreEqual(pool.Length, pool.Select(c => c.id).Distinct().Count(), "unique ids");
            string[] reported = { TY_Retention.StatCollect, TY_Retention.StatCoins, TY_Retention.StatBuilds, TY_Retention.StatRebirth, TY_Retention.StatRush,
                TY_Retention.StatGoals, TY_Retention.StatMinutes, TY_Retention.StatEarn, TY_Retention.StatIncome, TY_Retention.StatHatch };
            foreach (ChallengeDefinition challenge in pool) {
                CollectionAssert.Contains(reported, challenge.statKey, $"{challenge.id} counts a stat the server never reports");
                Assert.Greater(challenge.target, 0);
                Assert.Greater(challenge.xpReward, 0);
                Assert.IsFalse(string.IsNullOrWhiteSpace(challenge.text));
            }
        }

        [Test]
        public void UnlockTuningStaysSane() {
            var tuning = new TY_UnlockTuning { luckyCoinsMultiplier = 0f, bigHopperCapacity = -1f, coinRainInterval = 0f, rushVipBonus = -2f, tokenPressTokens = -1, legendIncome = 0.5f };
            tuning.Validate();
            Assert.GreaterOrEqual(tuning.luckyCoinsMultiplier, 1f);
            Assert.GreaterOrEqual(tuning.bigHopperCapacity, 1f);
            Assert.Greater(tuning.coinRainInterval, 0f);
            Assert.GreaterOrEqual(tuning.rushVipBonus, 0f);
            Assert.GreaterOrEqual(tuning.tokenPressTokens, 0);
            Assert.GreaterOrEqual(tuning.legendIncome, 1f);
        }
    
        // ---------------------------------------------------------------- try 2: decisions the server and HUD share
        [Test]
        public void TheChestStreakContinuesRepeatsOrRestarts() {
            Assert.AreEqual(1, TY_DailyChest.StreakDayToday(0, -1, 100), "first ever chest");
            Assert.AreEqual(4, TY_DailyChest.StreakDayToday(3, 99, 100), "played yesterday: the streak goes on");
            Assert.AreEqual(3, TY_DailyChest.StreakDayToday(3, 100, 100), "already claimed today");
            Assert.AreEqual(1, TY_DailyChest.StreakDayToday(6, 97, 100), "missed days start again");
        }

        [Test]
        public void TheChestOpensOncePerDayAndOnlyAfterTheSave() {
            Assert.IsFalse(TY_DailyChest.Ready(false, -1, 100), "before the save is applied it could pay twice");
            Assert.IsTrue(TY_DailyChest.Ready(true, 99, 100));
            Assert.IsFalse(TY_DailyChest.Ready(true, 100, 100));
        }

        [Test]
        public void TheChestGrowsWithTheStreakAndPaysTokensOnSetDays() {
            var chest = new TY_DailyChest();
            chest.Validate();
            for (int day = 2; day <= chest.growthDays; day++) {
                Assert.Greater(chest.Cash(1000, day), chest.Cash(1000, day - 1), $"day {day} pays more cash");
                Assert.Greater(chest.Xp(day), chest.Xp(day - 1), $"day {day} pays more XP");
            }
            Assert.AreEqual(chest.Cash(1000, chest.growthDays), chest.Cash(1000, chest.growthDays + 5), "growth stops");
            Assert.AreEqual(chest.minimumCash, chest.Cash(0, 1), "a new player still gets the minimum");
            Assert.AreEqual(chest.minimumCash, chest.Cash(double.NaN, 3));
            Assert.AreEqual(0, chest.Tokens(1));
            Assert.AreEqual(chest.midWeekTokens, chest.Tokens(chest.midWeekDay));
            Assert.AreEqual(chest.weekTokens, chest.Tokens(chest.week));
            Assert.AreEqual(chest.weekTokens, chest.Tokens(chest.week * 2), "every week of the streak");
            Assert.AreEqual(chest.midWeekDay, chest.NextTokenDay(1));
            Assert.AreEqual(chest.week, chest.NextTokenDay(chest.midWeekDay + 1));
            Assert.AreEqual("5 min of best income", TY_DailyChest.PrizeText(5, 0));
            Assert.AreEqual("8 min of best income, +1 perk token", TY_DailyChest.PrizeText(8, 1));
            Assert.AreEqual("20 min of best income, +2 perk tokens", TY_DailyChest.PrizeText(20, 2));
            Assert.AreEqual(TY_DailyChest.PrizeText(chest.Minutes(3), chest.Tokens(3)), chest.Prize(3), "the sign and the server name the same prize");
        }

        [Test]
        public void AHeldMessageGoesOutOnceAndOnlyToAListener() {
            var held = new TY_HeldMessage<int>();
            Assert.IsFalse(held.TryTake(out _), "nothing held");
            held.Hold(7);
            Assert.IsFalse(held.TryTake(out _), "the client is not listening yet");
            Assert.IsTrue(held.Pending);
            held.MarkListening();
            Assert.IsTrue(held.TryTake(out int value));
            Assert.AreEqual(7, value);
            Assert.IsFalse(held.TryTake(out _), "sent once");
            held.Hold(8);
            Assert.IsTrue(held.TryTake(out value), "a listener gets a later message at once");
            Assert.AreEqual(8, value);
        }

        [Test]
        public void ARebirthKeepsLandmarksPaysAndNamesItself() {
            var rewards = new TY_RewardTuning();
            ulong solar = 1ul << TY_Catalog.Items.ToList().FindIndex(i => i.name == "Solar Farm");
            ulong built = TY_Catalog.AllButRebirth;
            TY_RebirthResult first = rewards.Rebirth(0, built, headOffice: false, extraTokens: 0);
            Assert.AreEqual(1, first.rebirths);
            Assert.AreEqual("Rebirth 1", first.Label);
            Assert.AreEqual(TY_Catalog.StarterMask | (built & (TY_Catalog.LandmarkMask | TY_Catalog.DecorMask)), first.kept);
            Assert.AreEqual(TY_Perks.TokensPerRebirth, first.tokens);
            Assert.AreEqual(rewards.rebirthXp, first.xp);
            Assert.AreEqual(TY_Catalog.LandmarkOpenedBy(1), first.landmark);
            StringAssert.StartsWith("Opens ", first.detail);
            TY_RebirthResult third = rewards.Rebirth(2, built | solar, headOffice: true, extraTokens: 2);
            Assert.AreEqual("Rebirth 3", third.Label);
            Assert.AreEqual(TY_Perks.TokensPerRebirth + 2, third.tokens);
            Assert.Greater(third.xp, first.xp, "later runs are longer and pay more");
            Assert.AreNotEqual(0ul, third.kept & solar, "landmarks survive");
            Assert.AreEqual(built & TY_Catalog.UtilityMask, third.kept & TY_Catalog.UtilityMask, "Head Office keeps the collector lane");
            if (third.landmark < 0) StringAssert.StartsWith("Income x", third.detail);
        }

        [Test]
        public void TheIncomeGoalPaysAtItsTargetAndOthersNameTheirSeconds() {
            var income = new TY_Goal(TY_GoalStat.Income, 500, "Reach {0}", incomeSeconds: 60, xp: 10);
            Assert.AreEqual(500 * 60, income.CashReward(200), "below the target: paid at the target");
            Assert.AreEqual(800 * 60, income.CashReward(800), "above it: paid at the income");
            Assert.IsFalse(income.PaysIncomeSeconds, "the card shows dollars, matching the toast");
            var builds = new TY_Goal(TY_GoalStat.Built, 10, "Build {0}", cash: 100, incomeSeconds: 30, xp: 10);
            Assert.IsTrue(builds.PaysIncomeSeconds);
            Assert.AreEqual(100, builds.CashReward(0), "the floor");
            Assert.AreEqual(30 * 50, builds.CashReward(50));
            var fixedCash = new TY_Goal(TY_GoalStat.Coins, 5, "Grab {0}", cash: 400, xp: 10);
            Assert.IsFalse(fixedCash.PaysIncomeSeconds);
        }

        [Test]
        public void TheWelcomeTextDoesNotRepeatTheTimeAndNamesTheCap() {
            var offline = new TY_OfflineEarnings();
            offline.Validate();
            string shortAway = offline.Describe(600, false, false);
            StringAssert.DoesNotContain(TY_OfflineEarnings.Duration(600), shortAway, "the card already shows how long you were away");
            StringAssert.DoesNotContain("Pay stops", shortAway);
            string capped = offline.Describe(30 * 3600, false, false);
            StringAssert.Contains("Pay stops after", capped);
            StringAssert.Contains("Night Shift", capped);
            StringAssert.DoesNotContain(TY_OfflineEarnings.Duration(30 * 3600), capped);
            StringAssert.Contains("Overtime", offline.Describe(30 * 3600, true, false));
            StringAssert.DoesNotContain("unlock", offline.Describe(30 * 3600, true, true));
        }

        [Test]
        public void PadsOfferTheRepeatableItemsOnlyWhileForSale() {
            ulong start = TY_Catalog.StarterMask;
            Assert.AreEqual(-1, TY_Catalog.PadItem(start, TY_ItemKind.Rebirth, 0), "no rebirth before the factory is built");
            Assert.AreEqual(-1, TY_Catalog.PadItem(start, TY_ItemKind.Invest, 0), "no investing before the factory is built");
            ulong full = TY_Catalog.AllButRebirth & ~TY_Catalog.LandmarkMask | TY_Catalog.StarterMask;
            Assert.AreEqual(TY_Catalog.RebirthIndex, TY_Catalog.PadItem(full, TY_ItemKind.Rebirth, 0));
            Assert.AreEqual(TY_Catalog.InvestIndex, TY_Catalog.PadItem(full, TY_ItemKind.Invest, 0));
            Assert.AreEqual(TY_Catalog.NextInLane(start, TY_ItemKind.Dropper), TY_Catalog.PadItem(start, TY_ItemKind.Dropper, 0));
            Assert.IsFalse(TY_Catalog.AvailableItems(full, 0).Contains(TY_Catalog.InvestIndex), "investing is never 'the next build'");
        }

        [Test]
        public void InvestingIsALateSinkThatKeepsPaying() {
            long previous = 0;
            for (int level = 0; level < 10; level++) {
                long price = TY_Catalog.InvestPrice(level, 3);
                Assert.Greater(price, previous, $"level {level} costs more");
                previous = price;
            }
            Assert.Greater(TY_Catalog.InvestPrice(0, 5), TY_Catalog.InvestPrice(0, 1), "dearer with rebirths, like the rebirth");
            Assert.AreEqual(TY_Catalog.InvestPrice(2, 4), TY_Catalog.Price(TY_Catalog.InvestIndex, 4, 2));
            Assert.AreEqual(1.0, TY_Catalog.InvestMultiplier(0), 1e-9);
            Assert.AreEqual(1.25, TY_Catalog.InvestMultiplier(5), 1e-9);
            Assert.IsTrue(TY_Catalog.Items[TY_Catalog.InvestIndex].IsRepeatable);
        }
    }
}
