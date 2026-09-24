using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>Covers what dying and demolishing cost a commander.</summary>
    public sealed class WV_PenaltyTests {
        [Test]
        public void DeathPenalty_IsAShareOfALargeBalance() {
            Assert.That(WV_Rules.GetDeathPenalty(2000), Is.EqualTo(300));
        }

        [Test]
        public void DeathPenalty_HasAFloor() {
            Assert.That(WV_Rules.GetDeathPenalty(200), Is.EqualTo(WV_Rules.DeathPenaltyMinimum));
        }

        [Test]
        public void DeathPenalty_NeverExceedsTheBalance() {
            Assert.That(WV_Rules.GetDeathPenalty(20), Is.EqualTo(20));
            Assert.That(WV_Rules.GetDeathPenalty(0), Is.EqualTo(0));
            Assert.That(WV_Rules.GetDeathPenalty(-5), Is.EqualTo(0));
        }

        [Test]
        public void Demolishing_RefundsLessForAFinishedBuildingThanForASite() {
            long finished = WV_Rules.GetDemolishRefund(1000, operational: true);
            long site = WV_Rules.GetDemolishRefund(1000, operational: false);

            Assert.That(finished, Is.EqualTo(500));
            Assert.That(site, Is.EqualTo(750));
            Assert.That(site, Is.GreaterThan(finished));
        }
    }
}
