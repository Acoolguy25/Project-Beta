using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>
    /// Covers the opening of a round: a longer build phase and smaller, slower first waves, so an
    /// undefended flag no longer falls under a minute into wave 1. Also covers the approach table
    /// the server spawns from and the client points its arrows at.
    /// </summary>
    public sealed class WV_EarlyWaveTests {
        /// <summary>How fast a knifeman runs at full speed, measured in play (about 14 m/s).</summary>
        const float KnifemanRunSpeed = 14f;

        /// <summary>Where the flag stands: the centre of the valley floor.</summary>
        static readonly Vector3 FlagPosition = new(750f, 51.6f, 750f);

        /// <summary>Before this pass the flag fell 55 s into wave 1 after a 55 s build phase.</summary>
        const float PreviousUndefendedSeconds = 110f;

        [Test]
        public void FirstWaves_AreSmallerThanTheCurve_AndCatchUpByWaveThree() {
            var tuning = new WV_WaveTuning();
            var plain = new WV_WaveTuning { EarlyWaves = 0 };
            Assert.That(WV_WavePlan.GetStrength(1, tuning), Is.LessThan(WV_WavePlan.GetStrength(1, plain)));
            Assert.That(WV_WavePlan.GetStrength(2, tuning), Is.LessThan(WV_WavePlan.GetStrength(2, plain)));
            Assert.That(WV_WavePlan.GetStrength(3, tuning), Is.EqualTo(WV_WavePlan.GetStrength(3, plain)));
            Assert.That(WV_WavePlan.Build(1, tuning).TotalCount, Is.LessThan(WV_WavePlan.Build(1, plain).TotalCount));
        }

        [Test]
        public void FirstWaves_MarchSlower_AndFullSpeedReturnsAfterTheOpening() {
            var tuning = new WV_WaveTuning();
            float first = WV_WavePlan.Build(1, tuning).MarchSpeedMultiplier;
            float second = WV_WavePlan.Build(2, tuning).MarchSpeedMultiplier;
            Assert.That(first, Is.EqualTo(tuning.FirstWaveSpeedScale).Within(1e-5f));
            Assert.That(second, Is.GreaterThan(first).And.LessThan(1f));
            for (int wave = tuning.EarlyWaves + 1; wave <= tuning.WaveCount; wave++)
                Assert.That(WV_WavePlan.Build(wave, tuning).MarchSpeedMultiplier, Is.EqualTo(1f), $"wave {wave}");
        }

        [Test]
        public void EarlyWaveEasing_CanBeTurnedOff() {
            var tuning = new WV_WaveTuning { EarlyWaves = 0 };
            Assert.That(WV_WavePlan.GetStrength(1, tuning), Is.EqualTo(tuning.BaseStrength));
            Assert.That(WV_WavePlan.Build(1, tuning).MarchSpeedMultiplier, Is.EqualTo(1f));
        }

        [TestCase(1, 0.5f)]
        [TestCase(2, 0.75f)]
        [TestCase(3, 1f)]
        [TestCase(10, 1f)]
        public void EarlyWaveScale_RisesEvenlyToTheFullCurve(int wave, float expected) {
            Assert.That(WV_WavePlan.GetEarlyWaveScale(wave, earlyWaves: 2, firstWaveScale: 0.5f),
                Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void UndefendedFlag_HoldsFarLongerIntoTheRound() {
            var tuning = new WV_WaveTuning();
            float nearest = float.MaxValue;
            for (int i = 0; i < WV_WavePlan.ApproachCount; i++) {
                Vector3 entrance = WV_WavePlan.GetEntrance((WV_Approach)i);
                nearest = Mathf.Min(nearest, Vector2.Distance(
                    new Vector2(entrance.x, entrance.z), new Vector2(FlagPosition.x, FlagPosition.z)));
            }
            float march = nearest / (KnifemanRunSpeed * WV_WavePlan.Build(1, tuning).MarchSpeedMultiplier);
            float firstContact = tuning.PreparationSeconds + march;
            // At least half as long again before the first knife reaches an undefended flag.
            Assert.That(firstContact, Is.GreaterThan(PreviousUndefendedSeconds * 1.5f));
            Assert.That(march, Is.GreaterThan(60f), "wave 1 should take over a minute to cross the valley");
        }

        [Test]
        public void Entrances_AreOnePerApproach_OnTheValleyEdge() {
            var seen = new HashSet<Vector3>();
            for (int i = 0; i < WV_WavePlan.ApproachCount; i++) {
                Vector3 entrance = WV_WavePlan.GetEntrance((WV_Approach)i);
                Assert.That(seen.Add(entrance), Is.True, ((WV_Approach)i).ToString());
                Assert.That(Vector3.Distance(entrance, FlagPosition), Is.GreaterThan(600f), ((WV_Approach)i).ToString());
            }
        }

        [Test]
        public void Approaches_SurviveTheWireMask() {
            var decoded = new List<WV_Approach>();
            for (int mask = 0; mask < 1 << WV_WavePlan.ApproachCount; mask++) {
                var approaches = new List<WV_Approach>();
                for (int i = 0; i < WV_WavePlan.ApproachCount; i++) {
                    if ((mask & (1 << i)) != 0) {
                        approaches.Add((WV_Approach)i);
                        approaches.Add((WV_Approach)i); // groups often share a side
                    }
                }
                byte encoded = WV_WavePlan.EncodeApproaches(approaches);
                Assert.That(encoded, Is.EqualTo(mask));
                WV_WavePlan.DecodeApproaches(encoded, decoded);
                Assert.That(decoded, Is.EquivalentTo(new HashSet<WV_Approach>(approaches)), $"mask {mask}");
            }
        }

        [Test]
        public void WaveAnnouncement_CarriesEveryApproachTheWaveUses() {
            var tuning = new WV_WaveTuning();
            var decoded = new List<WV_Approach>();
            for (int wave = 1; wave <= tuning.WaveCount; wave++) {
                WV_Wave built = WV_WavePlan.Build(wave, tuning);
                WV_WavePlan.DecodeApproaches(WV_WavePlan.EncodeApproaches(built.Approaches), decoded);
                Assert.That(decoded, Is.EquivalentTo(built.DistinctApproaches), $"wave {wave}");
            }
        }
    }
}
