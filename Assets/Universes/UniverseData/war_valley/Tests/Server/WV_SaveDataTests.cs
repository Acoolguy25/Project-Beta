using NUnit.Framework;
using Universes.UniverseData.war_valley.Server;

namespace Universes.UniverseData.war_valley.Tests.Server {
    /// <summary>
    /// Covers the round statistics a commander's save keeps, and the clamps applied on load.
    /// <para>
    /// The save lives in the server assembly, so this assembly compiles only with UNITY_SERVER and the
    /// client project's test run never sees it. Run it as a second EditMode run in the server clone
    /// project: <c>-runTests -testPlatform EditMode -assemblyNames Universes.UniverseData.war_valley.Server.Tests</c>.
    /// </para>
    /// </summary>
    public sealed class WV_SaveDataTests {
        [Test]
        public void RecordRound_CountsTheRound_AndAddsItsDefeatedEnemies() {
            var save = new WV_SaveData();
            save.RecordRound(wavesHeld: 3, defeated: 20);
            save.RecordRound(wavesHeld: 1, defeated: 5);
            Assert.That(save.roundsPlayed, Is.EqualTo(2));
            Assert.That(save.enemiesDefeated, Is.EqualTo(25));
        }

        [Test]
        public void RecordRound_KeepsTheMostWavesHeld() {
            var save = new WV_SaveData();
            save.RecordRound(wavesHeld: 4, defeated: 0);
            save.RecordRound(wavesHeld: 2, defeated: 0);
            Assert.That(save.mostWavesHeld, Is.EqualTo(4));
        }

        [Test]
        public void RecordRound_IgnoresNegativeInputs() {
            var save = new WV_SaveData();
            save.RecordRound(wavesHeld: -3, defeated: -10);
            Assert.That(save.roundsPlayed, Is.EqualTo(1));
            Assert.That(save.mostWavesHeld, Is.Zero);
            Assert.That(save.enemiesDefeated, Is.Zero);
        }

        [Test]
        public void Validate_ClampsNegativeStatsFromADamagedFile() {
            var save = new WV_SaveData {
                bestWave = -1, victories = -2, roundsPlayed = -3, mostWavesHeld = -4, enemiesDefeated = -5
            };
            save.Validate();
            Assert.That(save.bestWave, Is.Zero);
            Assert.That(save.victories, Is.Zero);
            Assert.That(save.roundsPlayed, Is.Zero);
            Assert.That(save.mostWavesHeld, Is.Zero);
            Assert.That(save.enemiesDefeated, Is.Zero);
        }
    }
}
