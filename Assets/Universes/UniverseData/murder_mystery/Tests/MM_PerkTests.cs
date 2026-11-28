using NUnit.Framework;

namespace Universes.UniverseData.murder_mystery.Tests {
    public sealed class MM_PerkTests {
        [Test]
        public void DefinitionsAreIndexedByPerk() {
            for (int i = 0; i < MM_Perks.All.Length; i++)
                Assert.That((int)MM_Perks.All[i].perk, Is.EqualTo(i), MM_Perks.All[i].title);
        }

        [Test]
        public void NewPlayerOnlyHasNoPerk() {
            Assert.That(MM_Perks.Step(MM_PerkSlot.Murderer, MM_Perk.None, 1, 0), Is.EqualTo(MM_Perk.None));
            Assert.That(MM_Perks.Count(MM_PerkSlot.Murderer, 0), Is.EqualTo((0, 3)));
            Assert.That(MM_Perks.NextLocked(MM_PerkSlot.Murderer, 0, out MM_Perks.Definition next), Is.True);
            Assert.That(next.perk, Is.EqualTo(MM_Perk.Haste));
        }

        [Test]
        public void StepCyclesUnlockedPerksOfTheSlotThroughNone() {
            int rounds = MM_Perks.Get(MM_Perk.Ninja).unlockRounds; // Haste and Ninja, not Sixth Sense yet
            Assert.That(MM_Perks.Step(MM_PerkSlot.Murderer, MM_Perk.None, 1, rounds), Is.EqualTo(MM_Perk.Haste));
            Assert.That(MM_Perks.Step(MM_PerkSlot.Murderer, MM_Perk.Haste, 1, rounds), Is.EqualTo(MM_Perk.Ninja));
            Assert.That(MM_Perks.Step(MM_PerkSlot.Murderer, MM_Perk.Ninja, 1, rounds), Is.EqualTo(MM_Perk.None));
            Assert.That(MM_Perks.Step(MM_PerkSlot.Murderer, MM_Perk.None, -1, rounds), Is.EqualTo(MM_Perk.Ninja));
            // A pick from the other slot is treated as None.
            Assert.That(MM_Perks.Step(MM_PerkSlot.Murderer, MM_Perk.Lucky, 1, rounds), Is.EqualTo(MM_Perk.Haste));
        }

        [Test]
        public void SanitizeDropsLockedAndWrongSlotPicks() {
            Assert.That(MM_Perks.Sanitize(MM_Perk.SixthSense, MM_PerkSlot.Murderer, 0), Is.EqualTo(MM_Perk.None));
            Assert.That(MM_Perks.Sanitize(MM_Perk.Lucky, MM_PerkSlot.Murderer, 100), Is.EqualTo(MM_Perk.None));
            Assert.That(MM_Perks.Sanitize(MM_Perk.Lucky, MM_PerkSlot.Innocent, 100), Is.EqualTo(MM_Perk.Lucky));
            Assert.That(MM_Perks.Sanitize((MM_Perk)200, MM_PerkSlot.Innocent, 100), Is.EqualTo(MM_Perk.None));
        }

        [Test]
        public void RoleDecidesWhichPerkApplies() {
            Assert.That(MM_Perks.ForRole(MM_Role.Murderer, MM_Perk.Haste, MM_Perk.Lucky), Is.EqualTo(MM_Perk.Haste));
            Assert.That(MM_Perks.ForRole(MM_Role.Innocent, MM_Perk.Haste, MM_Perk.Lucky), Is.EqualTo(MM_Perk.Lucky));
            Assert.That(MM_Perks.ForRole(MM_Role.Sheriff, MM_Perk.Haste, MM_Perk.Lucky), Is.EqualTo(MM_Perk.Lucky));
            Assert.That(MM_Perks.ForRole(MM_Role.Spectator, MM_Perk.Haste, MM_Perk.Lucky), Is.EqualTo(MM_Perk.None));
        }

        [Test]
        public void JustUnlockedFindsThePerkCrossedThisRound() {
            int lucky = MM_Perks.Get(MM_Perk.Lucky).unlockRounds;
            Assert.That(MM_Perks.JustUnlocked(lucky - 1, lucky, out MM_Perks.Definition unlocked), Is.True);
            Assert.That(unlocked.perk, Is.EqualTo(MM_Perk.Lucky));
            Assert.That(MM_Perks.JustUnlocked(lucky, lucky, out _), Is.False);
        }

        [Test]
        public void HeartbeatGrowsAsTheMurdererClosesIn() {
            Assert.That(MM_Perks.HeartbeatStrength(30f, 6f, 24f), Is.EqualTo(0f));
            Assert.That(MM_Perks.HeartbeatStrength(24f, 6f, 24f), Is.EqualTo(0f));
            Assert.That(MM_Perks.HeartbeatStrength(15f, 6f, 24f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(MM_Perks.HeartbeatStrength(3f, 6f, 24f), Is.EqualTo(1f));
        }

        [Test]
        public void TuningAppliesPerks() {
            MM_ClassicTuning tuning = new();
            Assert.That(tuning.BagSizeFor(MM_Perk.Lucky), Is.EqualTo(tuning.coinBagSize + tuning.luckyExtraBagCoins));
            Assert.That(tuning.BagSizeFor(MM_Perk.Heartbeat), Is.EqualTo(tuning.coinBagSize));
            Assert.That(tuning.ThrowCooldownFor(MM_Perk.Haste), Is.LessThan(tuning.ThrowCooldownFor(MM_Perk.None)));
            Assert.That(tuning.SmokeCooldownFor(MM_Perk.LightFeet), Is.LessThan(tuning.smokeCooldownSeconds));
            Assert.That(tuning.SmokeBoostFor(MM_Perk.LightFeet), Is.GreaterThan(tuning.smokeBoostSeconds));
        }
    }
}
