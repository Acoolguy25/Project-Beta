using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Broadcast;
using NUnit.Framework;

namespace RyanAssets.Retention.Tests {
    /// <summary>
    /// EditMode tests of the reward orchestration the server service runs (<see cref="RetentionLedger"/>):
    /// every reward is paid exactly once, nothing is paid twice between play, challenges, the streak and
    /// the match card, and stat reports are coalesced.
    /// </summary>
    public class RetentionLedgerTests {
        const string Universe = "test_game";
        static readonly int Day = RetentionDay.Of(new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc));
        static readonly DateTime Noon = RetentionDay.Start(Day).AddHours(12);

        static readonly UnlockDefinition[] Unlocks = {
            new(2, "hat", "Hat", "A hat.", "star"),
            new(5, "rifle", "Rifle", "A long gun.", "crosshair"),
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

        /// <summary>Records what the ledger asked for.</summary>
        sealed class Sink : IRetentionSink {
            public readonly List<object> Sent = new();
            public long AccountXp;
            public long Gold;
            public int Payouts;
            public int Saves;
            public readonly List<string> UnlockIds = new();
            /// <summary>Runs after each send, like a game handler reacting to it.</summary>
            public Action<object> AfterSend;
            /// <summary>Run around each account payout, like RetentionService's level announcement.</summary>
            public Action<RetentionPlayer> BeforePay, AfterPay;

            public void Send<T>(RetentionPlayer player, T message) where T : struct, IBroadcast {
                Sent.Add(message);
                AfterSend?.Invoke(message);
            }

            public void PayAccount(RetentionPlayer player, int accountXp, int gold) {
                Assert.IsTrue(accountXp > 0 || gold > 0, "never a zero payout (LevelsServer warns on those)");
                BeforePay?.Invoke(player);
                AccountXp += accountXp;
                Gold += gold;
                Payouts++;
                AfterPay?.Invoke(player);
            }

            public void SaveSoon(RetentionPlayer player) => Saves++;

            public void Unlocked(RetentionPlayer player, UnlockDefinition unlock) => UnlockIds.Add(unlock.id);

            public IEnumerable<T> Of<T>() => Sent.OfType<T>();
        }

        sealed class Fixture {
            public readonly RetentionRules Rules;
            public readonly Sink Sink = new();
            public readonly RetentionLedger Ledger;
            public readonly RetentionPlayer Player = new();
            public readonly RetentionSave Save = new();

            public Fixture(RetentionTuning tuning = null) {
                Rules = new RetentionRules(Universe, tuning ?? new RetentionTuning(), Unlocks, Pool());
                Ledger = new RetentionLedger(Rules, Sink);
            }

            public ChallengeDefinition Picked(int index) => Rules.ChallengesFor(Day)[index];
        }

        [Test]
        public void ReportsAreCoalescedAndAChallengePaysOnce() {
            var f = new Fixture();
            ChallengeDefinition builds = f.Picked(0);
            Assert.Greater(builds.target, 2);

            Assert.AreEqual(StatEffect.Progress, f.Ledger.Queue(f.Player, f.Save, Day, builds.statKey, 1, true));
            Assert.AreEqual(StatEffect.Progress, f.Ledger.Queue(f.Player, f.Save, Day, builds.statKey, 1, true));
            Assert.IsTrue(f.Player.HasPendingStats);
            Assert.AreEqual(RetentionDay.None, f.Save.challengeDay, "queueing never touches the document");
            Assert.IsEmpty(f.Sink.Sent);

            f.Ledger.Flush(f.Player, f.Save, Day);
            RetentionProgressBroadcast progress = f.Sink.Of<RetentionProgressBroadcast>().Single();
            Assert.AreEqual(2, progress.progress, "two reports, one message");
            Assert.AreEqual(Day, progress.day);
            Assert.AreEqual(0, f.Sink.Payouts);

            Assert.AreEqual(StatEffect.Completes, f.Ledger.Queue(f.Player, f.Save, Day, builds.statKey, builds.target, true));
            f.Ledger.Flush(f.Player, f.Save, Day);
            RetentionChallengeBroadcast completed = f.Sink.Of<RetentionChallengeBroadcast>().Single();
            Assert.IsTrue(completed.completed);
            Assert.AreEqual(builds.goldReward, f.Sink.Gold);
            Assert.AreEqual(builds.xpReward, f.Sink.AccountXp);
            Assert.AreEqual(builds.xpReward, f.Save.xp);
            Assert.AreEqual(1, f.Sink.Saves, "a completion is saved at once");

            int sent = f.Sink.Sent.Count;
            Assert.AreEqual(StatEffect.None, f.Ledger.Queue(f.Player, f.Save, Day, builds.statKey, 100, true));
            Assert.IsFalse(f.Player.HasPendingStats);
            f.Ledger.Flush(f.Player, f.Save, Day);
            Assert.AreEqual(sent, f.Sink.Sent.Count, "a done challenge takes no reports and sends nothing");
            Assert.AreEqual(builds.goldReward, f.Sink.Gold);
        }

        [Test]
        public void UnpickedStatsCostNothing() {
            var f = new Fixture();
            Assert.AreEqual(StatEffect.None, f.Ledger.Queue(f.Player, f.Save, Day, "kills", 5, true), "kills is not picked on the test day");
            Assert.AreEqual(StatEffect.None, f.Ledger.Queue(f.Player, f.Save, Day, "unknown", 5, true));
            Assert.IsFalse(f.Player.HasPendingStats);
            Assert.IsEmpty(f.Sink.Sent);
        }

        [Test]
        public void ReachKeepsTheBestWhileQueued() {
            var f = new Fixture(new RetentionTuning { dailyChallengeCount = 8 });
            ChallengeDefinition wave = f.Rules.ChallengesFor(Day).Single(c => c.statKey == "wave");
            f.Ledger.Queue(f.Player, f.Save, Day, "wave", 6, false);
            f.Ledger.Queue(f.Player, f.Save, Day, "wave", 4, false);
            f.Ledger.Flush(f.Player, f.Save, Day);
            Assert.AreEqual(6, f.Sink.Of<RetentionProgressBroadcast>().Single().progress);
            Assert.AreEqual(StatEffect.Completes, f.Ledger.Queue(f.Player, f.Save, Day, "wave", wave.target, false));
        }

        [Test]
        public void MatchPaysEachRewardOnceAndTheStreakOncePerDay() {
            var f = new Fixture();
            ChallengeDefinition wins = f.Rules.ChallengesFor(Day).Single(c => c.statKey == "wins");
            f.Ledger.BeginMatch(f.Player, f.Save);
            f.Ledger.AddXp(f.Player, f.Save, 10, "Kill", true);
            f.Ledger.AddXp(f.Player, f.Save, 10, "Kill", true);
            // Completed in the last moment: still queued when the match ends.
            Assert.AreEqual(StatEffect.Completes, f.Ledger.Queue(f.Player, f.Save, Day, "wins", 1, true));
            f.Ledger.FinishMatch(f.Player, f.Save, Day, Noon, "Victory!", new[] { new RewardLine("Victory", 100, 25), RewardLine.Info("Stars", "3 / 3") });

            int streakXp = f.Rules.Tuning.streak.BonusXp(1), streakGold = f.Rules.Tuning.streak.BonusGold(1);
            long expectedXp = 20 + wins.xpReward + 100 + streakXp;
            Assert.AreEqual(expectedXp, f.Save.xp);
            Assert.AreEqual(expectedXp, f.Sink.AccountXp, "account XP follows game XP exactly once");
            Assert.AreEqual(wins.goldReward + 25 + streakGold, f.Sink.Gold);

            RetentionSummaryBroadcast card = f.Sink.Of<RetentionSummaryBroadcast>().Single();
            Assert.AreEqual(expectedXp, card.totalXp);
            Assert.AreEqual(1, card.streakDays);
            CollectionAssert.AreEqual(new[] { "Kill", wins.Describe(), "Victory", "Stars", f.Rules.Tuning.streak.Line(1) }, card.lines.Select(l => l.label).ToArray());
            Assert.AreEqual(2, card.lines[0].count);
            RetentionRewardView stars = card.lines.Single(l => l.label == "Stars");
            Assert.AreEqual("3 / 3", stars.detail);
            Assert.AreEqual(0, stars.xp);
            Assert.AreEqual(1, f.Sink.Of<RetentionSnapshotBroadcast>().Count(), "a fresh snapshot follows the card");
            Assert.AreEqual(2, f.Sink.Saves, "the challenge and the streak are saved at once");

            // A second match the same day: its own rewards, no streak, nothing from the first match again.
            long before = f.Sink.AccountXp;
            f.Ledger.BeginMatch(f.Player, f.Save);
            f.Ledger.FinishMatch(f.Player, f.Save, Day, Noon, "Defeat", new[] { new RewardLine("Played", 30) });
            RetentionSummaryBroadcast second = f.Sink.Of<RetentionSummaryBroadcast>().Last();
            Assert.AreEqual(0, second.streakDays);
            Assert.AreEqual(30, second.totalXp);
            Assert.AreEqual(before + 30, f.Sink.AccountXp);
            Assert.AreEqual(2, f.Save.matchesFinished);
        }

        [Test]
        public void ClaimDailyStreakPaysOncePerDay() {
            var f = new Fixture();
            f.Ledger.ClaimDailyStreak(f.Player, f.Save, Day);
            f.Ledger.ClaimDailyStreak(f.Player, f.Save, Day);
            Assert.AreEqual(1, f.Sink.Payouts);
            Assert.AreEqual(f.Rules.Tuning.streak.BonusGold(1), f.Sink.Gold);
            f.Ledger.FinishMatch(f.Player, f.Save, Day, Noon, "Done", Array.Empty<RewardLine>());
            Assert.AreEqual(1, f.Sink.Payouts, "the match finds today's streak already paid");
            f.Ledger.ClaimDailyStreak(f.Player, f.Save, Day + 1);
            Assert.AreEqual(2, f.Save.streakDays);
        }

        [Test]
        public void UnsavedPlayersGetNoAccountCurrencyFromDailyRewards() {
            var f = new Fixture();
            f.Player.Saved = false;
            ChallengeDefinition first = f.Picked(0);
            f.Ledger.Apply(f.Player, f.Save, Day, first.statKey, first.target, true);
            f.Ledger.ClaimDailyStreak(f.Player, f.Save, Day);
            Assert.AreEqual(0, f.Sink.Payouts, "daily rewards would come back on every rejoin");
            Assert.AreEqual(first.xpReward + f.Rules.Tuning.streak.BonusXp(1), f.Save.xp, "the session still levels up");

            f.Ledger.AddXp(f.Player, f.Save, 40, "Kill", true);
            Assert.AreEqual(40, f.Sink.AccountXp, "XP earned by playing still pays the account");
            f.Ledger.SendSnapshot(f.Player, f.Save, Noon);
            Assert.IsTrue(f.Sink.Of<RetentionSnapshotBroadcast>().Single().notSaved);
        }

        [Test]
        public void FractionalAccountXpIsCarried() {
            var f = new Fixture(new RetentionTuning { accountXpScale = 0.5f });
            for (int i = 0; i < 5; i++) f.Ledger.AddXp(f.Player, f.Save, 1, "Tick", false);
            Assert.AreEqual(2, f.Sink.AccountXp);
            Assert.AreEqual(2, f.Sink.Payouts, "no zero payouts in between");
            Assert.AreEqual(0.5, f.Player.AccountXpCarry, 1e-9);
            Assert.AreEqual(5, f.Save.xp);
        }

        [Test]
        public void LevelUpsAnnounceAndRaiseEachUnlockOnce() {
            var f = new Fixture();
            int toLevel5 = (int)f.Rules.Track.XpForLevel(5);
            f.Ledger.AddXp(f.Player, f.Save, toLevel5, "Bonus", true);
            RetentionLevelUpBroadcast up = f.Sink.Of<RetentionLevelUpBroadcast>().Single();
            Assert.AreEqual(1, up.previousLevel);
            Assert.AreEqual(5, up.level);
            CollectionAssert.AreEqual(new[] { "hat", "rifle" }, up.unlocks.Select(u => u.id).ToArray());
            CollectionAssert.AreEqual(new[] { "hat", "rifle" }, f.Sink.UnlockIds);

            f.Ledger.AddXp(f.Player, f.Save, 1, "Bonus", true);
            Assert.AreEqual(1, f.Sink.Of<RetentionLevelUpBroadcast>().Count());
            Assert.AreEqual(2, f.Sink.UnlockIds.Count);
        }

        [Test]
        public void AccountLevelFromZeroIsAnnouncedOnceAndInMatchOnlyAtTheEnd() {
            var f = new Fixture(new RetentionTuning { accountXpScale = 1f });
            // Mirrors RetentionService: account rank = account XP / 10; the rank before the first payout
            // is noted, a rank-up outside a match is announced at once, one inside a match when it ends.
            int Rank() => (int)(f.Sink.AccountXp / 10);
            var announced = new List<int>();
            var paidInMatch = new List<bool>();
            f.Sink.BeforePay = player => player.NoteAccountLevel(Rank());
            f.Sink.AfterPay = player => {
                paidInMatch.Add(player.InMatch);
                if (!player.InMatch && player.TryAnnounceAccountLevel(Rank())) announced.Add(Rank());
            };
            Assert.AreEqual(-1, f.Player.AnnouncedAccountLevel, "not known before the first payout");
            Assert.IsFalse(f.Player.TryAnnounceAccountLevel(3), "nothing is announced while the level is not known");

            f.Ledger.AddXp(f.Player, f.Save, 15, "Bonus", false);
            CollectionAssert.AreEqual(new[] { 1 }, announced, "a new player's first rank-up (0 to 1) is announced");
            f.Ledger.AddXp(f.Player, f.Save, 1, "Bonus", false);
            CollectionAssert.AreEqual(new[] { 1 }, announced, "same rank, no second line");

            f.Ledger.BeginMatch(f.Player, f.Save);
            paidInMatch.Clear();
            f.Ledger.AddXp(f.Player, f.Save, 10, "Kill", false);
            f.Ledger.FinishMatch(f.Player, f.Save, Day, Noon, "Victory!", new[] { new RewardLine("Win", 20) });
            Assert.IsTrue(paidInMatch.Count >= 2 && paidInMatch.All(x => x), "payouts inside the match and while it finishes are in the match");
            Assert.IsFalse(f.Player.InMatch);
            CollectionAssert.AreEqual(new[] { 1 }, announced, "nothing announced during the match");
            Assert.IsTrue(f.Player.TryAnnounceAccountLevel(Rank()), "the final rank is announced when the match ends");
            Assert.IsTrue(Rank() >= 4);
            Assert.IsFalse(f.Player.TryAnnounceAccountLevel(Rank()), "once");
        }

        [Test]
        public void JoiningNeverAnnouncesAnAccountLevel() {
            // As RetentionService does at join: the level now is the baseline, and level-ups from rewards
            // paid while the player loads in (goals already met, a daily claim) only move it.
            var player = new RetentionPlayer();
            player.NoteAccountLevel(0);
            player.QuietAccountLevelUntil(Noon.AddSeconds(15));
            Assert.IsFalse(player.TryAnnounceAccountLevel(1, Noon.AddSeconds(2)), "a level-up while joining is not announced");
            Assert.IsFalse(player.TryAnnounceAccountLevel(2, Noon.AddSeconds(5)));
            Assert.AreEqual(2, player.AnnouncedAccountLevel, "but it moves the baseline");
            Assert.IsFalse(player.TryAnnounceAccountLevel(2, Noon.AddMinutes(5)), "the same level later is not news");
            Assert.IsTrue(player.TryAnnounceAccountLevel(3, Noon.AddMinutes(5)), "a real level gain after joining is");
            Assert.IsFalse(player.TryAnnounceAccountLevel(3, Noon.AddMinutes(6)), "once");
        }

        [Test]
        public void ReportsFromARewardHandlerAreNotLost() {
            var f = new Fixture();
            ChallengeDefinition builds = f.Picked(0), bosses = f.Picked(2);
            // A game handler reports another stat while the first batch is being applied.
            f.Sink.AfterSend = message => {
                if (message is RetentionChallengeBroadcast { id: var id } && id == builds.id)
                    Assert.AreEqual(StatEffect.Completes, f.Ledger.Queue(f.Player, f.Save, Day, bosses.statKey, bosses.target, true));
            };
            f.Ledger.Queue(f.Player, f.Save, Day, builds.statKey, builds.target, true);
            f.Ledger.Flush(f.Player, f.Save, Day);
            Assert.IsTrue(f.Player.HasPendingStats, "the report made during the flush waits for the next one");
            f.Ledger.Flush(f.Player, f.Save, Day);
            Assert.AreEqual(2, f.Sink.Of<RetentionChallengeBroadcast>().Count());
            Assert.AreEqual(builds.goldReward + bosses.goldReward, f.Sink.Gold);
        }
    }
}
