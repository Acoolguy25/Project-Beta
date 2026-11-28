using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>
    /// Covers the cap on how far an ordinary wave may out-grow the one before it, the difficulty
    /// scaling on top of the waves, and the Endless top bar.
    /// </summary>
    public sealed class WV_WaveStepTests {
        static float Threat(WV_Wave wave) {
            float threat = 0f;
            foreach (WV_WaveGroup group in wave.Groups)
                threat += group.Count * WV_TroopCatalog.Get(group.Kind).Threat;
            return threat;
        }

        [Test]
        public void OrdinaryWaves_SpendAtMostTheStepOverTheWaveBefore() {
            var tuning = new WV_WaveTuning();
            for (int wave = 2; wave <= tuning.WaveCount; wave++) {
                if (WV_WavePlan.IsSurge(wave, tuning) || WV_WavePlan.IsBossWave(wave, tuning))
                    continue;
                float ceiling = WV_WavePlan.GetStrength(wave - 1, tuning) * (1f + tuning.MaxStepUp);
                float budget = WV_WavePlan.GetBudget(wave, tuning);
                Assert.That(budget, Is.LessThanOrEqualTo(System.Math.Max(ceiling, WV_WavePlan.GetStrength(wave, tuning)) + 1e-3f),
                    $"wave {wave}");
            }
        }

        [Test]
        public void WaveEight_NoLongerJumpsOverWaveSeven() {
            var tuning = new WV_WaveTuning();
            WV_Wave seven = WV_WavePlan.Build(7, tuning);
            WV_Wave eight = WV_WavePlan.Build(8, tuning);
            Assert.That(eight.Theme, Is.EqualTo(WV_WaveTheme.Swarm), "the second Swarm lands on wave 8 by default");
            // The cap is on the budget; rounding to whole soldiers (and one of each kind) adds a little.
            Assert.That(Threat(eight) / Threat(seven), Is.LessThanOrEqualTo(1f + tuning.MaxStepUp + 0.05f));
            var uncapped = new WV_WaveTuning { MaxStepUp = 1f };
            Assert.That(WV_WavePlan.Build(8, uncapped).TotalCount, Is.GreaterThan(eight.TotalCount),
                "the cap should field fewer bodies than the old wave 8 did");
        }

        [Test]
        public void SurgesAndBossWaves_AreNotCapped() {
            var tuning = new WV_WaveTuning();
            Assert.That(WV_WavePlan.IsSurge(10, tuning), Is.True);
            Assert.That(WV_WavePlan.GetBudget(10, tuning), Is.EqualTo(WV_WavePlan.GetStrength(10, tuning)).Within(1e-4f));
        }

        [Test]
        public void Scaling_RaisesStrengthAndHealth() {
            var tuning = new WV_WaveTuning();
            WV_Wave plain = WV_WavePlan.Build(12, tuning);
            WV_Wave hard = WV_WavePlan.Build(12, tuning, new WV_WaveScaling(1.5f, 1.3f));
            Assert.That(hard.HealthMultiplier, Is.EqualTo(plain.HealthMultiplier * 1.3f).Within(1e-4f));
            Assert.That(Threat(hard), Is.GreaterThan(Threat(plain)));
            Assert.That(WV_WavePlan.Build(12, tuning, WV_WaveScaling.None).TotalCount, Is.EqualTo(plain.TotalCount));
        }

        [Test]
        public void EndlessTopBar_HasNoLastWave() {
            var tuning = new WV_WaveTuning();
            WV_Wave wave = WV_WavePlan.Build(23, tuning);
            Assert.That(WV_WavePlan.DescribeTopBar(wave, 0), Does.StartWith("Endless wave 23"));
            Assert.That(WV_WavePlan.DescribeTopBar(WV_WavePlan.Build(1, tuning), tuning.WaveCount), Is.EqualTo("Wave 1/20"));
        }

        [Test]
        public void EndlessWaves_KeepGrowingPastTheStandardRound() {
            var tuning = new WV_WaveTuning();
            var difficulty = new WV_DifficultyTuning();
            float previous = 0f;
            for (int wave = tuning.WaveCount; wave <= tuning.WaveCount + 10; wave++) {
                float health = WV_Campaign.EndlessHealthMultiplier(wave, tuning.WaveCount, difficulty);
                Assert.That(health, Is.GreaterThan(previous), $"wave {wave}");
                previous = health;
            }
            Assert.That(WV_Campaign.EndlessHealthMultiplier(tuning.WaveCount, tuning.WaveCount, difficulty), Is.EqualTo(1f));
        }
    }
}
