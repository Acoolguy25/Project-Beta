using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Tests {
    /// <summary>The pumpkin hunt's rules (<see cref="HS_Pumpkins"/>) and its save fields.</summary>
    public sealed class HS_PumpkinsTests {
        [Test]
        public void HidersCollectSeekersSnuffAndSpectatorsDoNothing() {
            Assert.That(HS_Pumpkins.TakeBy(HS_Role.Hider), Is.EqualTo(HS_PumpkinTake.Collected));
            Assert.That(HS_Pumpkins.TakeBy(HS_Role.Seeker), Is.EqualTo(HS_PumpkinTake.Snuffed));
            Assert.That(HS_Pumpkins.TakeBy(HS_Role.Caught), Is.EqualTo(HS_PumpkinTake.Snuffed));
            Assert.That(HS_Pumpkins.TakeBy(HS_Role.None), Is.EqualTo(HS_PumpkinTake.None));
        }

        [Test]
        public void ReachIsFlatAndNeverThroughAFloor() {
            Vector3 pumpkin = new(10f, 0f, 10f);
            Assert.That(HS_Pumpkins.InReach(new Vector3(12f, 0.5f, 11f), pumpkin, 3.2f, 3f), Is.True);
            Assert.That(HS_Pumpkins.InReach(new Vector3(14f, 0f, 10f), pumpkin, 3.2f, 3f), Is.False, "too far");
            Assert.That(HS_Pumpkins.InReach(new Vector3(10f, 9f, 10f), pumpkin, 3.2f, 3f), Is.False, "the storey above");
        }

        [Test]
        public void SpotsAreClearOfLivePumpkinsAndPlayers() {
            var points = new List<Vector3>();
            for (int i = 0; i < 20; i++) points.Add(new Vector3(i * 10f, 0f, 0f));
            var live = new List<Vector3> { new(0f, 0f, 0f) };
            var players = new List<Vector3> { new(190f, 0f, 0f) };
            var random = new System.Random(7);
            for (int run = 0; run < 200; run++) {
                int spot = HS_Pumpkins.PickSpot(points, live, players, 30f, 25f, random);
                Assert.That(spot, Is.InRange(3, 16), $"run {run}: x = {points[spot].x}");
            }
        }

        [Test]
        public void ACrowdedMapFallsBackToTheClearestPoint() {
            var points = new List<Vector3> { new(0f, 0f, 0f), new(10f, 0f, 0f), new(20f, 0f, 0f) };
            var players = new List<Vector3> { new(0f, 0f, 0f) };
            Assert.That(HS_Pumpkins.PickSpot(points, null, players, 30f, 50f, new System.Random(1)), Is.EqualTo(2));
            Assert.That(HS_Pumpkins.PickSpot(new List<Vector3>(), null, players, 30f, 50f, new System.Random(1)), Is.EqualTo(-1));
        }

        [Test]
        public void EveryThirdPumpkinFillsABag() {
            Assert.That(HS_Pumpkins.FillsBag(1, 3), Is.False);
            Assert.That(HS_Pumpkins.FillsBag(3, 3), Is.True);
            Assert.That(HS_Pumpkins.FillsBag(6, 3), Is.True);
            Assert.That(HS_Pumpkins.InBag(0, 3), Is.EqualTo(0));
            Assert.That(HS_Pumpkins.InBag(2, 3), Is.EqualTo(2));
            Assert.That(HS_Pumpkins.InBag(3, 3), Is.EqualTo(3), "a full bag shows full");
            Assert.That(HS_Pumpkins.InBag(4, 3), Is.EqualTo(1));
        }

        [Test]
        public void TheScheduleHandsOutOnlyWhatIsDue() {
            var schedule = new List<float> { 5f, 12f, 5f };
            Assert.That(HS_Pumpkins.TakeDue(schedule, 4f), Is.EqualTo(0));
            Assert.That(HS_Pumpkins.TakeDue(schedule, 5f), Is.EqualTo(2));
            Assert.That(schedule, Is.EqualTo(new List<float> { 12f }));
        }

        [Test]
        public void TuningIsClampedAtTheAuthorityBoundary() {
            var tuning = new HS_PumpkinTuning { liveCount = 40, bagSize = 0, respawnSeconds = -3f, paidPerRound = 0 };
            tuning.Validate();
            Assert.That(tuning.liveCount, Is.EqualTo(8));
            Assert.That(tuning.bagSize, Is.EqualTo(1));
            Assert.That(tuning.respawnSeconds, Is.EqualTo(1f));
            Assert.That(tuning.paidPerRound, Is.EqualTo(1));
        }

        [Test]
        public void PumpkinCountsRepairNegatives() {
            var save = new HS_SaveData { pumpkins = 4, pumpkinsSnuffed = -2 };
            save.Validate();
            Assert.That(save.pumpkins, Is.EqualTo(4));
            Assert.That(save.pumpkinsSnuffed, Is.EqualTo(0));
        }
    }
}
