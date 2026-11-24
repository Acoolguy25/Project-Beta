using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RyanAssets.Retention;

namespace Universes.UniverseData.tycoon.Tests {
    // How fast the level track fills against what the tycoon pays: a model player from a fresh save,
    // day by day, with the numbers the server uses (TY_Retention.DefaultTuning, TY_RewardTuning,
    // TY_DailyChest, TY_GoldCoins, TY_Goals, the challenge pool).
    public class TY_PacingTests {
        // The model player: plays an hour on day 1 and 40 minutes on every later day, buys the cheapest
        // thing it can (rebirths as soon as it can), grabs most gold coins, empties its collector now and
        // then, finishes the three daily challenges early in each session (a live fresh-save run finished day 1's
        // three in its first five minutes; most take longer) and opens the chest every day.
        const double FirstSessionMinutes = 60, SessionMinutes = 40;
        const double CoinsGrabbed = 0.75;
        const double SecondsPerCollect = 30;
        /// <summary>Minutes into a session when each daily challenge is done (the first, second, third).</summary>
        static readonly double[] ChallengeMinutes = { 4, 8, 16 };
        /// <summary>Seconds of walking to a pad for every purchase, and between a rebirth and the next run.</summary>
        const double SecondsPerPurchase = 5, SecondsPerRebirth = 15;

        /// <summary>What the model printed (also shown in the test output), for the report.</summary>
        sealed class PacingRun {
            public readonly List<(int rebirth, double minute, int level)> rebirths = new();
            public readonly Dictionary<int, (int day, double minute)> levelReached = new();
            public double coinCash, factoryCash;
            public double coinCashLate, factoryCashLate;
            public string log;
        }

        static readonly RetentionTuning Tuning = TY_Retention.DefaultTuning();

        /// <summary>Prints the numbers into the test results (and the console, for a runner without an NUnit context).</summary>
        static void Report(string text) {
            Console.WriteLine(text);
            try {
                TestContext.WriteLine(text);
            } catch (NullReferenceException) {
                // Outside Unity's test runner (a plain reflection run) there is no test context to write to.
            }
        }

        static int LevelOf(long xp) {
            LevelCurve curve = Tuning.levels;
            int level = 1;
            while (level < curve.maxLevel && xp >= curve.XpToNext(level)) { xp -= curve.XpToNext(level); level++; }
            return level;
        }

