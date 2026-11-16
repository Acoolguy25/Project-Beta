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

        [Test]
        public void OnlyHidersTauntAndOnlyWhileSeekersHunt() {
            float ready = HS_Rules.TauntCooldown;
            Assert.That(HS_Rules.CanTaunt(HS_Phase.Seeking, HS_Role.Hider, ready, 0), Is.True);
            Assert.That(HS_Rules.CanTaunt(HS_Phase.Hiding, HS_Role.Hider, ready, 0), Is.False);
            Assert.That(HS_Rules.CanTaunt(HS_Phase.Results, HS_Role.Hider, ready, 0), Is.False);
            Assert.That(HS_Rules.CanTaunt(HS_Phase.Seeking, HS_Role.Seeker, ready, 0), Is.False);
            Assert.That(HS_Rules.CanTaunt(HS_Phase.Seeking, HS_Role.Caught, ready, 0), Is.False);
            Assert.That(HS_Rules.CanTaunt(HS_Phase.Seeking, HS_Role.None, ready, 0), Is.False);
        }

        [Test]
        public void TauntsRespectCooldownAndAllowance() {
            Assert.That(HS_Rules.CanTaunt(HS_Phase.Seeking, HS_Role.Hider, HS_Rules.TauntCooldown - 0.01f, 0), Is.False);
            Assert.That(HS_Rules.CanTaunt(HS_Phase.Seeking, HS_Role.Hider, float.MaxValue, HS_Rules.TauntsPerRound - 1), Is.True);
            Assert.That(HS_Rules.CanTaunt(HS_Phase.Seeking, HS_Role.Hider, float.MaxValue, HS_Rules.TauntsPerRound), Is.False);
        }

        [Test]
        public void BraveTauntsPayMore() {
            Assert.That(HS_Rules.TauntReward(10f, 40f, 3, 10, out bool brave), Is.EqualTo(10));
            Assert.That(brave, Is.True);
            Assert.That(HS_Rules.TauntReward(40f, 40f, 3, 10, out brave), Is.EqualTo(10));
            Assert.That(HS_Rules.TauntReward(41f, 40f, 3, 10, out brave), Is.EqualTo(3));
            Assert.That(brave, Is.False);
            // No seeker at all.
            Assert.That(HS_Rules.TauntReward(float.MaxValue, 40f, 3, 10, out brave), Is.EqualTo(3));
        }

        [Test]
        public void ProximityRisesSmoothlyAsASeekerApproaches() {
            Assert.That(HS_Rules.Proximity(100f, 10f, 60f), Is.EqualTo(0f));
            Assert.That(HS_Rules.Proximity(60f, 10f, 60f), Is.EqualTo(0f));
            Assert.That(HS_Rules.Proximity(35f, 10f, 60f), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(HS_Rules.Proximity(10f, 10f, 60f), Is.EqualTo(1f));
            Assert.That(HS_Rules.Proximity(0f, 10f, 60f), Is.EqualTo(1f));
            Assert.That(HS_Rules.Proximity(float.MaxValue, 10f, 60f), Is.EqualTo(0f));
            float previous = 0f;
            for (float d = 60f; d >= 10f; d -= 1f) {
                float p = HS_Rules.Proximity(d, 10f, 60f);
                Assert.That(p, Is.GreaterThanOrEqualTo(previous));
                previous = p;
            }
        }

        [Test]
        public void ProximityHandlesADegenerateRange() {
            Assert.That(HS_Rules.Proximity(5f, 10f, 10f), Is.EqualTo(1f));
            Assert.That(HS_Rules.Proximity(15f, 10f, 10f), Is.EqualTo(0f));
        }
    }
}
