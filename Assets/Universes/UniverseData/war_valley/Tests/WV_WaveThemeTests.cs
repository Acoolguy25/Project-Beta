using System.Collections.Generic;
using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>Covers wave themes, Warlord bosses, approaches, and bounties.</summary>
    public sealed class WV_WaveThemeTests {
        /// <summary>No surges and no periodic bosses, so only the rotation decides a theme.</summary>
        static WV_WaveTuning RotationOnly() => new WV_WaveTuning { SurgeEvery = 0, BossEvery = 0, WaveCount = 100 };

        static float Strength(WV_Wave wave, ICollection<WV_TroopKind> kinds, bool inside) {
            float strength = 0f;
            foreach (WV_WaveGroup group in wave.Groups) {
                if (kinds.Contains(group.Kind) == inside)
                    strength += group.Count * WV_TroopCatalog.Get(group.Kind).Threat;
            }
            return strength;
        }

        [Test]
        public void FirstWaves_AreStandardAndBossFree() {
            var tuning = new WV_WaveTuning();
            for (int wave = 1; wave <= 2; wave++) {
                WV_Wave built = WV_WavePlan.Build(wave, tuning);
                Assert.That(built.Theme, Is.EqualTo(WV_WaveTheme.Standard), $"wave {wave}");
                Assert.That(built.Boss, Is.Null, $"wave {wave}");
            }
        }

        [Test]
        public void BossWaves_AreSiegesWithAWarlord_AndTheFinalWaveAlwaysHasOne() {
            var tuning = new WV_WaveTuning { WaveCount = 25, BossEvery = 10 };
            foreach (int wave in new[] { 10, 20, 25 }) {
                WV_Wave built = WV_WavePlan.Build(wave, tuning);
                Assert.That(built.Boss, Is.Not.Null, $"wave {wave}");
                Assert.That(built.Theme, Is.EqualTo(WV_WaveTheme.Siege), $"wave {wave}");
                Assert.That(built.EnemyCount, Is.EqualTo(built.TotalCount + 1));
                Assert.That(built.Boss.DisplayName, Is.EqualTo(WV_WaveBoss.DefaultName));
                Assert.That(built.Boss.HealthMultiplier, Is.GreaterThan(1f));
                Assert.That(built.Boss.Build.Proportions.y,
                    Is.GreaterThan(WV_TroopCatalog.Get(built.Boss.Kind).Build.Proportions.y));
            }
            Assert.That(WV_WavePlan.Build(11, tuning).Boss, Is.Null);
        }

        [Test]
        public void SurgeWaves_ArePincersFromEverySide() {
            var tuning = new WV_WaveTuning { SurgeEvery = 5, BossEvery = 0, WaveCount = 100 };
            WV_Wave wave = WV_WavePlan.Build(15, tuning);
            Assert.That(wave.Theme, Is.EqualTo(WV_WaveTheme.Pincer));
            Assert.That(wave.Groups.Count, Is.GreaterThanOrEqualTo(WV_WavePlan.ApproachCount));
            Assert.That(wave.DistinctApproaches.Count, Is.EqualTo(WV_WavePlan.ApproachCount));
            Assert.That(WV_WavePlan.DescribeApproaches(wave), Is.EqualTo("every side"));
        }

        [Test]
        public void Themes_NeverRepeatBackToBack() {
            WV_WaveTuning tuning = RotationOnly();
            // Before the first themed wave every wave is a plain assault on purpose.
            for (int wave = tuning.FirstThemedWave + 1; wave <= 80; wave++)
                Assert.That(WV_WavePlan.GetTheme(wave, tuning), Is.Not.EqualTo(WV_WavePlan.GetTheme(wave - 1, tuning)),
                    $"wave {wave}");
        }

        [Test]
        public void EveryTheme_ShowsUpInAStandardRound() {
            var tuning = new WV_WaveTuning { WaveCount = 30 };
            var seen = new HashSet<WV_WaveTheme>();
            for (int wave = 1; wave <= tuning.WaveCount; wave++)
                seen.Add(WV_WavePlan.Build(wave, tuning).Theme);
            foreach (WV_WaveTheme theme in System.Enum.GetValues(typeof(WV_WaveTheme)))
                Assert.That(seen, Does.Contain(theme), theme.ToString());
        }

        [Test]
        public void Themes_WaitForTheirTroopsToUnlock() {
            WV_WaveTuning tuning = RotationOnly();
            for (int wave = 1; wave <= 40; wave++) {
                WV_WaveTheme theme = WV_WavePlan.GetTheme(wave, tuning);
                foreach (WV_TroopKind kind in WV_WavePlan.GetFeaturedKinds(theme))
                    Assert.That(WV_TroopCatalog.Get(kind).FirstWave, Is.LessThanOrEqualTo(wave), $"wave {wave} {theme}");
            }
        }

        [Test]
        public void ThemedWaves_SpendMostOfTheirStrengthOnFeaturedTroops() {
            WV_WaveTuning tuning = RotationOnly();
            int checkedWaves = 0;
            for (int wave = 12; wave <= 40; wave++) {
                WV_Wave built = WV_WavePlan.Build(wave, tuning);
                var featured = new HashSet<WV_TroopKind>(WV_WavePlan.GetFeaturedKinds(built.Theme));
                if (featured.Count == 0)
                    continue;
                checkedWaves++;
                Assert.That(Strength(built, featured, inside: true), Is.GreaterThan(Strength(built, featured, inside: false)),
                    $"wave {wave} {built.Theme}");
                // Every unlocked kind still shows up, so no defence is ever made pointless.
                foreach (WV_TroopProfile profile in WV_TroopCatalog.All) {
                    bool present = false;
                    foreach (WV_WaveGroup group in built.Groups)
                        present |= group.Kind == profile.Kind && group.Count > 0;
                    Assert.That(present, Is.True, $"wave {wave} lacks {profile.DisplayName}");
                }
            }
            Assert.That(checkedWaves, Is.GreaterThan(5));
        }

        [Test]
        public void SwarmWaves_FieldMoreBodies_AndBlitzStreamsFaster() {
            WV_WaveTuning tuning = RotationOnly();
            WV_Wave swarm = null;
            WV_Wave blitz = null;
            for (int wave = 6; wave <= 40 && (swarm == null || blitz == null); wave++) {
                WV_Wave built = WV_WavePlan.Build(wave, tuning);
                if (built.Theme == WV_WaveTheme.Swarm && swarm == null)
                    swarm = built;
                if (built.Theme == WV_WaveTheme.Blitz && blitz == null)
                    blitz = built;
            }
            Assert.That(swarm, Is.Not.Null);
            Assert.That(blitz, Is.Not.Null);
            Assert.That(blitz.SpawnSpacingMultiplier, Is.LessThan(1f));
            Assert.That(swarm.SpawnSpacingMultiplier, Is.LessThan(1f));

            float swarmThreatPerBody = 0f;
            foreach (WV_WaveGroup group in swarm.Groups)
                swarmThreatPerBody += group.Count * WV_TroopCatalog.Get(group.Kind).Threat;
            swarmThreatPerBody /= swarm.TotalCount;
            Assert.That(swarmThreatPerBody, Is.LessThan(1f), "a swarm is mostly cheap troops");
        }

        [Test]
        public void Approaches_AreParallelToGroups_AndSiegesComeFromOneSide() {
            var tuning = new WV_WaveTuning();
            for (int wave = 1; wave <= 30; wave++) {
                WV_Wave built = WV_WavePlan.Build(wave, tuning);
                Assert.That(built.Approaches.Count, Is.EqualTo(built.Groups.Count), $"wave {wave}");
                if (built.Theme == WV_WaveTheme.Siege)
                    Assert.That(built.DistinctApproaches.Count, Is.EqualTo(1), $"wave {wave}");
                else if (built.Groups.Count > 1)
                    Assert.That(built.DistinctApproaches.Count, Is.GreaterThan(1), $"wave {wave}");
            }
        }

        [Test]
        public void Approaches_MoveFromWaveToWave() {
            var firstSides = new HashSet<WV_Approach>();
            for (int wave = 1; wave <= 8; wave++)
                firstSides.Add(WV_WavePlan.AssignApproaches(wave, WV_WaveTheme.Siege, 1)[0]);
            Assert.That(firstSides.Count, Is.EqualTo(WV_WavePlan.ApproachCount));
        }

        [Test]
        public void Bounties_GrowWithTheWave() {
            var tuning = new WV_WaveTuning { WaveBountyBase = 100, WaveBountyPerWave = 20 };
            Assert.That(WV_WavePlan.GetWaveBounty(1, tuning), Is.EqualTo(100));
            Assert.That(WV_WavePlan.GetWaveBounty(6, tuning), Is.EqualTo(200));
            Assert.That(WV_WavePlan.GetWaveBounty(0, new WV_WaveTuning { WaveBountyBase = 0, WaveBountyPerWave = 0 }), Is.Zero);
        }

        [Test]
        public void TopBar_IsShort_AndNamesOnlyAThemedWave() {
            var tuning = new WV_WaveTuning { WaveCount = 20 };
            Assert.That(WV_WavePlan.DescribeTopBar(WV_WavePlan.Build(1, tuning), 20), Is.EqualTo("Wave 1/20"));
            string final = WV_WavePlan.DescribeTopBar(WV_WavePlan.Build(20, tuning), 20);
            Assert.That(final, Is.EqualTo("Wave 20/20 - Siege"));
        }

        [Test]
        public void BossWaves_StillRespectTheEnemyCap() {
            var tuning = new WV_WaveTuning { MaxEnemiesPerWave = 25, WaveCount = 60 };
            Assert.That(WV_WavePlan.Build(60, tuning).EnemyCount, Is.LessThanOrEqualTo(25));
        }
    }
}
