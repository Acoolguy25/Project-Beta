using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>
    /// Covers the flag's siege armor: it used to fall in two or three seconds once knifemen reached
    /// it, too fast for a commander to answer the alarm.
    /// </summary>
    public sealed class WV_FlagArmorTests {
        const long FlagHealth = 1000;
        const float Tick = 0.1f;

        /// <summary>Seconds a flag of <see cref="FlagHealth"/> lasts against <paramref name="damagePerSecond"/>.</summary>
        static float SecondsToFall(float damagePerSecond, WV_FlagArmorTuning tuning, float difficulty = 1f) {
            var armor = new WV_FlagArmor();
            long health = FlagHealth;
            float time = 0f;
            long hit = (long)(damagePerSecond * Tick);
            while (health > 0 && time < 600f) {
                health -= armor.Absorb(hit, time, FlagHealth, tuning, difficulty);
                time += Tick;
            }
            return time;
        }

        [Test]
        public void SingleHit_IsCutByTheArmor() {
            var tuning = new WV_FlagArmorTuning();
            var armor = new WV_FlagArmor();
            Assert.That(armor.Absorb(40, 0f, FlagHealth, tuning), Is.EqualTo(20));
        }

        [Test]
        public void ACrowdAtTheFlag_TakesAboutHalfAMinute_NotSeconds() {
            var tuning = new WV_FlagArmorTuning();
            // Fifteen knifemen hitting together: the crowd that used to fell the flag in 2-3 seconds.
            float seconds = SecondsToFall(400f, tuning);
            Assert.That(seconds, Is.GreaterThanOrEqualTo(tuning.MinimumSecondsFromFull() - 1f));
            Assert.That(seconds, Is.GreaterThan(20f));
        }

        [Test]
        public void ALoneAttacker_StillHurts() {
            var tuning = new WV_FlagArmorTuning();
            float seconds = SecondsToFall(25f, tuning);
            Assert.That(seconds, Is.LessThan(120f), "a single soldier left alone at the flag must still be a threat");
        }

        [Test]
        public void HarderTiers_BreakTheFlagFaster() {
            var tuning = new WV_FlagArmorTuning();
            Assert.That(SecondsToFall(400f, tuning, 1.4f), Is.LessThan(SecondsToFall(400f, tuning)));
        }

        [Test]
        public void Budget_RefillsWhileTheFlagIsLeftAlone() {
            var tuning = new WV_FlagArmorTuning();
            var armor = new WV_FlagArmor();
            long first = armor.Absorb(100000, 0f, FlagHealth, tuning);
            long drained = armor.Absorb(100000, 0.01f, FlagHealth, tuning);
            long refilled = armor.Absorb(100000, 10f, FlagHealth, tuning);
            Assert.That(first, Is.EqualTo((long)(FlagHealth * tuning.MaxLossPerSecondFraction * tuning.BurstSeconds)));
            Assert.That(drained, Is.LessThanOrEqualTo(1));
            Assert.That(refilled, Is.EqualTo(first));
        }

        [Test]
        public void Reset_ForgetsTheBudget() {
            var tuning = new WV_FlagArmorTuning();
            var armor = new WV_FlagArmor();
            long first = armor.Absorb(100000, 5f, FlagHealth, tuning);
            armor.Reset();
            Assert.That(armor.Absorb(100000, 5f, FlagHealth, tuning), Is.EqualTo(first));
        }
    }
}
