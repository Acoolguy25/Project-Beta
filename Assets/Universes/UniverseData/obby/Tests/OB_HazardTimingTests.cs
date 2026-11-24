using NUnit.Framework;
using RyanAssets.Platformer;
using UnityEngine;

namespace Universes.UniverseData.obby.Tests {
    /// <summary>
    /// The server judges laser gates and crushers at the moment the player saw, with the same grace at both
    /// ends of every deadly window (<see cref="TimedHazards"/>): a player who visibly cleared one is never
    /// sent back, and one standing in it still is.
    /// </summary>
    public sealed class OB_HazardTimingTests {
        const float Grace = 0.05f;
        static readonly Vector3 Body = new(0.5f, 1f, 0.5f); // half extents of a small test body
        GameObject root;
        TimedHazards judge;

        [SetUp] public void SetUp() {
            root = new GameObject("Hazards");
            judge = new TimedHazards();
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        TimedToggleBlock Laser(Vector3 at, float period, float onFraction) {
            var go = new GameObject("Laser");
            go.transform.SetParent(root.transform, false);
            go.transform.position = at;
            go.transform.localScale = new Vector3(4f, 0.3f, 0.3f);
            go.AddComponent<BoxCollider>().isTrigger = true;
            var block = go.AddComponent<TimedToggleBlock>();
            block.Configure(period, onFraction, 0f);
            go.AddComponent<HazardVolume>();
            return block;
        }

        Crusher Thwomp(Vector3 raised, float drop) {
            var go = new GameObject("Crusher");
            go.transform.SetParent(root.transform, false);
            go.transform.position = raised;
            go.AddComponent<Rigidbody>();
            var crusher = go.AddComponent<Crusher>();
            var zone = new GameObject("Zone");
            zone.transform.SetParent(go.transform, false);
            zone.transform.localPosition = new Vector3(0f, -0.55f, 0f);
            var box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.6f, 0.25f, 1.6f);
            zone.AddComponent<HazardVolume>();
            crusher.crushZone = box;
            crusher.Configure(drop, 3f, 0f, 0.6f);
            return crusher;
        }

        bool Hits(Vector3 at, double time) => judge.Hits(at, Body, Quaternion.identity, time, Grace);

        [Test] public void OrientedBoxesOverlapOnlyWhenTheyTouch() {
            var turned = Quaternion.Euler(0f, 45f, 0f);
            Assert.IsTrue(PlatformerQuery.BoxesOverlap(Vector3.zero, Vector3.one, Quaternion.identity, new Vector3(1.9f, 0f, 0f), Vector3.one, Quaternion.identity));
            Assert.IsFalse(PlatformerQuery.BoxesOverlap(Vector3.zero, Vector3.one, Quaternion.identity, new Vector3(2.1f, 0f, 0f), Vector3.one, Quaternion.identity));
            // A box turned 45 degrees reaches sqrt(2) along x: it touches at 2.3, not at 2.5.
            Assert.IsTrue(PlatformerQuery.BoxesOverlap(Vector3.zero, Vector3.one, Quaternion.identity, new Vector3(2.3f, 0f, 0f), Vector3.one, turned));
            Assert.IsFalse(PlatformerQuery.BoxesOverlap(Vector3.zero, Vector3.one, Quaternion.identity, new Vector3(2.5f, 0f, 0f), Vector3.one, turned));
            // Turned about different axes: overlapping corners, then pulled apart.
            var tilted = Quaternion.Euler(45f, 0f, 0f);
            Assert.IsTrue(PlatformerQuery.BoxesOverlap(Vector3.zero, Vector3.one, turned, new Vector3(1.6f, 1.6f, 0f), Vector3.one, tilted));
            Assert.IsFalse(PlatformerQuery.BoxesOverlap(Vector3.zero, Vector3.one, turned, new Vector3(2.5f, 2.5f, 0f), Vector3.one, tilted));
        }

        [Test] public void ALaserCountsOnlyWhileOnAtTheMomentThePlayerSaw() {
            Laser(new Vector3(0f, 1f, 0f), 2f, 0.5f); // on for [0, 1) of every 2 s
            judge.Collect(root.transform);
            Assert.AreEqual(1, judge.Count);
            Vector3 inBeam = new(0f, 1f, 0f), clear = new(0f, 1f, 3f);
            Assert.IsTrue(Hits(inBeam, 0.5), "standing in a lit beam is deadly");
            Assert.IsFalse(Hits(clear, 0.5), "beside the beam is safe");
            Assert.IsFalse(Hits(inBeam, 1.5), "an unlit beam is safe");
            Assert.IsFalse(Hits(inBeam, 0.02), "inside the grace after it lights");
            Assert.IsFalse(Hits(inBeam, 0.97), "inside the grace before it goes out");
            Assert.IsTrue(Hits(inBeam, 0.06), "just past the grace it counts");
        }

        [Test] public void APlayerWhoCrossedAtTheLastSafeMomentIsNeverSentBack() {
            // The beam lights at t = 2. The player saw it unlit and was in the beam until 0.01 s before it lit.
            // Their position reaches a server whose clock is 0.2 s ahead: judged at the server's now it would
            // be a death on every one of these passes; judged at the moment they saw, never.
            var block = Laser(new Vector3(0f, 1f, 0f), 2f, 0.5f);
            judge.Collect(root.transform);
            Vector3 inBeam = new(0f, 1f, 0f);
            const float serverAhead = 0.2f;
            for (int pass = 0; pass < 20; pass++) {
                double seen = 2.0 * (pass + 1) - 0.01 - pass * 0.0004; // the last visible moment, slightly varied
                Assert.IsFalse(block.IsSolidAt(seen), "the player saw the beam off");
                Assert.IsTrue(block.IsSolidAt(seen + serverAhead), "the server's now has it on");
                Assert.IsFalse(Hits(inBeam, seen), $"pass {pass}: sent back although the beam was off where they saw it");
            }
            // Standing in it is still deadly whenever it is on, whatever the delay.
            for (double t = 0.1; t < 0.9; t += 0.1) Assert.IsTrue(Hits(inBeam, 2.0 + t));
        }

        [Test] public void ACrusherCrushesOnlyWhereTheBlockWasWhenThePlayerSaw() {
            const float drop = 4f;
            var crusher = Thwomp(new Vector3(0f, drop + 1.5f, 0f), drop);
            judge.Collect(root.transform);
            Assert.AreEqual(1, judge.Count);
            float open = crusher.OpenSeconds; // slam [open, open+0.16), down until open+0.76, then rising
            Vector3 underneath = new(0f, 1f, 0f), aside = new(3f, 1f, 0f);
            double down = open + 0.4;
            Assert.IsTrue(crusher.IsDeadlyAt(down));
            Assert.IsTrue(Hits(underneath, down), "under a crusher that is down");
            Assert.IsFalse(Hits(aside, down), "beside it");
            Assert.IsFalse(Hits(underneath, open * 0.5), "while it waits raised");
            Assert.IsFalse(Hits(underneath, open - 0.02), "while it shakes before the slam");
            Assert.IsFalse(Hits(underneath, open + crusher.slamSeconds + crusher.holdSeconds - 0.02), "inside the grace before it rises");
            // Last-safe-moment passes: out from under it just before the slam, with the server 0.2 s ahead.
            for (int pass = 0; pass < 20; pass++) {
                double seen = open + crusher.period * pass - 0.01;
                Assert.IsTrue(crusher.IsDeadlyAt(seen + 0.2), "the server's now has it slamming");
                Assert.IsFalse(Hits(underneath, seen), $"pass {pass}: crushed although it had not slammed where they saw it");
            }
        }
    }
}
