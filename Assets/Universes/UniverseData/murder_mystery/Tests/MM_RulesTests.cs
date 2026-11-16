using NUnit.Framework;

namespace Universes.UniverseData.murder_mystery.Tests {
    public sealed class MM_KillClockTests {
        const int Murderer = 7;
        const int OtherMurderer = 9;

        static MM_KillClock Clock(float grace = 25f, float cooldown = 20f, float meeting = 10f) => new(grace, cooldown, meeting);

        [Test]
        public void NobodyMayKillBeforeTheHuntStarts() {
            MM_KillClock clock = Clock();
            Assert.That(clock.Running, Is.False);
            Assert.That(clock.CanKill(Murderer, 1000f), Is.False);
        }

        [Test]
        public void GracePeriodHoldsEveryMurdererBack() {
            MM_KillClock clock = Clock(grace: 25f);
            clock.BeginHunt(100f);
            Assert.That(clock.CanKill(Murderer, 100f), Is.False);
            Assert.That(clock.SecondsUntilReady(Murderer, 110f), Is.EqualTo(15f).Within(1e-4f));
            Assert.That(clock.CanKill(Murderer, 124.9f), Is.False);
            Assert.That(clock.CanKill(Murderer, 125f), Is.True);
            Assert.That(clock.CanKill(OtherMurderer, 125f), Is.True);
        }

        [Test]
        public void KillCooldownOnlyAppliesToTheKiller() {
            MM_KillClock clock = Clock(grace: 0f, cooldown: 20f);
            clock.BeginHunt(0f);
            clock.RecordKill(Murderer, 30f);
            Assert.That(clock.CanKill(Murderer, 49f), Is.False);
            Assert.That(clock.CanKill(Murderer, 50f), Is.True);
            Assert.That(clock.CanKill(OtherMurderer, 31f), Is.True);
        }

        [Test]
        public void MeetingAddsASharedCooldownButNeverShortensOne() {
            MM_KillClock clock = Clock(grace: 0f, cooldown: 30f, meeting: 10f);
            clock.BeginHunt(0f);
            clock.RecordKill(Murderer, 50f);
            clock.EndMeeting(60f);
            Assert.That(clock.CanKill(OtherMurderer, 65f), Is.False);
            Assert.That(clock.CanKill(OtherMurderer, 70f), Is.True);
            // The killer's own cooldown (until 80) outlasts the meeting cooldown (until 70).
            Assert.That(clock.SecondsUntilReady(Murderer, 70f), Is.EqualTo(10f).Within(1e-4f));
        }

        [Test]
        public void MeetingDuringGraceDoesNotEndTheGraceEarly() {
            MM_KillClock clock = Clock(grace: 25f, meeting: 10f);
            clock.BeginHunt(0f);
            clock.EndMeeting(5f);
            Assert.That(clock.CanKill(Murderer, 20f), Is.False);
            Assert.That(clock.CanKill(Murderer, 25f), Is.True);
        }

        [Test]
        public void ANewHuntForgetsLastRoundsCooldowns() {
            MM_KillClock clock = Clock(grace: 5f, cooldown: 100f);
            clock.BeginHunt(0f);
            clock.RecordKill(Murderer, 10f);
            clock.BeginHunt(200f);
            Assert.That(clock.CanKill(Murderer, 205f), Is.True);
        }

        [Test]
        public void ClearStopsAllKillsUntilTheNextHunt() {
            MM_KillClock clock = Clock(grace: 0f);
            clock.BeginHunt(0f);
            clock.Clear();
            Assert.That(clock.Running, Is.False);
            Assert.That(clock.CanKill(Murderer, 500f), Is.False);
        }

        [Test]
        public void NegativeTuningIsRejected() {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Clock(grace: -1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Clock(cooldown: -1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Clock(meeting: float.NaN));
        }

        [Test]
        public void DefaultTuningGivesTheSheriffABreather() {
            MM_ClassicTuning tuning = new();
            MM_KillClock clock = new(tuning);
            Assert.That(clock.GraceSeconds, Is.GreaterThan(0f));
            Assert.That(clock.KillCooldownSeconds, Is.GreaterThan(0f));
            Assert.That(tuning.murdererGraceSeconds, Is.LessThan(tuning.huntSeconds));
        }
    }