        static PacingRun Simulate(int days) {
            var run = new PacingRun();
            var rewards = new TY_RewardTuning();
            var chest = new TY_DailyChest();
            var coins = new TY_GoldCoins();
            var unlocks = new TY_UnlockTuning();
            var offline = new TY_OfflineEarnings();
            ChallengeDefinition[] pool = TY_Retention.ChallengePool();
            double challengeXp = pool.Average(c => c.xpReward);
            UnlockDefinition[] track = TY_Retention.Unlocks(unlocks, offline);
            int UnlockLevel(string id) => track.First(u => u.id == id).level;

            ulong purchased = TY_Catalog.StarterMask;
            TY_PerkLevels perks = default;
            int rebirths = 0, goal = 0, level = 1, tokens = 0, perkLevels = 0;
            double played = 0;
            long xp = 0, coinCount = 0, collects = 0;
            double cash = 0, lifetime = 0, minutes = 0;
            var log = new StringBuilder();

            void AddXp(int amount, int day) {
                xp += amount;
                int now = LevelOf(xp);
                for (int l = level + 1; l <= now; l++) if (!run.levelReached.ContainsKey(l)) run.levelReached[l] = (day, minutes);
                level = now;
            }

            double Income() => TY_Catalog.Income(purchased, rebirths, perks) * (level >= UnlockLevel(TY_Retention.Legend) ? unlocks.legendIncome : 1f);
            double CoinRate() => CoinsGrabbed / (coins.MeanInterval * (level >= UnlockLevel(TY_Retention.CoinRain) ? unlocks.coinRainInterval : 1f));
            float Lucky() => level >= UnlockLevel(TY_Retention.LuckyCoins) ? unlocks.luckyCoinsMultiplier : 1f;

            TY_GoalStats Stats() => new() {
                purchased = purchased, collects = collects, coins = coinCount, lifetime = (long)Math.Min(long.MaxValue / 4d, lifetime),
                income = (long)Income(), rebirths = rebirths, perkLevels = perkLevels,
            };

            void Goals(int day) {
                for (int i = 0; i < 20 && TY_Goals.IsMet(goal, Stats()); i++) {
                    TY_Goal g = TY_Goals.Get(goal++);
                    double paid = g.CashReward(Income());
                    cash += paid; lifetime += paid;
                    AddXp(g.xp, day);
                }
            }

            // Earns for dt seconds: the factory plus the coins grabbed in that time.
            void Earn(double dt, int day) {
                double income = Income();
                double coinsGot = CoinRate() * dt;
                double factory = income * dt, bonus = coinsGot * coins.Reward(income, Lucky());
                cash += factory + bonus; lifetime += factory + bonus;
                run.factoryCash += factory; run.coinCash += bonus;
                if (level >= UnlockLevel(TY_Retention.CoinRain)) { run.factoryCashLate += factory; run.coinCashLate += bonus; }
                coinCount = (long)(coinCount + coinsGot);
                collects += (long)(dt / SecondsPerCollect);
                AddXp((int)Math.Round(coinsGot * rewards.coinXp), day);
                minutes += dt / 60d;
                // Play-time XP, once a minute.
                played += dt / 60d;
                for (; played >= 1d; played -= 1d) AddXp(rewards.minuteXp, day);
            }

            // Spends perk tokens: income first (it is what the model measures), then the rest.
            void BuyPerks() {
                foreach (TY_Perk perk in TY_Perks.All) {
                    int lvl = perks.Level(perk.id);
                    while (lvl < perk.maxLevel && tokens >= perk.Cost(lvl)) { tokens -= perk.Cost(lvl); perks = perks.WithLevel(perk.id, ++lvl); perkLevels++; }
                }
            }

            int streak = 0;
            for (int day = 1; day <= days; day++) {
                double sessionEnd = minutes + (day == 1 ? FirstSessionMinutes : SessionMinutes);
                if (day > 1) {
                    // Away since yesterday's session: offline earnings at the cap of the unlocks reached.
                    bool night = level >= UnlockLevel(TY_Retention.NightShift), over = level >= UnlockLevel(TY_Retention.Overtime);
                    long paid = offline.Payout(Income(), 20 * 3600, night, over);
                    cash += paid; lifetime += paid;
                }
                streak++;
                AddXp(Tuning.streak.BonusXp(streak), day);
                long prize = chest.Cash(Income(), streak);
                cash += prize; lifetime += prize;
                AddXp(chest.Xp(streak), day);
                double sessionStart = minutes;
                int challengesPaid = 0;
                while (minutes < sessionEnd) {
                    Goals(day);
                    while (challengesPaid < Math.Min(Tuning.dailyChallengeCount, ChallengeMinutes.Length) && minutes >= sessionStart + ChallengeMinutes[challengesPaid]) {
                        challengesPaid++;
                        AddXp((int)Math.Round(challengeXp), day);
                    }
                    int next = TY_Catalog.AvailableItems(purchased, rebirths).OrderBy(i => TY_Catalog.Price(i, rebirths)).First();
                    long price = TY_Catalog.Price(next, rebirths);
                    if (cash < price) {
                        double rate = Income() * (1 + CoinRate() * coins.incomeSeconds * Lucky());
                        double dt = Math.Min((price - cash) / rate, (sessionEnd - minutes) * 60d);
                        // Small steps, so goals and coins land on time.
                        Earn(Math.Min(dt, 30d) + 1e-6, day);
                        continue;
                    }
                    cash -= price;
                    if (next == TY_Catalog.RebirthIndex) {
                        bool press = level >= UnlockLevel(TY_Retention.TokenPress);
                        TY_RebirthResult result = rewards.Rebirth(rebirths, purchased, level >= UnlockLevel(TY_Retention.HeadOffice), press ? unlocks.tokenPressTokens : 0);
                        rebirths = result.rebirths;
                        purchased = result.kept;
                        tokens += result.tokens;
                        cash = TY_Perks.StartingCash(perks);
                        Goals(day);
                        AddXp(result.xp, day);
                        run.rebirths.Add((rebirths, minutes, level));
                        BuyPerks();
                        Earn(SecondsPerRebirth, day);
                    } else {
                        purchased |= 1ul << next;
                        Earn(SecondsPerPurchase, day);
                    }
                }
                log.Append($"day {day}: level {level}, rebirths {rebirths}, {minutes:0} min played; ");
            }
            run.log = log.ToString();
            return run;
        }

