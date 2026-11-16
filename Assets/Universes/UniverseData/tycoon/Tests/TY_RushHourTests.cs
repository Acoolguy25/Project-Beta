using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.tycoon.Tests {
    public class TY_RushHourTests {
        static TY_RushHour Rush() => new() { interval = new Vector2(100f, 200f), duration = 30f, multiplier = 2f };

        [Test]
        public void StartsWhenDueAndEndsAfterItsDuration() {
            var rush = Rush();
            rush.Schedule(0f, 0f);
            Assert.AreEqual(100f, rush.NextAt, 1e-4f);
            Assert.AreEqual(TY_RushChange.None, rush.Tick(99f, 0.5f, true));
            Assert.AreEqual(1f, rush.Multiplier(99f));

            Assert.AreEqual(TY_RushChange.Started, rush.Tick(100f, 0.5f, true));
            Assert.IsTrue(rush.IsActive(110f));
            Assert.AreEqual(2f, rush.Multiplier(110f));
            Assert.AreEqual(20f, rush.Remaining(110f), 1e-4f);
            Assert.AreEqual(TY_RushChange.None, rush.Tick(110f, 0.5f, true));

            Assert.AreEqual(TY_RushChange.Ended, rush.Tick(130f, 0.5f, true));
            Assert.IsFalse(rush.IsActive(130f));
            Assert.AreEqual(0f, rush.Remaining(130f));
            Assert.AreEqual(130f + 150f, rush.NextAt, 1e-4f, "the next one is planned from the end of this one");
        }

        [Test]
        public void WaitsWhileNobodyCanEarn() {
            var rush = Rush();
            rush.Schedule(0f, 1f);
            Assert.AreEqual(TY_RushChange.None, rush.Tick(250f, 0f, false), "no plot is owned: pushed back, not started");
            Assert.AreEqual(350f, rush.NextAt, 1e-4f);
            Assert.AreEqual(TY_RushChange.None, rush.Tick(251f, 0f, true), "claiming a plot does not fire it at once");
            Assert.AreEqual(TY_RushChange.Started, rush.Tick(350f, 0f, true));
        }

        [Test]
        public void BeginForcesARushAndValidateKeepsTuningSane() {
            var rush = new TY_RushHour { interval = new Vector2(50f, 10f), duration = 0f, multiplier = 0.5f };
            rush.Begin(5f);
            Assert.IsTrue(rush.IsActive(5.5f));
            Assert.GreaterOrEqual(rush.duration, 1f);
            Assert.GreaterOrEqual(rush.multiplier, 1f);
            Assert.GreaterOrEqual(rush.interval.y, rush.interval.x);
            Assert.AreEqual(TY_RushChange.None, rush.Tick(5.5f, 0f, true));
        }
    }
}
