using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Universes.UniverseData.obby.Tests {
    public sealed class OB_ScoringTests {
        static OB_Standing Racer(int id, int stage, bool finished = false, float scored = 0f) =>
            new() { clientId = id, name = "P" + id, stage = stage, finished = finished, scoredTime = scored };

        [Test] public void CoinsTakeTimeOffButNeverBelowZero() {
            Assert.AreEqual(95f, OB_Scoring.ScoredTime(100f, 10, 0.5f), 1e-4f);
            Assert.AreEqual(100f, OB_Scoring.ScoredTime(100f, 0, 0.5f), 1e-4f);
            Assert.AreEqual(0f, OB_Scoring.ScoredTime(3f, 30, 0.5f));
            Assert.AreEqual(100f, OB_Scoring.ScoredTime(100f, 10, -1f), "a negative bonus is ignored");
        }

        [Test] public void MedalsScaleWithCourseLength() {
            var times = new OB_MedalTimes { goldPerStage = 10f, silverPerStage = 15f, bronzePerStage = 20f };
            Assert.AreEqual(300f, times.Target(OB_Medal.Gold, 30));
            Assert.AreEqual(OB_Medal.Gold, times.Award(300f, 30));
            Assert.AreEqual(OB_Medal.Silver, times.Award(300.1f, 30));
            Assert.AreEqual(OB_Medal.Bronze, times.Award(600f, 30));
            Assert.AreEqual(OB_Medal.None, times.Award(600.1f, 30));
            Assert.AreEqual(OB_Medal.Gold, times.Award(100f, 10));
        }

        [Test] public void DefaultMedalTargetsAreOrdered() {
            var times = OB_MedalTimes.Default;
            Assert.Less(times.goldPerStage, times.silverPerStage);
            Assert.Less(times.silverPerStage, times.bronzePerStage);
        }

        [Test] public void FinishersAreAheadOfRacersAndRankedByScoredTime() {
            var fastFinish = Racer(1, 30, true, 200f);
            var slowFinish = Racer(2, 30, true, 250f);
            var leader = Racer(3, 29);
            var trailer = Racer(4, 12);
            Assert.IsTrue(OB_Scoring.IsAhead(fastFinish, slowFinish));
            Assert.IsTrue(OB_Scoring.IsAhead(slowFinish, leader));
            Assert.IsTrue(OB_Scoring.IsAhead(leader, trailer));
            Assert.IsFalse(OB_Scoring.IsAhead(trailer, leader));
            Assert.IsFalse(OB_Scoring.IsAhead(Racer(5, 12), trailer), "same checkpoint is level");
        }

        [Test] public void PlaceCountsOnlyRacersStrictlyAhead() {
            var racers = new[] { Racer(1, 5), Racer(2, 9), Racer(3, 5), Racer(4, 2) };
            Assert.AreEqual(1, OB_Scoring.PlaceOf(racers, 2));
            Assert.AreEqual(2, OB_Scoring.PlaceOf(racers, 1));
            Assert.AreEqual(2, OB_Scoring.PlaceOf(racers, 3), "level racers share a place");
            Assert.AreEqual(4, OB_Scoring.PlaceOf(racers, 4));
            Assert.AreEqual(0, OB_Scoring.PlaceOf(racers, 99));
            Assert.AreEqual(0, OB_Scoring.PlaceOf(null, 1));
        }

        [Test] public void ResultOrderIsLeadersFirstAndStable() {
            var racers = new[] { Racer(4, 3), Racer(1, 30, true, 240f), Racer(3, 8), Racer(2, 30, true, 210f), Racer(5, 8) };
            var order = racers.ToList();
            order.Sort(OB_Scoring.Compare);
            CollectionAssert.AreEqual(new[] { 2, 1, 3, 5, 4 }, order.Select(r => r.clientId));
        }

        [Test] public void CoinMaskCountsBits() {
            Assert.AreEqual(0, OB_Scoring.CountCoins(0));
            Assert.AreEqual(3, OB_Scoring.CountCoins(0b1011));
            Assert.AreEqual(64, OB_Scoring.CountCoins(ulong.MaxValue));
        }

        [Test] public void ClockFormatsAndRollsTenthsOver() {
            Assert.AreEqual("1:05", OB_Scoring.Clock(65.9f));
            Assert.AreEqual("1:05.9", OB_Scoring.Clock(65.9f, true));
            Assert.AreEqual("2:00.0", OB_Scoring.Clock(119.97f, true));
            Assert.AreEqual("0:00", OB_Scoring.Clock(-3f));
        }

        [Test] public void AllocationFreeClockMatchesTheStringClock() {
            var buffer = new char[16];
            foreach (int tenths in new[] { 0, 9, 659, 1234, 5999, 6000, 36001 }) {
                int length = OB_Scoring.ClockTenths(tenths, buffer);
                Assert.AreEqual(OB_Scoring.Clock(tenths / 10f + 0.01f, true), new string(buffer, 0, length), $"{tenths} tenths");
            }
            Assert.AreEqual("0:00.0", new string(buffer, 0, OB_Scoring.ClockTenths(-5, buffer)));
        }

        // Overtakes: the rule the race strip uses ("who was ahead before and is not now").
        static HashSet<int> Ahead(OB_Standing[] racers, int me) {
            var set = new HashSet<int>();
            OB_Scoring.AheadOf(racers, me, set);
            return set;
        }

        [Test] public void ReachingACheckpointPastSomeoneIsAGain() {
            var before = Ahead(new[] { Racer(1, 3), Racer(2, 4) }, 1);
            var now = new[] { Racer(1, 5), Racer(2, 4) };
            OB_Scoring.Overtakes(before, Ahead(now, 1), now, out int gained, out int lost);
            Assert.AreEqual((1, 0), (gained, lost));
        }

        [Test] public void BeingPassedIsALoss() {
            var before = Ahead(new[] { Racer(1, 5), Racer(2, 4), Racer(3, 4) }, 1);
            var now = new[] { Racer(1, 5), Racer(2, 6), Racer(3, 7) };
            OB_Scoring.Overtakes(before, Ahead(now, 1), now, out int gained, out int lost);
            Assert.AreEqual((0, 2), (gained, lost));
        }

        [Test] public void DrawingLevelOrSomeoneLeavingIsNoPass() {
            var before = Ahead(new[] { Racer(1, 3), Racer(2, 4), Racer(3, 6) }, 1);
            var now = new[] { Racer(1, 4), Racer(2, 4) };
            OB_Scoring.Overtakes(before, Ahead(now, 1), now, out int gained, out int lost);
            Assert.AreEqual(1, gained, "drawing level with racer 2 counts (they are no longer strictly ahead)");
            Assert.AreEqual(0, lost);
            var left = new[] { Racer(1, 3) };
            OB_Scoring.Overtakes(Ahead(new[] { Racer(1, 3), Racer(3, 6) }, 1), Ahead(left, 1), left, out gained, out lost);
            Assert.AreEqual((0, 0), (gained, lost), "a leader who leaves was not passed");
        }

        [Test] public void FinishingAheadOfAFinisherIsAGain() {
            var before = Ahead(new[] { Racer(1, 29), Racer(2, 30, true, 300f) }, 1);
            var now = new[] { Racer(1, 30, true, 280f), Racer(2, 30, true, 300f) };
            OB_Scoring.Overtakes(before, Ahead(now, 1), now, out int gained, out int lost);
            Assert.AreEqual((1, 0), (gained, lost));
        }

        [Test] public void GoldPaceCountsCoinsAndStages() {
            Assert.AreEqual(-5f, OB_Scoring.GoldPaceDelta(25f, 0, 0.5f, 3, 10f), 1e-4f);
            Assert.AreEqual(4f, OB_Scoring.GoldPaceDelta(35f, 2, 0.5f, 3, 10f), 1e-4f);
            Assert.AreEqual(0f, OB_Scoring.GoldPaceDelta(0f, 0, 0.5f, 0, 10f), 1e-4f);
        }

        [Test] public void OrdinalsReadNaturally() {
            CollectionAssert.AreEqual(new[] { "1st", "2nd", "3rd", "4th", "11th", "12th", "13th", "21st", "102nd" },
                new[] { 1, 2, 3, 4, 11, 12, 13, 21, 102 }.Select(OB_Scoring.Ordinal));
        }
    }
}