        static string Describe(PacingRun run) {
            string Reb(int n) => run.rebirths.Count >= n ? $"rebirth {n} at {run.rebirths[n - 1].minute:0} min (level {run.rebirths[n - 1].level})" : $"no rebirth {n}";
            string Lvl(int l) => run.levelReached.TryGetValue(l, out var at) ? $"level {l} on day {at.day} ({at.minute:0} min played)" : $"level {l} not reached";
            return $"{Reb(1)}; {Reb(2)}; {Reb(3)}; {Reb(9)}; {Lvl(5)}; {Lvl(10)}; {Lvl(15)}; {Lvl(20)}; {Lvl(25)}. {run.log}";
        }

        [Test]
        public void LevelsLastDaysNotOneSitting() {
            PacingRun run = Simulate(14);
            string text = Describe(run);
            Report(text);
            Assert.GreaterOrEqual(run.rebirths.Count, 9, text);
            int first = run.rebirths[0].level;
            Assert.That(first, Is.InRange(4, 6), $"level 5 should come at about the first rebirth: {text}");
            Assert.That(run.rebirths[2].level, Is.InRange(5, 10), $"rebirth 3: {text}");
            Assert.IsTrue(run.levelReached.TryGetValue(25, out var l25), $"level 25 within two weeks: {text}");
            Assert.That(l25.day, Is.InRange(4, 10), $"level 25 should take several days of play: {text}");
            Assert.IsTrue(run.levelReached.TryGetValue(10, out var l10) && l10.minute >= 30, $"level 10 takes a full first session: {text}");
            Assert.IsTrue(run.levelReached.TryGetValue(15, out var l15) && l15.day >= 2, $"level 15 is not a first-day level: {text}");
        }

        [Test]
        public void ThreeChallengesAreWorthAboutALevel() {
            double three = TY_Retention.ChallengePool().Average(c => c.xpReward) * Tuning.dailyChallengeCount;
            int level12 = Tuning.levels.XpToNext(12);
            Report($"three challenges {three:0} XP, level 12 to 13 {level12} XP ({three / level12:0.00} of a level)");
            Assert.That(three / level12, Is.InRange(0.7, 1.4), $"three challenges {three:0} XP vs one level at 12: {level12} XP");
        }

        [Test]
        public void TheDailyChestBeatsAHandfulOfCoins() {
            var chest = new TY_DailyChest();
            var coins = new TY_GoldCoins();
            var unlocks = new TY_UnlockTuning();
            // A coin with Lucky Coins, against day 1's chest: the chest is worth many coins, and grows.
            double coin = coins.incomeSeconds * unlocks.luckyCoinsMultiplier;
            Assert.GreaterOrEqual(chest.IncomeSecondsOn(1) / coin, 6, "day 1's chest is worth at least six coins");
            Assert.Greater(chest.IncomeSecondsOn(7), chest.IncomeSecondsOn(1) * 3, "a week's streak makes it much bigger");
            Assert.Greater(chest.Xp(7), chest.Xp(1));
        }

        [Test]
        public void GoldCoinsStayUnderHalfTheCashLater() {
            PacingRun run = Simulate(4);
            double share = run.coinCashLate / Math.Max(1d, run.coinCashLate + run.factoryCashLate);
            Report($"after Coin Rain: coins {share:P0} of the cash ({Describe(run)})");
            Assert.Less(share, 0.5, "the factory, not the coins, should earn most of the cash");
        }
    }
}
