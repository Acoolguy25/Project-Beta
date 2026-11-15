using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Tests {
    public sealed class CTF_RulesTests {
        [Test]
        public void JoinersGoToTheSmallerThenLosingTeam() {
            Assert.That(CTF_Rules.PickTeam(3, 2, 0, 0), Is.EqualTo(CTF_Team.Blue));
            Assert.That(CTF_Rules.PickTeam(1, 2, 0, 0), Is.EqualTo(CTF_Team.Red));
            Assert.That(CTF_Rules.PickTeam(2, 2, 2, 0), Is.EqualTo(CTF_Team.Blue));
            Assert.That(CTF_Rules.PickTeam(2, 2, 0, 0), Is.EqualTo(CTF_Team.Red));
        }

        [Test]
        public void ReachIgnoresSmallHeightButNotFloors() {
            Assert.That(CTF_Rules.InReach(new Vector3(0f, 0f, 0f), new Vector3(2f, 1.5f, 0f), CTF_Rules.PickupRange), Is.True);
            Assert.That(CTF_Rules.InReach(new Vector3(0f, 0f, 0f), new Vector3(0f, 5f, 0f), CTF_Rules.PickupRange), Is.False);
            Assert.That(CTF_Rules.InReach(new Vector3(0f, 0f, 0f), new Vector3(3f, 0f, 0f), CTF_Rules.PickupRange), Is.False);
        }

        [Test]
        public void WinnerAndOtherTeam() {
            Assert.That(CTF_Rules.Winner(1, 1), Is.Null);
            Assert.That(CTF_Rules.Winner(3, 1), Is.EqualTo(CTF_Team.Red));
            Assert.That(CTF_Rules.Other(CTF_Team.Red), Is.EqualTo(CTF_Team.Blue));
        }
    }
}
