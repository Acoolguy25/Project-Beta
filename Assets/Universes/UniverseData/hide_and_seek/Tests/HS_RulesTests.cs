using NUnit.Framework;
using RyanAssets.Rounds;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Tests {
    public sealed class HS_RulesTests {
        [Test]
        public void BlurStaysInsideTheScatterRadiusAndKeepsHeight() {
            var random = new System.Random(5);
            for (int i = 0; i < 500; i++) {
                Vector3 p = HS_Rules.Blur(new Vector3(10f, 4f, -3f), random);
                Assert.That(p.y, Is.EqualTo(4f));
                Assert.That(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(10f, -3f)), Is.LessThanOrEqualTo(HS_Rules.PingScatter + 1e-4f));
            }
        }

        [TestCase(2, 1)]
        [TestCase(4, 1)]
        [TestCase(5, 1)]
        [TestCase(8, 2)]
        [TestCase(12, 3)]
        public void SeekerCountScalesWithPlayers(int players, int seekers) =>
            Assert.That(TeamSplit.MinorityCount(players, HS_Rules.PlayersPerSeeker), Is.EqualTo(seekers));

        [Test]
        public void TagRangeIsCloseButReachable() => Assert.That(HS_Rules.TagRange, Is.InRange(2.5f, 5f));
    }
}
