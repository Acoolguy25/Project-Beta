using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Universes.UniverseData.murder_mystery.Tests {
    public sealed class MM_KillClockTests {
        const int Murderer = 7;
        const int OtherMurderer = 9;

        [Test]
        public void NobodyMayKillBeforeTheHuntStarts() {
            MM_KillClock clock = new(8f);
            Assert.That(clock.Running, Is.False);
            Assert.That(clock.CanKill(Murderer, 1000f), Is.False);
        }

        [Test]
        public void GracePeriodHoldsEveryMurdererBack() {
            MM_KillClock clock = new(8f);
            clock.BeginHunt(100f);
            Assert.That(clock.CanKill(Murderer, 100f), Is.False);
            Assert.That(clock.SecondsUntilReady(Murderer, 104f), Is.EqualTo(4f).Within(1e-4f));
            Assert.That(clock.CanKill(Murderer, 107.9f), Is.False);
            Assert.That(clock.CanKill(Murderer, 108f), Is.True);
            Assert.That(clock.CanKill(OtherMurderer, 108f), Is.True);
        }

        [Test]
        public void KillCooldownOnlyAppliesToTheKiller() {
            MM_KillClock clock = new(0f);
            clock.BeginHunt(0f);
            clock.RecordKill(Murderer, 30f, 9f);
            Assert.That(clock.CanKill(Murderer, 38.9f), Is.False);
            Assert.That(clock.CanKill(Murderer, 39f), Is.True);
            Assert.That(clock.CanKill(OtherMurderer, 31f), Is.True);
        }

        [Test]
        public void AShorterCooldownNeverCutsALongerOneShort() {
            MM_KillClock clock = new(0f);
            clock.BeginHunt(0f);
            clock.RecordKill(Murderer, 10f, 20f);
            clock.RecordKill(Murderer, 11f, 1f);
            Assert.That(clock.SecondsUntilReady(Murderer, 20f), Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void ReleaseAllEndsGraceAndCooldowns() {
            MM_KillClock clock = new(30f);
            clock.BeginHunt(0f);
            clock.RecordKill(Murderer, 5f, 100f);
            clock.ReleaseAll(10f);
            Assert.That(clock.CanKill(Murderer, 10f), Is.True);
            Assert.That(clock.CanKill(OtherMurderer, 10f), Is.True);
        }

        [Test]
        public void ReleaseAllBeforeTheHuntDoesNotStartIt() {
            MM_KillClock clock = new(5f);
            clock.ReleaseAll(10f);
            Assert.That(clock.Running, Is.False);
        }

        [Test]
        public void ANewHuntForgetsLastRoundsCooldowns() {
            MM_KillClock clock = new(5f);
            clock.BeginHunt(0f);
            clock.RecordKill(Murderer, 10f, 100f);
            clock.BeginHunt(200f);
            Assert.That(clock.CanKill(Murderer, 205f), Is.True);
        }

        [Test]
        public void ClearStopsAllKillsUntilTheNextHunt() {
            MM_KillClock clock = new(0f);
            clock.BeginHunt(0f);
            clock.Clear();
            Assert.That(clock.Running, Is.False);
            Assert.That(clock.CanKill(Murderer, 500f), Is.False);
        }

        [Test]
        public void NegativeTimesAreRejected() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MM_KillClock(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MM_KillClock(float.NaN));
            MM_KillClock clock = new(0f);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.RecordKill(Murderer, 0f, -1f));
        }

        [Test]
        public void DefaultTuningKeepsTheGraceShortAndPlayersFasterThanNpcs() {
            MM_ClassicTuning tuning = new();
            Assert.That(tuning.murdererGraceSeconds, Is.GreaterThan(0f).And.LessThan(tuning.huntSeconds * 0.1f));
            Assert.That(tuning.playerKillCooldownSeconds, Is.LessThan(tuning.npcKillCooldownSeconds));
        }
    }

    public sealed class MM_RoleOddsTests {
        [TestCase(1, 24, 0)]
        [TestCase(2, 24, 1)]
        [TestCase(18, 24, 1)]
        [TestCase(25, 24, 2)]
        [TestCase(3, 1, 2)]
        public void RoleCountGrowsWithTheCrowdButLeavesSomeoneInnocent(int characters, int perRole, int expected) =>
            Assert.That(MM_RoleOdds.RoleCount(characters, perRole), Is.EqualTo(expected));

        [Test]
        public void PlayersWeighMoreAndGainLuckUpToACap() {
            Assert.That(MM_RoleOdds.Weight(false, 50, 3f, 1.5f, 12), Is.EqualTo(MM_RoleOdds.NpcWeight));
            Assert.That(MM_RoleOdds.Weight(true, 0, 3f, 1.5f, 12), Is.EqualTo(3f));
            Assert.That(MM_RoleOdds.Weight(true, 2, 3f, 1.5f, 12), Is.EqualTo(6f));
            Assert.That(MM_RoleOdds.Weight(true, 100, 3f, 1.5f, 12), Is.EqualTo(3f + 12 * 1.5f));
        }

        [Test]
        public void ASmallLobbyKeepsItsShareOfTheBigRoles() {
            // One player at weight 3 among 17 NPCs: at least 40 % of the murderer draw.
            float npc = MM_RoleOdds.NpcWeightFor(players: 1, npcs: 17, playerWeight: 3f, playerShare: 0.4f);
            float[] weights = new float[18];
            weights[0] = 3f;
            for (int i = 1; i < weights.Length; i++) weights[i] = npc;
            Assert.That(MM_RoleOdds.MurdererChance(weights, 0, 1), Is.EqualTo(0.4f).Within(0.001f));
            // A full lobby (players already hold more than the share) keeps NPCs at their plain weight.
            Assert.That(MM_RoleOdds.NpcWeightFor(players: 12, npcs: 6, playerWeight: 3f, playerShare: 0.4f), Is.EqualTo(MM_RoleOdds.NpcWeight));
            Assert.That(MM_RoleOdds.NpcWeightFor(players: 1, npcs: 17, playerWeight: 3f, playerShare: 0f), Is.EqualTo(MM_RoleOdds.NpcWeight));
            Assert.That(MM_RoleOdds.NpcWeightFor(players: 0, npcs: 17, playerWeight: 3f, playerShare: 0.4f), Is.EqualTo(MM_RoleOdds.NpcWeight));
        }

        [Test]
        public void DealGivesExactlyTheRequestedRolesToDifferentCharacters() {
            float[] weights = { 1f, 1f, 1f, 1f, 1f };
            var rng = new Random(1);
            for (int round = 0; round < 200; round++) {
                MM_Role[] roles = MM_RoleOdds.Deal(weights, weights, 1, 1, () => (float)rng.NextDouble());
                Assert.That(Count(roles, MM_Role.Murderer), Is.EqualTo(1));
                Assert.That(Count(roles, MM_Role.Sheriff), Is.EqualTo(1));
                Assert.That(Count(roles, MM_Role.Innocent), Is.EqualTo(3));
            }
        }

        [Test]
        public void DealFollowsTheWeights() {
            // One player at weight 3 among nine NPCs at 1: murderer 3 / 12 = 25 % of the time.
            float[] weights = { 3f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
            var rng = new Random(7);
            int playerMurderer = 0;
            const int Rounds = 20000;
            for (int round = 0; round < Rounds; round++)
                if (MM_RoleOdds.Deal(weights, weights, 1, 1, () => (float)rng.NextDouble())[0] == MM_Role.Murderer)
                    playerMurderer++;
            Assert.That(playerMurderer / (float)Rounds, Is.EqualTo(MM_RoleOdds.MurdererChance(weights, 0, 1)).Within(0.015f));
            Assert.That(MM_RoleOdds.MurdererChance(weights, 0, 1), Is.EqualTo(0.25f).Within(1e-5f));
        }

        [Test]
        public void SheriffChanceMatchesTheDrawOrder() {
            float[] weights = { 3f, 1f, 1f, 1f, 1f, 1f };
            var rng = new Random(3);
            int playerSheriff = 0;
            const int Rounds = 20000;
            for (int round = 0; round < Rounds; round++)
                if (MM_RoleOdds.Deal(weights, weights, 1, 1, () => (float)rng.NextDouble())[0] == MM_Role.Sheriff)
                    playerSheriff++;
            Assert.That(playerSheriff / (float)Rounds, Is.EqualTo(MM_RoleOdds.SheriffChance(weights, weights, 0, 1, 1)).Within(0.015f));
        }

        [Test]
        public void DealNeverRunsOutOfCharacters() {
            MM_Role[] roles = MM_RoleOdds.Deal(new[] { 1f, 1f }, new[] { 1f, 1f }, 3, 3, () => 0.5f);
            Assert.That(Count(roles, MM_Role.Innocent), Is.EqualTo(0));
            Assert.That(Count(roles, MM_Role.Murderer), Is.EqualTo(2));
        }

        [Test]
        public void PercentNeverHidesARealChance() {
            Assert.That(MM_RoleOdds.Percent(0f), Is.EqualTo(0));
            Assert.That(MM_RoleOdds.Percent(0.001f), Is.EqualTo(1));
            Assert.That(MM_RoleOdds.Percent(0.254f), Is.EqualTo(25));
            Assert.That(MM_RoleOdds.Percent(1f), Is.EqualTo(100));
        }

        static int Count(IEnumerable<MM_Role> roles, MM_Role role) {
            int n = 0;
            foreach (MM_Role r in roles)
                if (r == role) n++;
            return n;
        }
    }

    public sealed class MM_RoundRulesTests {
        [Test]
        public void TheRoundGoesOnWhileBothSidesStand() =>
            Assert.That(MM_RoundRules.Decide(1, 5, timeUp: false), Is.EqualTo(MM_Outcome.None));

        [Test]
        public void StoppingTheMurdererWinsEvenIfTheLastInnocentFellToo() =>
            Assert.That(MM_RoundRules.Decide(0, 0, timeUp: false), Is.EqualTo(MM_Outcome.MurdererStopped));

        [Test]
        public void TheMurdererWinsWhenNobodyIsLeft() =>
            Assert.That(MM_RoundRules.Decide(1, 0, timeUp: true), Is.EqualTo(MM_Outcome.MurdererWon));

        [Test]
        public void SurvivingTheClockWinsForTheInnocents() =>
            Assert.That(MM_RoundRules.Decide(1, 1, timeUp: true), Is.EqualTo(MM_Outcome.InnocentsSurvived));

        [Test]
        public void WinnersAreTheRightSide() {
            Assert.That(MM_RoundRules.IsWinner(MM_Role.Murderer, MM_Outcome.MurdererWon), Is.True);
            Assert.That(MM_RoundRules.IsWinner(MM_Role.Innocent, MM_Outcome.MurdererWon), Is.False);
            Assert.That(MM_RoundRules.IsWinner(MM_Role.Sheriff, MM_Outcome.MurdererStopped), Is.True);
            Assert.That(MM_RoundRules.IsWinner(MM_Role.Innocent, MM_Outcome.InnocentsSurvived), Is.True);
            Assert.That(MM_RoundRules.IsWinner(MM_Role.Murderer, MM_Outcome.InnocentsSurvived), Is.False);
            Assert.That(MM_RoundRules.IsWinner(MM_Role.Spectator, MM_Outcome.InnocentsSurvived), Is.False);
        }

        [Test]
        public void OnlyASurvivedWinExtendsTheStreak() {
            Assert.That(MM_RoundRules.ExtendsStreak(won: true, survived: true), Is.True);
            Assert.That(MM_RoundRules.ExtendsStreak(won: true, survived: false), Is.False);
            Assert.That(MM_RoundRules.ExtendsStreak(won: false, survived: true), Is.False);
        }

        [Test]
        public void TheBagHoldsExactlyItsSize() {
            Assert.That(MM_RoundRules.BagHasRoom(39, 40), Is.True);
            Assert.That(MM_RoundRules.BagHasRoom(40, 40), Is.False);
        }

        [Test]
        public void TheResultsLineNamesWhoStoppedTheMurderer() {
            Assert.That(MM_RoundRules.Reason(MM_Outcome.MurdererStopped, "Ada"), Does.Contain("Ada"));
            Assert.That(MM_RoundRules.Reason(MM_Outcome.MurdererStopped, null), Is.Not.Empty);
            Assert.That(MM_RoundRules.Reason(MM_Outcome.MurdererWon, null), Does.Contain("Nobody"));
        }
    }

    public sealed class MM_DropRulesTests {
        [Test]
        public void OnlyTheSheriffDropsTheGun() {
            Assert.That(MM_DropRules.DropsGun(MM_Role.Sheriff), Is.True);
            Assert.That(MM_DropRules.DropsGun(MM_Role.Murderer), Is.False);
            Assert.That(MM_DropRules.DropsGun(MM_Role.Innocent), Is.False);
        }

        [Test]
        public void OnlyALivingEmptyHandedInnocentMayPickUp() {
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Innocent, alive: true, toolsHeld: 0), Is.True);
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Innocent, alive: false, toolsHeld: 0), Is.False);
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Innocent, alive: true, toolsHeld: 1), Is.False);
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Murderer, alive: true, toolsHeld: 0), Is.False);
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Sheriff, alive: true, toolsHeld: 0), Is.False);
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Spectator, alive: true, toolsHeld: 0), Is.False);
        }

        [Test]
        public void NpcsGoForTheGunOnlyAfterThePlayersHeadStart() {
            Assert.That(MM_DropRules.NpcMayGoForGun(secondsOnFloor: 3f, playerHeadStart: 10f), Is.False);
            Assert.That(MM_DropRules.NpcMayGoForGun(secondsOnFloor: 10f, playerHeadStart: 10f), Is.True);
            Assert.That(MM_DropRules.NpcMayGoForGun(secondsOnFloor: 0f, playerHeadStart: 0f), Is.True);
            Assert.That(MM_DropRules.NpcMayGoForGun(secondsOnFloor: 999f, playerHeadStart: -1f), Is.False, "a negative head start turns NPC heroes off");
        }

        [Test]
        public void ShootingAnyoneButAMurdererIsAMistake() {
            Assert.That(MM_DropRules.IsWrongShot(MM_Role.Sheriff, MM_Role.Innocent), Is.True);
            Assert.That(MM_DropRules.IsWrongShot(MM_Role.Sheriff, MM_Role.Sheriff), Is.True);
            Assert.That(MM_DropRules.IsWrongShot(MM_Role.Sheriff, MM_Role.Murderer), Is.False);
            Assert.That(MM_DropRules.IsWrongShot(MM_Role.Murderer, MM_Role.Innocent), Is.False);
        }

        [Test]
        public void MurderersNoticeTheSheriffFromFurtherLateInTheHunt() {
            MM_ClassicTuning tuning = new() {
                sheriffNoticeDistance = 30f, sheriffManhuntAfterSeconds = 70f, manhuntNoticeDistance = 200f, manhuntStaggerSeconds = 20f,
            };
            Assert.That(tuning.SheriffNoticeDistance(10f, 0), Is.EqualTo(30f));
            Assert.That(tuning.SheriffNoticeDistance(70f, 0), Is.EqualTo(200f));
            // The second murderer joins the manhunt one stagger later.
            Assert.That(tuning.SheriffNoticeDistance(80f, 1), Is.EqualTo(30f));
            Assert.That(tuning.SheriffNoticeDistance(90f, 1), Is.EqualTo(200f));
        }
    }

    public sealed class MM_KnifeSkinsTests {
        [Test]
        public void EveryoneStartsWithTheFirstSkin() {
            Assert.That(MM_KnifeSkins.All[0].coins, Is.EqualTo(0));
            Assert.That(MM_KnifeSkins.Unlocked(0), Is.EqualTo(0));
        }

        [Test]
        public void SkinsUnlockInOrderAtTheirPrice() {
            for (int i = 1; i < MM_KnifeSkins.All.Length; i++) {
                Assert.That(MM_KnifeSkins.All[i].coins, Is.GreaterThan(MM_KnifeSkins.All[i - 1].coins));
                Assert.That(MM_KnifeSkins.Unlocked(MM_KnifeSkins.All[i].coins - 1), Is.EqualTo(i - 1));
                Assert.That(MM_KnifeSkins.Unlocked(MM_KnifeSkins.All[i].coins), Is.EqualTo(i));
            }
        }

        [Test]
        public void NextPointsAtTheUpcomingSkinUntilAllAreUnlocked() {
            Assert.That(MM_KnifeSkins.Next(0), Is.EqualTo(1));
            Assert.That(MM_KnifeSkins.Next(int.MaxValue), Is.EqualTo(-1));
        }

        [Test]
        public void GetClampsOutOfRangeIndices() {
            Assert.That(MM_KnifeSkins.Get(-5).name, Is.EqualTo(MM_KnifeSkins.All[0].name));
            Assert.That(MM_KnifeSkins.Get(999).name, Is.EqualTo(MM_KnifeSkins.All[^1].name));
        }

        [Test]
        public void TheFirstSkinIsReachableInAFewRounds() {
            // A full bag a round should unlock the second skin within two rounds: an early, visible reward.
            MM_ClassicTuning tuning = new();
            Assert.That(MM_KnifeSkins.All[1].coins, Is.LessThanOrEqualTo(tuning.coinBagSize * 2));
        }
    }

    public sealed class MM_TextTests {
        [Test]
        public void TheHeroIsASheriffWithItsOwnName() {
            Assert.That(MM_Text.RoleName(MM_Role.Sheriff, hero: true), Is.EqualTo("Hero"));
            Assert.That(MM_Text.RoleName(MM_Role.Sheriff, hero: false), Is.EqualTo("Sheriff"));
            Assert.That(MM_Text.RoleName(MM_Role.Innocent, hero: true), Is.EqualTo("Innocent"));
        }

        [TestCase(0, "0:00")]
        [TestCase(65, "1:05")]
        [TestCase(-3, "0:00")]
        public void ClockFormatsMinutesAndSeconds(int seconds, string expected) =>
            Assert.That(MM_Text.Clock(seconds), Is.EqualTo(expected));
    }

    public sealed class MM_SmokeRulesTests {
        const int Cost = 6;

        [Test]
        public void ALivingInnocentWithCoinsMayThrow() =>
            Assert.That(MM_SmokeRules.Check(MM_Phase.Hunt, MM_Role.Innocent, false, true, Cost, Cost, 0f), Is.EqualTo(MM_SmokeCheck.Ok));

        [TestCase(MM_Role.Murderer, false)]
        [TestCase(MM_Role.Sheriff, false)]
        [TestCase(MM_Role.Innocent, true)] // the hero carries the gun instead
        [TestCase(MM_Role.Spectator, false)]
        public void OnlyPlainInnocentsCarrySmoke(MM_Role role, bool hero) =>
            Assert.That(MM_SmokeRules.Check(MM_Phase.Hunt, role, hero, true, 40, Cost, 0f), Is.EqualTo(MM_SmokeCheck.WrongRole));

        [Test]
        public void TheDeadCannotThrow() =>
            Assert.That(MM_SmokeRules.Check(MM_Phase.Hunt, MM_Role.Innocent, false, false, 40, Cost, 0f), Is.EqualTo(MM_SmokeCheck.WrongRole));

        [TestCase(MM_Phase.Lobby)]
        [TestCase(MM_Phase.RoleReveal)]
        [TestCase(MM_Phase.Results)]
        public void OnlyDuringTheHunt(MM_Phase phase) =>
            Assert.That(MM_SmokeRules.Check(phase, MM_Role.Innocent, false, true, 40, Cost, 0f), Is.EqualTo(MM_SmokeCheck.NotHunting));

        [Test]
        public void ABagShortOfThePriceIsRefused() =>
            Assert.That(MM_SmokeRules.Check(MM_Phase.Hunt, MM_Role.Innocent, false, true, Cost - 1, Cost, 0f), Is.EqualTo(MM_SmokeCheck.NotEnoughCoins));

        [Test]
        public void TheCooldownIsReportedBeforeThePrice() =>
            Assert.That(MM_SmokeRules.Check(MM_Phase.Hunt, MM_Role.Innocent, false, true, 0, Cost, 3f), Is.EqualTo(MM_SmokeCheck.Cooling));

        [Test]
        public void AFreeBombNeedsNoCoins() =>
            Assert.That(MM_SmokeRules.Check(MM_Phase.Hunt, MM_Role.Innocent, false, true, 0, 0, 0f), Is.EqualTo(MM_SmokeCheck.Ok));

        [Test]
        public void SpendingComesOffTheLifetimeTotal() =>
            Assert.That(MM_SmokeRules.LifetimeAfterSpending(100, Cost), Is.EqualTo(94));

        [Test]
        public void SpendingNeverLocksAnUnlockedSkin() {
            int crimson = MM_KnifeSkins.All[1].coins;
            Assert.That(MM_SmokeRules.LifetimeAfterSpending(crimson + 2, Cost), Is.EqualTo(crimson));
            Assert.That(MM_KnifeSkins.Unlocked(MM_SmokeRules.LifetimeAfterSpending(crimson, Cost)), Is.EqualTo(1));
        }

        [Test]
        public void ANewPlayerCannotGoBelowZero() =>
            Assert.That(MM_SmokeRules.LifetimeAfterSpending(3, Cost), Is.EqualTo(0));
    }

    public sealed class MM_BannerQueueTests {
        [Test]
        public void TheFirstBannerShowsAtOnce() {
            MM_BannerQueue<string> queue = new(2.5f, 6f);
            Assert.That(queue.Offer("a", 0f, false), Is.True);
        }

        [Test]
        public void ASecondBannerWaitsForTheFirstsMinimumTime() {
            MM_BannerQueue<string> queue = new(2.5f, 6f);
            queue.Offer("mistake", 10f, false);
            Assert.That(queue.Offer("players out", 11f, true), Is.False);
            Assert.That(queue.TryNext(12f, true, out _), Is.False);
            Assert.That(queue.TryNext(12.5f, true, out string next), Is.True);
            Assert.That(next, Is.EqualTo("players out"));
        }

        [Test]
        public void ABannerThatWaitedTooLongIsDropped() {
            MM_BannerQueue<string> queue = new(2.5f, 6f);
            queue.Offer("a", 0f, false);
            queue.Offer("stale", 1f, true);
            queue.Offer("fresh", 6f, true);
            Assert.That(queue.TryNext(7.5f, true, out string next), Is.True);
            Assert.That(next, Is.EqualTo("fresh"));
            Assert.That(queue.Waiting, Is.Zero);
        }

        [Test]
        public void WaitingBannersKeepTheirOrder() {
            MM_BannerQueue<string> queue = new(2.5f, 6f);
            queue.Offer("a", 0f, false);
            queue.Offer("b", 0.5f, true);
            // The banner has gone, but "c" still queues behind "b".
            Assert.That(queue.Offer("c", 1f, false), Is.False);
            Assert.That(queue.TryNext(1f, false, out string first), Is.True);
            Assert.That(first, Is.EqualTo("b"));
            Assert.That(queue.TryNext(3.6f, true, out string second), Is.True);
            Assert.That(second, Is.EqualTo("c"));
        }

        [Test]
        public void ClearForgetsWaitingBanners() {
            MM_BannerQueue<string> queue = new(2.5f, 6f);
            queue.Offer("a", 0f, false);
            queue.Offer("b", 1f, true);
            queue.Clear();
            Assert.That(queue.TryNext(10f, false, out _), Is.False);
        }
    }
}
