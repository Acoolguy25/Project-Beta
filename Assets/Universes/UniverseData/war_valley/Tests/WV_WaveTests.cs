using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>Covers the difficulty curve of the waves and the troop kinds they are built from.</summary>
    public sealed class WV_WaveTests {
        static WV_WaveTuning Tuning() => new WV_WaveTuning { SurgeEvery = 0 };

        static int Count(WV_Wave wave, WV_TroopKind kind) {
            int count = 0;
            foreach (WV_WaveGroup group in wave.Groups) {
                if (group.Kind == kind)
                    count += group.Count;
            }
            return count;
        }

        [Test]
        public void Waves_GrowStrongerEveryWave() {
            WV_WaveTuning tuning = Tuning();
            for (int wave = 1; wave < tuning.WaveCount; wave++) {
                Assert.That(WV_WavePlan.GetStrength(wave + 1, tuning), Is.GreaterThan(WV_WavePlan.GetStrength(wave, tuning)));
                Assert.That(WV_WavePlan.Build(wave + 1, tuning).HealthMultiplier,
                    Is.GreaterThan(WV_WavePlan.Build(wave, tuning).HealthMultiplier));
            }
            Assert.That(WV_WavePlan.Build(15, tuning).TotalCount, Is.GreaterThan(WV_WavePlan.Build(1, tuning).TotalCount));
        }

        [Test]
        public void Waves_IntroduceEachTroopKindAtItsFirstWave() {
            WV_WaveTuning tuning = Tuning();
            foreach (WV_TroopProfile profile in WV_TroopCatalog.All) {
                if (profile.FirstWave > 1)
                    Assert.That(Count(WV_WavePlan.Build(profile.FirstWave - 1, tuning), profile.Kind), Is.Zero, profile.DisplayName);
                Assert.That(Count(WV_WavePlan.Build(profile.FirstWave, tuning), profile.Kind), Is.GreaterThan(0), profile.DisplayName);
            }
        }

        [Test]
        public void Waves_ShortenTheirGapsButNeverBelowTheMinimum() {
            WV_WaveTuning tuning = Tuning();
            int previous = int.MaxValue;
            for (int wave = 1; wave <= 60; wave++) {
                int gap = WV_WavePlan.Build(wave, tuning).IntermissionSeconds;
                Assert.That(gap, Is.LessThanOrEqualTo(previous));
                Assert.That(gap, Is.GreaterThanOrEqualTo(tuning.MinimumIntermissionSeconds));
                previous = gap;
            }
        }

        [Test]
        public void Waves_NeverExceedTheEnemyCap() {
            var tuning = new WV_WaveTuning { MaxEnemiesPerWave = 30 };
            Assert.That(WV_WavePlan.Build(80, tuning).TotalCount, Is.LessThanOrEqualTo(30));
        }

        [Test]
        public void SurgeWaves_AreStrongerThanTheWaveBefore() {
            var tuning = new WV_WaveTuning { SurgeEvery = 5, SurgeMultiplier = 1.5f };
            Assert.That(WV_WavePlan.Build(5, tuning).IsSurge, Is.True);
            Assert.That(WV_WavePlan.GetStrength(5, tuning), Is.GreaterThan(WV_WavePlan.GetStrength(4, tuning) * 1.3f));
        }

        [Test]
        public void TroopKinds_HaveTheirDescribedBuilds() {
            WV_TroopProfile knife = WV_TroopCatalog.Get(WV_TroopKind.Knife);
            WV_TroopProfile skinny = WV_TroopCatalog.Get(WV_TroopKind.SkinnyLegend);
            WV_TroopProfile punk = WV_TroopCatalog.Get(WV_TroopKind.Punk);
            WV_TroopProfile shrimp = WV_TroopCatalog.Get(WV_TroopKind.Shrimp);
            WV_TroopProfile speedy = WV_TroopCatalog.Get(WV_TroopKind.Speedy);

            Assert.That(skinny.Build.Proportions.x, Is.LessThan(1f));
            Assert.That(punk.Build.Proportions.x, Is.GreaterThan(1f));
            Assert.That(punk.Build.Proportions.y, Is.GreaterThan(1f));
            Assert.That(shrimp.Build.Proportions.y, Is.LessThan(1f));
            Assert.That(shrimp.Cost, Is.LessThan(knife.Cost));
            Assert.That(speedy.Build.SpeedMultiplier, Is.GreaterThan(1f));
            Assert.That(speedy.Build.HealthMultiplier, Is.LessThan(1f));
            Assert.That(speedy.Build.DamageMultiplier, Is.LessThan(1f));
        }

        [Test]
        public void TroopKinds_SurviveTheWireEncoding() {
            foreach (WV_TroopProfile profile in WV_TroopCatalog.All) {
                WV_ProductionItem decoded = WV_ProductionItem.Decode(WV_ProductionItem.Troop(profile.Kind).Encoded);
                Assert.That(decoded.IsTroop, Is.True, profile.DisplayName);
                Assert.That(decoded.TroopKind, Is.EqualTo(profile.Kind), profile.DisplayName);
            }
        }
    }
}
