using NUnit.Framework;

namespace Universes.UniverseData.hide_and_seek.Tests {
    public sealed class HS_SaveDataTests {
        [Test]
        public void RecordTauntCountsBraveOnesSeparately() {
            var save = new HS_SaveData();
            save.RecordTaunt(false);
            save.RecordTaunt(true);
            save.RecordTaunt(true);
            Assert.That(save.taunts, Is.EqualTo(3));
            Assert.That(save.braveTaunts, Is.EqualTo(2));
        }

        [Test]
        public void RecordRoundCountsSurvivalsOnlyWhenSurvived() {
            var save = new HS_SaveData();
            save.RecordRound(true);
            save.RecordRound(false);
            Assert.That(save.roundsPlayed, Is.EqualTo(2));
            Assert.That(save.hidesSurvived, Is.EqualTo(1));
        }

        [Test]
        public void ValidateClampsNegativesAndBraveAboveTaunts() {
            var save = new HS_SaveData { roundsPlayed = -3, hidesSurvived = -1, tags = -7, taunts = 4, braveTaunts = 9 };
            save.Validate();
            Assert.That(save.roundsPlayed, Is.Zero);
            Assert.That(save.hidesSurvived, Is.Zero);
            Assert.That(save.tags, Is.Zero);
            Assert.That(save.braveTaunts, Is.EqualTo(4));

            save = new HS_SaveData { taunts = -2, braveTaunts = 1 };
            save.Validate();
            Assert.That(save.taunts, Is.Zero);
            Assert.That(save.braveTaunts, Is.Zero);
        }

        [Test]
        public void TauntRefusalExplainsEachReasonAndAllowsAReadyHider() {
            Assert.That(HS_Rules.TauntRefusal(HS_Phase.Seeking, HS_Role.Hider, HS_Rules.TauntCooldown, 0), Is.Null);
            Assert.That(HS_Rules.TauntRefusal(HS_Phase.Hiding, HS_Role.Hider, 99f, 0), Does.Contain("Only hiders"));
            Assert.That(HS_Rules.TauntRefusal(HS_Phase.Seeking, HS_Role.Seeker, 99f, 0), Does.Contain("Only hiders"));
            Assert.That(HS_Rules.TauntRefusal(HS_Phase.Seeking, HS_Role.Hider, 99f, HS_Rules.TauntsPerRound), Does.Contain("No taunts"));
            Assert.That(HS_Rules.TauntRefusal(HS_Phase.Seeking, HS_Role.Hider, 1f, 0), Does.Contain("recharging"));
            // Refusal and CanTaunt never disagree.
            foreach (HS_Phase phase in System.Enum.GetValues(typeof(HS_Phase)))
            foreach (HS_Role role in System.Enum.GetValues(typeof(HS_Role)))
            foreach (float since in new[] { 0f, 11.9f, 12f, 50f })
            foreach (int used in new[] { 0, 5, 6 })
                Assert.That(HS_Rules.TauntRefusal(phase, role, since, used) == null, Is.EqualTo(HS_Rules.CanTaunt(phase, role, since, used)));
        }
    }
}
