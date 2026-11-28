using System.Collections.Generic;
using NUnit.Framework;
using RyanAssets.Retention;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>Covers War Valley's level track and challenge pool for the retention kit.</summary>
    public sealed class WV_RetentionTests {
        [Test]
        public void Rules_BuildWithoutErrors() {
            RetentionRules rules = WV_Retention.Rules(new RetentionTuning());
            Assert.That(rules.UniverseId, Is.EqualTo(WV_Retention.WV_SaveUniverse));
            Assert.That(rules.ChallengePool.Count, Is.EqualTo(WV_Retention.ChallengePool.Length));
        }

        [Test]
        public void UnlockTable_HasSixToTenUnlocks_OnRisingLevels() {
            Assert.That(WV_Retention.Unlocks.Length, Is.InRange(6, 10));
            var ids = new HashSet<string>();
            int previous = 1;
            foreach (UnlockDefinition unlock in WV_Retention.Unlocks) {
                Assert.That(unlock.level, Is.GreaterThan(previous), unlock.id);
                Assert.That(ids.Add(unlock.id), Is.True, $"{unlock.id} is listed twice");
                Assert.That(unlock.title, Is.Not.Empty);
                Assert.That(unlock.description, Is.Not.Empty);
                Assert.That(unlock.icon, Is.Not.Empty);
                previous = unlock.level;
            }
        }

        [Test]
        public void ChallengePool_HasEightOrMore_WithDistinctStats() {
            Assert.That(WV_Retention.ChallengePool.Length, Is.GreaterThanOrEqualTo(8));
            var ids = new HashSet<string>();
            var stats = new HashSet<string>();
            foreach (ChallengeDefinition challenge in WV_Retention.ChallengePool) {
                Assert.That(ids.Add(challenge.id), Is.True, challenge.id);
                Assert.That(stats.Add(challenge.statKey), Is.True, $"{challenge.statKey} is counted by two challenges");
                Assert.That(challenge.target, Is.GreaterThanOrEqualTo(1));
                Assert.That(challenge.xpReward, Is.GreaterThan(0));
                Assert.That(challenge.text, Is.Not.Empty);
            }
        }

        [Test]
        public void EveryUnlock_IsFoundByLevel() {
            foreach (UnlockDefinition unlock in WV_Retention.Unlocks)
                Assert.That(WV_Retention.LevelOf(unlock.id), Is.EqualTo(unlock.level));
            Assert.That(WV_Retention.LevelOf("no_such_unlock"), Is.Zero);
        }

        [Test]
        public void EveryDailyChallenge_CanBeDoneOnRecruit() {
            foreach (ChallengeDefinition challenge in WV_Retention.ChallengePool) {
                Assert.That(challenge.statKey, Is.Not.EqualTo(WV_Retention.VeteranWinStat), challenge.id);
                Assert.That(challenge.statKey, Is.Not.EqualTo(WV_Retention.EndlessStat), challenge.id);
            }
        }

        [Test]
        public void EndlessChallenge_IsReachableAfterTheStandardRound() {
            var waves = new WV_WaveTuning();
            foreach (ChallengeDefinition challenge in WV_Retention.ChallengePool) {
                if (challenge.statKey == WV_Retention.EndlessStat)
                    Assert.That(challenge.target, Is.GreaterThan(waves.WaveCount));
                if (challenge.statKey == WV_Retention.WaveStat)
                    Assert.That(challenge.target, Is.LessThanOrEqualTo(waves.WaveCount));
                if (challenge.statKey == WV_Retention.StarsStat)
                    Assert.That(challenge.target, Is.LessThanOrEqualTo(WV_Campaign.MaxStars));
            }
        }
    }
}
