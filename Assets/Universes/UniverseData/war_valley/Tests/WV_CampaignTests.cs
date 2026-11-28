using System.Collections.Generic;
using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>Covers the difficulty tiers: what unlocks them, the stars a win earns and the next goal.</summary>
    public sealed class WV_CampaignTests {
        static List<int> Stars(params int[] perTier) => new(perTier);

        [Test]
        public void NewCommander_HasOnlyRecruit() {
            var none = new List<int>();
            Assert.That(WV_Campaign.IsUnlocked(none, WV_DifficultyTier.Recruit), Is.True);
            foreach (WV_DifficultyTier tier in WV_Campaign.Tiers) {
                if (tier != WV_DifficultyTier.Recruit)
                    Assert.That(WV_Campaign.IsUnlocked(none, tier), Is.False, tier.ToString());
            }
            Assert.That(WV_Campaign.UnlockedMask(none), Is.EqualTo(1));
        }

        [Test]
        public void WinningATier_UnlocksTheNext_AndVeteranUnlocksEndless() {
            List<int> recruitWon = Stars(1);
            Assert.That(WV_Campaign.IsUnlocked(recruitWon, WV_DifficultyTier.Veteran), Is.True);
            Assert.That(WV_Campaign.IsUnlocked(recruitWon, WV_DifficultyTier.Elite), Is.False);
            Assert.That(WV_Campaign.IsUnlocked(recruitWon, WV_DifficultyTier.Endless), Is.False);

            List<int> veteranWon = Stars(2, 1);
            Assert.That(WV_Campaign.IsUnlocked(veteranWon, WV_DifficultyTier.Elite), Is.True);
            Assert.That(WV_Campaign.IsUnlocked(veteranWon, WV_DifficultyTier.Endless), Is.True);
            Assert.That(WV_Campaign.IsUnlocked(veteranWon, WV_DifficultyTier.Nightmare), Is.False);
        }

        [TestCase(false, 1f, 0)]
        [TestCase(true, 0.9f, 3)]
        [TestCase(true, 0.75f, 3)]
        [TestCase(true, 0.5f, 2)]
        [TestCase(true, 0.1f, 1)]
        public void Stars_FollowHowCloseTheFlagCameToFalling(bool victory, float lowest, int expected) {
            Assert.That(WV_Campaign.Stars(victory, lowest, new WV_DifficultyTuning()), Is.EqualTo(expected));
        }

        [Test]
        public void HarderTiers_AreHarderAndPayMore() {
            var tuning = new WV_DifficultyTuning();
            WV_DifficultyTier[] ladder = { WV_DifficultyTier.Recruit, WV_DifficultyTier.Veteran, WV_DifficultyTier.Elite, WV_DifficultyTier.Nightmare };
            for (int i = 1; i < ladder.Length; i++) {
                WV_DifficultyProfile easier = tuning.Get(ladder[i - 1]);
                WV_DifficultyProfile harder = tuning.Get(ladder[i]);
                Assert.That(harder.StrengthMultiplier, Is.GreaterThan(easier.StrengthMultiplier), ladder[i].ToString());
                Assert.That(harder.HealthMultiplier, Is.GreaterThan(easier.HealthMultiplier), ladder[i].ToString());
                Assert.That(harder.XpMultiplier, Is.GreaterThan(easier.XpMultiplier), ladder[i].ToString());
            }
            Assert.That(tuning.Get(WV_DifficultyTier.Recruit).StrengthMultiplier, Is.EqualTo(1f), "Recruit is the tuned game");
        }

        [Test]
        public void NextGoal_WalksTheLadder() {
            var tuning = new WV_DifficultyTuning();
            Assert.That(WV_Campaign.NextTarget(Stars()), Is.EqualTo(WV_DifficultyTier.Recruit));
            Assert.That(WV_Campaign.DescribeNextGoal(Stars(), 0, tuning), Does.Contain("unlock Veteran"));
            Assert.That(WV_Campaign.NextTarget(Stars(1)), Is.EqualTo(WV_DifficultyTier.Veteran));
            Assert.That(WV_Campaign.DescribeNextGoal(Stars(1), 0, tuning), Does.Contain("Endless"));
            Assert.That(WV_Campaign.NextTarget(Stars(3, 3, 3, 3)), Is.EqualTo(WV_DifficultyTier.Endless));
            Assert.That(WV_Campaign.DescribeNextGoal(Stars(3, 3, 3, 3), 31, tuning), Does.Contain("31"));
            // Every tier won but not perfectly: chase the missing stars, easiest first.
            Assert.That(WV_Campaign.NextTarget(Stars(3, 2, 1, 1)), Is.EqualTo(WV_DifficultyTier.Veteran));
            Assert.That(WV_Campaign.DescribeNextGoal(Stars(3, 2, 1, 1), 0, tuning), Does.Contain("3-star Veteran"));
        }

        [Test]
        public void NoVotes_PicksTheEasiestTierSomeoneHasNotWon() {
            var offered = new List<WV_DifficultyTier> { WV_DifficultyTier.Recruit, WV_DifficultyTier.Veteran, WV_DifficultyTier.Elite };
            Assert.That(WV_Campaign.DefaultChoice(offered, Stars(1, 0, 0)), Is.EqualTo(WV_DifficultyTier.Veteran));
            Assert.That(WV_Campaign.DefaultChoice(offered, Stars(3, 3, 2)), Is.EqualTo(WV_DifficultyTier.Elite));
            Assert.That(WV_Campaign.DefaultChoice(new List<WV_DifficultyTier>(), Stars()), Is.EqualTo(WV_DifficultyTier.Recruit));
        }

        [Test]
        public void Vote_OffersEveryOpenTier_SoEveryCommandersNextGoalIsOnTheBallot() {
            // Every reachable campaign (a starred tier only has stars once the one before it was won).
            var campaigns = new List<List<int>>();
            for (int code = 0; code < 256; code++) {
                var stars = Stars(code & 3, (code >> 2) & 3, (code >> 4) & 3, (code >> 6) & 3, 0);
                bool reachable = true;
                for (int tier = 1; tier < 4; tier++)
                    reachable &= stars[tier] == 0 || stars[tier - 1] > 0;
                if (reachable)
                    campaigns.Add(stars);
            }
            Assert.That(campaigns.Count, Is.EqualTo(1 + 3 + 9 + 27 + 81 - 0), "1 + 3 + 3^2 + 3^3 + 3^4 reachable star sets");

            var tuning = new WV_DifficultyTuning();
            foreach (List<int> mine in campaigns) {
                // Alone: the ballot is every tier this commander has open, and it holds their goal.
                List<WV_DifficultyTier> open = Open(WV_Campaign.UnlockedMask(mine));
                List<WV_DifficultyTier> ballot = WV_Campaign.VoteOptions(open, mine);
                Assert.That(ballot, Is.EqualTo(open), $"stars {string.Join(",", mine)}");
                WV_DifficultyTier goal = WV_Campaign.NextTarget(mine);
                Assert.That(ballot, Does.Contain(goal), $"goal {goal} for stars {string.Join(",", mine)}");
                StringAssert.Contains(tuning.Get(goal).DisplayName, WV_Campaign.DescribeNextGoal(mine, 7, tuning));

                // With a newcomer: the ballot is the union, and still holds both goals.
                List<int> fresh = Stars(0, 0, 0, 0, 0);
                List<WV_DifficultyTier> union = Open((byte)(WV_Campaign.UnlockedMask(mine) | WV_Campaign.UnlockedMask(fresh)));
                var lowest = new List<int>();
                for (int tier = 0; tier < 5; tier++)
                    lowest.Add(System.Math.Min(mine[tier], fresh[tier]));
                List<WV_DifficultyTier> shared = WV_Campaign.VoteOptions(union, lowest);
                Assert.That(shared, Does.Contain(goal));
                Assert.That(shared, Does.Contain(WV_Campaign.NextTarget(fresh)));
            }
            Assert.That(WV_Campaign.VoteOptions(new List<WV_DifficultyTier>(), Stars()), Is.EqualTo(new[] { WV_DifficultyTier.Recruit }));
        }

        static List<WV_DifficultyTier> Open(byte mask) {
            var open = new List<WV_DifficultyTier>();
            foreach (WV_DifficultyTier tier in WV_Campaign.Tiers) {
                if (WV_Campaign.IsInMask(mask, tier))
                    open.Add(tier);
            }
            return open;
        }

        [Test]
        public void TierValues_AreStableOnTheWireAndInSaves() {
            Assert.That((byte)WV_DifficultyTier.Recruit, Is.EqualTo(0));
            Assert.That((byte)WV_DifficultyTier.Veteran, Is.EqualTo(1));
            Assert.That((byte)WV_DifficultyTier.Elite, Is.EqualTo(2));
            Assert.That((byte)WV_DifficultyTier.Nightmare, Is.EqualTo(3));
            Assert.That((byte)WV_DifficultyTier.Endless, Is.EqualTo(4));
            Assert.That(WV_Campaign.Tiers.Length, Is.EqualTo(5));
        }
    }
}
