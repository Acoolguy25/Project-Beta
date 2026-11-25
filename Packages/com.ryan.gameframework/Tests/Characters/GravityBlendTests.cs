using NUnit.Framework;
using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.Characters.Tests {
    /// <summary>EditMode tests of the gravity smoothing maths: the up blend, velocity carry-over and the zone-edge debounce.</summary>
    public class GravityBlendTests {
        const float Tolerance = 1e-4f;

        static readonly Vector3 Up = Vector3.up;
        static readonly Vector3 Forward = Vector3.forward;

        [Test]
        public void Ease_StartsAndEndsAtRestAndIsMonotonic() {
            Assert.AreEqual(0f, GravityBlend.Ease(0f), Tolerance);
            Assert.AreEqual(1f, GravityBlend.Ease(1f), Tolerance);
            Assert.AreEqual(0.5f, GravityBlend.Ease(0.5f), Tolerance);
            float previous = 0f;
            for (int i = 1; i <= 100; i++) {
                float value = GravityBlend.Ease(i / 100f);
                Assert.GreaterOrEqual(value, previous);
                previous = value;
            }
            // No speed at the ends: a turn never starts or stops with a pop.
            Assert.Less(GravityBlend.Ease(0.01f), 0.001f);
            Assert.Greater(GravityBlend.Ease(0.99f), 0.999f);
        }

        [Test]
        public void Up_ReachesExactlyTheTargetAndStartsAtTheSource() {
            Vector3 target = Vector3.right;
            Assert.AreEqual(Up, GravityBlend.Up(Up, target, 0f, Forward));
            Assert.AreEqual(target, GravityBlend.Up(Up, target, 1f, Forward));
            Assert.AreEqual(target, GravityBlend.Up(Up, target, 2f, Forward));
        }

        [Test]
        public void Up_StaysUnitLengthAndMovesInSmallSteps() {
            foreach (Vector3 target in new[] { Vector3.right, Vector3.down, Vector3.back, new Vector3(1f, 1f, 0f).normalized }) {
                Vector3 previous = Up;
                float widest = 0f;
                for (int i = 1; i <= 60; i++) {
                    Vector3 up = GravityBlend.Up(Up, target, i / 60f, Forward);
                    Assert.AreEqual(1f, up.magnitude, Tolerance, $"unit length toward {target}");
                    widest = Mathf.Max(widest, Vector3.Angle(previous, up));
                    previous = up;
                }
                float total = Vector3.Angle(Up, target);
                // Smoothstep peaks at 1.5x the average speed, so no step is more than ~1.6x an even one.
                Assert.LessOrEqual(widest, total / 60f * 1.6f + 0.01f, $"no pop toward {target}");
            }
        }

        [Test]
        public void Up_FlippingUpsideDownRollsAboutTheFacingAxis() {
            // Turning over about forward keeps up in the plane perpendicular to forward: it passes through
            // the sideways axis, never through forward, so the camera and body keep facing the same way.
            for (int i = 1; i < 20; i++) {
                Vector3 up = GravityBlend.Up(Up, Vector3.down, i / 20f, Forward);
                Assert.AreEqual(0f, Vector3.Dot(up, Forward), Tolerance);
            }
            Vector3 middle = GravityBlend.Up(Up, Vector3.down, 0.5f, Forward);
            Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(middle, Vector3.right)), Tolerance);
        }

        [Test]
        public void Reproject_PlanarSpeedTurnsWithTheFrameAndTheVerticalPartKeepsItsWorldDirection() {
            Vector3 velocity = Forward * 5f + Up * 3f;
            Vector3 newUp = Vector3.right;
            Vector3 result = GravityBlend.Reproject(velocity, Up, newUp, Forward);
            // Forward is perpendicular to both ups, so it is unchanged (and still planar); the vertical part stays world up.
            Assert.AreEqual(5f, Vector3.Dot(result, Forward), Tolerance);
            Assert.AreEqual(3f, Vector3.Dot(result, Up), Tolerance);
            // Running toward the wall ahead-and-left turns with the frame instead of ploughing into it.
            Vector3 running = Vector3.right * 6f;
            Vector3 carried = GravityBlend.Reproject(running, Up, Vector3.right, Forward);
            Assert.AreEqual(6f, Vector3.Dot(carried, Vector3.down), 1e-3f, "planar +x becomes planar along the new wall (down in world)");
            Assert.AreEqual(0f, Vector3.Dot(carried, newUp), 1e-3f, "none of it points into the new up");
        }

        [Test]
        public void Reproject_WithNoTurnChangesNothing() {
            Vector3 velocity = new(1f, 2f, 3f);
            Assert.AreEqual(velocity, GravityBlend.Reproject(velocity, Up, Up, Forward));
        }

        [Test]
        public void Reproject_PlanarSpeedIsKeptStepByStepThroughATurn() {
            Vector3 velocity = Forward * 7f;
            Vector3 previous = Up;
            for (int i = 1; i <= 20; i++) {
                Vector3 up = GravityBlend.Up(Up, Vector3.right, i / 20f, Forward);
                velocity = GravityBlend.Reproject(velocity, previous, up, Forward);
                previous = up;
            }
            Assert.AreEqual(7f, velocity.magnitude, 1e-2f);
            Assert.AreEqual(0f, Vector3.Dot(velocity, previous), 1e-2f);
        }

        [Test]
        public void SwitchFilter_AcceptsOnlyAfterTheConfirmTime() {
            var filter = new GravityBlend.SwitchFilter();
            filter.Reset(Vector3.down, 0f);
            Assert.IsFalse(filter.Update(Vector3.right, 1f, 0.1f, 0f));
            Assert.IsTrue(filter.HasPending);
            Assert.IsFalse(filter.Update(Vector3.right, 1.05f, 0.1f, 0f));
            Assert.IsTrue(filter.Update(Vector3.right, 1.1f, 0.1f, 0f));
            Assert.AreEqual(Vector3.right, filter.Accepted);
            Assert.IsFalse(filter.HasPending);
        }

        [Test]
        public void SwitchFilter_AFlickerAtAZoneEdgeNeverChangesDirection() {
            var filter = new GravityBlend.SwitchFilter();
            filter.Reset(Vector3.down, 0f);
            // In and out of a volume every step (0.02 s): the new direction is never asked for steadily.
            for (int i = 1; i <= 200; i++) {
                float now = i * 0.02f;
                Vector3 requested = i % 2 == 0 ? Vector3.right : Vector3.down;
                Assert.IsFalse(filter.Update(requested, now, 0.06f, 0.35f), $"step {i}");
            }
            Assert.AreEqual(Vector3.down, filter.Accepted);
        }

        [Test]
        public void SwitchFilter_HoldsTheDirectionForTheMinimumTimeBeforeChangingAgain() {
            var filter = new GravityBlend.SwitchFilter();
            filter.Reset(Vector3.down, 0f);
            Assert.IsTrue(filter.Update(Vector3.up, 0f, 0f, 0.35f));
            // Two flip pads close together: the second waits out the hold, and only then applies.
            Assert.IsFalse(filter.Update(Vector3.down, 0.1f, 0f, 0.35f));
            Assert.IsFalse(filter.Update(Vector3.down, 0.3f, 0f, 0.35f));
            Assert.IsTrue(filter.Update(Vector3.down, 0.36f, 0f, 0.35f));
            Assert.AreEqual(Vector3.down, filter.Accepted);
        }

        [Test]
        public void SwitchFilter_AskingForTheCurrentDirectionCancelsAPendingChange() {
            var filter = new GravityBlend.SwitchFilter();
            filter.Reset(Vector3.down, 0f);
            filter.Update(Vector3.right, 1f, 0.1f, 0f);
            Assert.IsTrue(filter.HasPending);
            filter.Update(Vector3.down, 1.05f, 0.1f, 0f);
            Assert.IsFalse(filter.HasPending);
            Assert.IsFalse(filter.Update(Vector3.right, 1.12f, 0.1f, 0f), "the clock restarted");
        }

        [Test]
        public void DefaultGravity_GivesUpEqualDownEqualDefaultWithNoBlend() {
            var go = new GameObject("gravity-default");
            try {
                var gravity = go.AddComponent<CharacterGravity>();
                Assert.AreEqual(Vector3.up, gravity.Up);
                Assert.AreEqual(Vector3.down, gravity.Down);
                Assert.AreEqual(gravity.TargetDown, gravity.Down, "smoothed and target agree");
                Assert.AreEqual(gravity.TargetUp, gravity.Up);
                Assert.IsFalse(gravity.IsBlending, "no ease is running at rest");
                Assert.IsTrue(gravity.IsDefaultDirection);
            } finally {
                Object.DestroyImmediate(go);
            }
        }
    }
}
