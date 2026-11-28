using System;
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
        public void DroppedFlagReturnsAfterAHoldAndFallsBackWhenLeft() {
            float hold = CTF_Rules.DefaultReturnHoldSeconds;
            float p = 0f;
            for (int i = 0; i < 10; i++) p = CTF_Rules.ReturnStep(p, 1, hold / 20f, hold);
            Assert.That(p, Is.EqualTo(0.5f).Within(1e-4f), "one defender: half way after half the hold");
            Assert.That(CTF_Rules.ReturnStep(p, 0, hold / 4f, hold), Is.EqualTo(0.25f).Within(1e-4f), "nobody on it: falls back at the same rate");
            Assert.That(CTF_Rules.ReturnStep(0f, 3, hold / 2f, hold), Is.EqualTo(1f), "three defenders are twice as fast");
            Assert.That(CTF_Rules.ReturnStep(0f, 0, 1f, hold), Is.EqualTo(0f), "never below zero");
            Assert.That(CTF_Rules.ReturnStep(0f, 1, 0.01f, 0f), Is.EqualTo(1f), "hold 0 = return on touch");
        }

        [Test]
        public void WinnerAndOtherTeam() {
            Assert.That(CTF_Rules.Winner(1, 1), Is.Null);
            Assert.That(CTF_Rules.Winner(3, 1), Is.EqualTo(CTF_Team.Red));
            Assert.That(CTF_Rules.Other(CTF_Team.Red), Is.EqualTo(CTF_Team.Blue));
        }

        [Test]
        public void PowerUpMaskTracksEachSpotOnItsOwn() {
            uint mask = 0;
            mask = CTF_Rules.WithReady(mask, 0, true);
            mask = CTF_Rules.WithReady(mask, 2, true);
            Assert.That(CTF_Rules.IsReady(mask, 0), Is.True);
            Assert.That(CTF_Rules.IsReady(mask, 1), Is.False);
            Assert.That(CTF_Rules.IsReady(mask, 2), Is.True);
            mask = CTF_Rules.WithReady(mask, 0, false);
            Assert.That(CTF_Rules.IsReady(mask, 0), Is.False);
            Assert.That(CTF_Rules.IsReady(mask, 2), Is.True);
            mask = CTF_Rules.WithReady(mask, CTF_Rules.MaxPowerUps - 1, true);
            Assert.That(CTF_Rules.IsReady(mask, CTF_Rules.MaxPowerUps - 1), Is.True);
        }

        [Test]
        public void PowerUpMaskRejectsIndicesOutsideTheWire() {
            Assert.That(CTF_Rules.IsReady(uint.MaxValue, -1), Is.False);
            Assert.That(CTF_Rules.IsReady(uint.MaxValue, CTF_Rules.MaxPowerUps), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => CTF_Rules.WithReady(0, CTF_Rules.MaxPowerUps, true));
        }

        [Test]
        public void CarryingSlowsAndBoostStacks() {
            float carrier = CTF_Rules.CarrierSpeedMultiplier;
            Assert.That(CTF_Rules.SpeedMultiplier(false, carrier, false, 1.4f, 1f), Is.EqualTo(1f));
            Assert.That(CTF_Rules.SpeedMultiplier(true, carrier, false, 1.4f, 1f), Is.EqualTo(carrier));
            Assert.That(CTF_Rules.SpeedMultiplier(false, carrier, true, 1.4f, 1f), Is.EqualTo(1.4f));
            Assert.That(CTF_Rules.SpeedMultiplier(true, carrier, true, 1.4f, 1f), Is.EqualTo(carrier * 1.4f).Within(1e-5f));
        }

        [Test]
        public void HealNeverOverfillsOrRevives() {
            Assert.That(CTF_Rules.HealAmount(50, 200, 100), Is.EqualTo(100));
            Assert.That(CTF_Rules.HealAmount(150, 200, 100), Is.EqualTo(50));
            Assert.That(CTF_Rules.HealAmount(200, 200, 100), Is.EqualTo(0));
            Assert.That(CTF_Rules.HealAmount(0, 200, 100), Is.EqualTo(0));
        }

        [Test]
        public void OnlyNewerStatesAreKept() {
            Assert.That(CTF_Rules.IsNewer(5, 4), Is.True);
            Assert.That(CTF_Rules.IsNewer(4, 5), Is.False);
            Assert.That(CTF_Rules.IsNewer(5, 5), Is.False);
            Assert.That(CTF_Rules.IsNewer(2, uint.MaxValue), Is.True, "the count wraps around");
            Assert.That(CTF_Rules.IsNewer(1, 50000), Is.True, "a restarted server starts again from 1");
        }

        [Test]
        public void MissingNamesFallBackBySide() {
            Assert.That(CTF_Rules.PlayerLabel("Ana", false, false), Is.EqualTo("Ana"));
            Assert.That(CTF_Rules.PlayerLabel("Ana", true, true), Is.EqualTo("You"));
            Assert.That(CTF_Rules.PlayerLabel("", false, false), Is.EqualTo("An enemy"));
            Assert.That(CTF_Rules.PlayerLabel(" ", false, true), Is.EqualTo("A teammate"));
            Assert.That(CTF_Rules.PlayerLabel(null, false, false, startsSentence: false), Is.EqualTo("an enemy"));
            Assert.That(CTF_Rules.ActorTeam(CTF_EventKind.Taken, CTF_Team.Red), Is.EqualTo(CTF_Team.Blue));
            Assert.That(CTF_Rules.ActorTeam(CTF_EventKind.Returned, CTF_Team.Red), Is.EqualTo(CTF_Team.Red));
        }

        [Test]
        public void OneHitLeftBeatsTheEdgeVignette() {
            Assert.That(CTF_Rules.LowHealthEdge(1f, 0.5f, 0.55f), Is.Zero);
            Assert.That(CTF_Rules.LowHealthEdge(0.5f, 0.5f, 0.55f), Is.EqualTo(0.55f).Within(1e-5f));
            Assert.That(CTF_Rules.LowHealthEdge(0.25f, 0.5f, 0.55f), Is.EqualTo(0.775f).Within(1e-5f));
            Assert.That(CTF_Rules.LowHealthEdge(0f, 0.5f, 0.55f), Is.Zero, "dead: no warning");
        }
    }
}