    public sealed class MM_DropRulesTests {
        [Test]
        public void SheriffAlwaysDropsTheGun() {
            Assert.That(MM_DropRules.WeaponLeftBy(MM_Role.Sheriff, dropDaggers: false, ejected: false), Is.EqualTo(MM_DroppedWeapon.Gun));
            Assert.That(MM_DropRules.WeaponLeftBy(MM_Role.Sheriff, dropDaggers: true, ejected: false), Is.EqualTo(MM_DroppedWeapon.Gun));
        }

        [Test]
        public void AnEjectedSheriffStillLeavesTheGunForTheTown() =>
            Assert.That(MM_DropRules.WeaponLeftBy(MM_Role.Sheriff, dropDaggers: false, ejected: true), Is.EqualTo(MM_DroppedWeapon.Gun));

        [Test]
        public void DaggerDropsOnlyWhenEnabled() {
            Assert.That(MM_DropRules.WeaponLeftBy(MM_Role.Murderer, dropDaggers: true, ejected: false), Is.EqualTo(MM_DroppedWeapon.Dagger));
            Assert.That(MM_DropRules.WeaponLeftBy(MM_Role.Murderer, dropDaggers: false, ejected: false), Is.EqualTo(MM_DroppedWeapon.None));
        }

        [Test]
        public void AVotedOutMurdererNeverLeavesADagger() {
            Assert.That(MM_DropRules.WeaponLeftBy(MM_Role.Murderer, dropDaggers: true, ejected: true), Is.EqualTo(MM_DroppedWeapon.None));
            Assert.That(MM_DropRules.WeaponLeftBy(MM_Role.Murderer, dropDaggers: false, ejected: true), Is.EqualTo(MM_DroppedWeapon.None));
        }

        [Test]
        public void DaggersStayWithTheDeadByDefault() =>
            Assert.That(new MM_ClassicTuning().dropDaggerOnMurdererDeath, Is.False);

        [TestCase(MM_Role.Innocent)]
        [TestCase(MM_Role.Spectator)]
        [TestCase(MM_Role.None)]
        public void UnarmedRolesDropNothing(MM_Role role) =>
            Assert.That(MM_DropRules.WeaponLeftBy(role, dropDaggers: true, ejected: false), Is.EqualTo(MM_DroppedWeapon.None));

        [Test]
        public void OnlyALivingEmptyHandedInnocentMayPickUp() {
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Innocent, alive: true, toolsHeld: 0, isPlayer: true), Is.True);
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Innocent, alive: false, toolsHeld: 0, isPlayer: true), Is.False);
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Innocent, alive: true, toolsHeld: 1, isPlayer: true), Is.False);
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Murderer, alive: true, toolsHeld: 0, isPlayer: true), Is.False);
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Sheriff, alive: true, toolsHeld: 0, isPlayer: true), Is.False);
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Spectator, alive: true, toolsHeld: 0, isPlayer: true), Is.False);
        }

        [Test]
        public void NpcsNeverPickUpAWeapon() =>
            Assert.That(MM_DropRules.CanPickUp(MM_Role.Innocent, alive: true, toolsHeld: 0, isPlayer: false), Is.False);

        [Test]
        public void TheGunMakesASheriffAndTheDaggerAMurderer() {
            Assert.That(MM_DropRules.RoleFor(MM_DroppedWeapon.Gun), Is.EqualTo(MM_Role.Sheriff));
            Assert.That(MM_DropRules.RoleFor(MM_DroppedWeapon.Dagger), Is.EqualTo(MM_Role.Murderer));
            Assert.That(MM_DropRules.RoleFor(MM_DroppedWeapon.None), Is.EqualTo(MM_Role.None));
        }
    }
}
