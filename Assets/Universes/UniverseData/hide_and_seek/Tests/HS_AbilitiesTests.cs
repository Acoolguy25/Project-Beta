using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Tests {
    public sealed class HS_AbilitiesTests {
        static readonly HS_Ability[] All = { HS_Ability.Dash, HS_Ability.Decoy, HS_Ability.Blend, HS_Ability.Rush, HS_Ability.Radar, HS_Ability.Flashlight };

        [Test]
        public void EachSideHasOneAbilityPerSlot() {
            foreach (bool seeker in new[] { false, true }) {
                var seen = new HashSet<HS_Ability>();
                for (int slot = 0; slot < HS_Abilities.SlotsPerSide; slot++) {
                    HS_Ability ability = HS_Abilities.InSlot(seeker, slot);
                    Assert.That(ability, Is.Not.EqualTo(HS_Ability.None), $"seeker={seeker} slot={slot}");
                    Assert.That(HS_Abilities.Info(ability).Seeker, Is.EqualTo(seeker));
                    Assert.That(seen.Add(ability), Is.True);
                }
            }
            Assert.That(HS_Abilities.Count, Is.EqualTo(All.Length + 1));
        }

        [Test]
        public void UnlocksAndUpgradesExistAndUpgradesComeLater() {
            foreach (HS_Ability ability in All) {
                HS_AbilityInfo info = HS_Abilities.Info(ability);
                int unlock = HS_Retention.UnlockLevel(info.UnlockId), upgrade = HS_Retention.UnlockLevel(info.UpgradeId);
                Assert.That(unlock, Is.GreaterThanOrEqualTo(1), ability.ToString());
                Assert.That(upgrade, Is.GreaterThan(unlock), ability.ToString());
            }
            // Each side starts with its first key, so a new player always has something to press.
            Assert.That(HS_Retention.UnlockLevel(HS_Abilities.Info(HS_Abilities.InSlot(false, 0)).UnlockId), Is.EqualTo(1));
            Assert.That(HS_Retention.UnlockLevel(HS_Abilities.Info(HS_Abilities.InSlot(true, 0)).UnlockId), Is.EqualTo(1));
        }

        [Test]
        public void UnknownAbilitiesAreInvalid() {
            Assert.That(HS_Abilities.IsKnown(HS_Ability.None), Is.False);
            Assert.That(HS_Abilities.IsKnown((HS_Ability)200), Is.False);
            Assert.That(HS_Abilities.Refusal(HS_Phase.Seeking, HS_Role.Hider, (HS_Ability)200, true, 0f), Is.Not.Null);
        }

        [Test]
        public void RefusalFollowsSidePhaseUnlockAndCooldown() {
            Assert.That(HS_Abilities.Refusal(HS_Phase.Seeking, HS_Role.Hider, HS_Ability.Dash, true, 0f), Is.Null);
            Assert.That(HS_Abilities.Refusal(HS_Phase.Hiding, HS_Role.Hider, HS_Ability.Dash, true, 0f), Is.Null, "hiders may dash to a spot");
            Assert.That(HS_Abilities.Refusal(HS_Phase.Hiding, HS_Role.Seeker, HS_Ability.Rush, true, 0f), Is.Not.Null, "blindfolded seekers wait");
            Assert.That(HS_Abilities.Refusal(HS_Phase.Seeking, HS_Role.Seeker, HS_Ability.Rush, true, 0f), Is.Null);
            Assert.That(HS_Abilities.Refusal(HS_Phase.Seeking, HS_Role.Caught, HS_Ability.Radar, true, 0f), Is.Null, "found hiders seek");
            Assert.That(HS_Abilities.Refusal(HS_Phase.Seeking, HS_Role.Caught, HS_Ability.Dash, true, 0f), Is.Not.Null);
            Assert.That(HS_Abilities.Refusal(HS_Phase.Seeking, HS_Role.Hider, HS_Ability.Rush, true, 0f), Is.Not.Null);
            Assert.That(HS_Abilities.Refusal(HS_Phase.Seeking, HS_Role.None, HS_Ability.Dash, true, 0f), Is.Not.Null);
            Assert.That(HS_Abilities.Refusal(HS_Phase.Results, HS_Role.Hider, HS_Ability.Dash, true, 0f), Is.Not.Null);
            Assert.That(HS_Abilities.Refusal(HS_Phase.Seeking, HS_Role.Hider, HS_Ability.Blend, false, 0f), Does.Contain("locked"));
            Assert.That(HS_Abilities.Refusal(HS_Phase.Seeking, HS_Role.Hider, HS_Ability.Blend, true, 0.5f), Does.Contain("recharging"));
        }

        [Test]
        public void ConeChecksRangeAndAngle() {
            Vector3 origin = Vector3.zero, forward = Vector3.forward;
            Assert.That(HS_Abilities.InCone(origin, forward, new Vector3(0f, 0f, 10f), 20f, 40f), Is.True);
            Assert.That(HS_Abilities.InCone(origin, forward, new Vector3(0f, 0f, 30f), 20f, 40f), Is.False, "out of range");
            Assert.That(HS_Abilities.InCone(origin, forward, new Vector3(10f, 0f, 10f), 20f, 40f), Is.False, "45 degrees off a 40-degree beam");
            Assert.That(HS_Abilities.InCone(origin, forward, new Vector3(3f, 0f, 10f), 20f, 40f), Is.True, "about 17 degrees off");
            Assert.That(HS_Abilities.InCone(origin, forward, new Vector3(0f, 0f, -5f), 20f, 40f), Is.False, "behind");
        }

        [Test]
        public void SafeDirectionRejectsZeroAndNaN() {
            Assert.That(HS_Abilities.SafeDirection(Vector3.zero, Vector3.right), Is.EqualTo(Vector3.right));
            Assert.That(HS_Abilities.SafeDirection(new Vector3(float.NaN, 0f, 1f), Vector3.left), Is.EqualTo(Vector3.left));
            Assert.That(HS_Abilities.SafeDirection(new Vector3(0f, 0f, 9f), Vector3.right), Is.EqualTo(Vector3.forward));
            Assert.That(HS_Abilities.SafeDirection(Vector3.zero, Vector3.zero), Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void DefaultTuningIsSaneAndUpgradesAreBetter() {
            var tuning = new HS_AbilityTuning();
            tuning.Validate();
            foreach (HS_Ability ability in All) {
                HS_AbilityStats basic = tuning.Stats(ability, false), upgraded = tuning.Stats(ability, true);
                Assert.That(upgraded.cooldown, Is.LessThanOrEqualTo(basic.cooldown), ability.ToString());
                Assert.That(upgraded.duration, Is.GreaterThanOrEqualTo(basic.duration), ability.ToString());
                Assert.That(upgraded.power, Is.GreaterThanOrEqualTo(basic.power), ability.ToString());
                Assert.That(basic.cooldown, Is.GreaterThan(basic.duration), $"{ability} cannot be kept up forever");
            }
            Assert.That(tuning.Cooldown(HS_Ability.Dash, false, true), Is.LessThan(tuning.Cooldown(HS_Ability.Dash, false, false)));
        }

        [Test]
        public void ValidateRepairsBrokenTuning() {
            var tuning = new HS_AbilityTuning { dash = null, flashlightConeDegrees = 500f, dazzledSpeedScale = -1f, quickRecoveryCooldownScale = 0f };
            tuning.radar.basic = new HS_AbilityStats(-5f, float.NaN, -2f);
            tuning.Validate();
            Assert.That(tuning.dash, Is.Not.Null);
            Assert.That(tuning.Stats(HS_Ability.Dash, false).cooldown, Is.GreaterThanOrEqualTo(1f));
            Assert.That(tuning.radar.basic.cooldown, Is.EqualTo(1f));
            Assert.That(tuning.radar.basic.duration, Is.Zero);
            Assert.That(tuning.radar.basic.power, Is.Zero);
            Assert.That(tuning.flashlightConeDegrees, Is.EqualTo(120f));
            Assert.That(tuning.dazzledSpeedScale, Is.EqualTo(0.1f));
            Assert.That(tuning.quickRecoveryCooldownScale, Is.EqualTo(0.3f));
        }
    }
}
