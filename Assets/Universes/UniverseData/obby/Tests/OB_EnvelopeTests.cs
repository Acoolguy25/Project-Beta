using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.obby.Tests {
    /// <summary>The server's stage envelope and the generator's out-of-bounds guards (brief item 3).</summary>
    public sealed class OB_EnvelopeTests {
        static readonly Vector3 Start = new(0f, 100f, 9f), Lobby = new(0f, 100f, -3f);
        static readonly OB_Movement Move = OB_Movement.Default;
        static readonly OB_CourseRules Rules = OB_CourseRules.Default;
        static readonly OB_EnvelopeRules Margins = OB_EnvelopeRules.Default;
        static readonly int[] Lengths = { 13, OB_CourseGenerator.DefaultStageCount, 30 };

        static IEnumerable<OB_CourseLayout> Courses(int seeds) {
            foreach (int n in Lengths)
                for (int seed = 1; seed <= seeds; seed++)
                    yield return OB_CourseGenerator.Generate(seed * 7919 + n, n, Move, Start, 0f, Lobby, Rules);
        }

        static string Where(OB_CourseLayout c, in OB_Piece p) => $"seed {c.seed} ({c.StageCount} stages) stage {p.stage} segment {p.segment} {p.kind}";

        /// <summary>Where a racer's middle is when standing on the top face at <paramref name="top"/>: half the body above the feet.</summary>
        static Vector3 Middle(Vector3 top) => top + Vector3.up * (Move.bodyHeight * 0.5f);

        static IEnumerable<Vector3> TopSamples(OB_Piece p, float step) {
            Quaternion turn = Quaternion.Euler(0f, p.yaw, 0f);
            Vector3 half = p.size * 0.5f;
            int nx = Mathf.Max(1, Mathf.CeilToInt(p.size.x / step)), nz = Mathf.Max(1, Mathf.CeilToInt(p.size.z / step));
            for (int i = 0; i <= nx; i++)
                for (int k = 0; k <= nz; k++) {
                    var local = new Vector3(Mathf.Lerp(-half.x, half.x, i / (float)nx) * 0.98f, half.y, Mathf.Lerp(-half.z, half.z, k / (float)nz) * 0.98f);
                    yield return p.position + turn * local;
                }
        }

        static bool IsFloor(in OB_Piece p) => p.kind switch {
            OB_PieceKind.Platform => p.group == 0,
            OB_PieceKind.CheckpointPad or OB_PieceKind.Finish or OB_PieceKind.Beam or OB_PieceKind.Ice or OB_PieceKind.Honey
                or OB_PieceKind.Ghost or OB_PieceKind.Plank or OB_PieceKind.Conveyor or OB_PieceKind.Crumble or OB_PieceKind.Beat => true,
            _ => false
        };

        [Test] public void EveryGravityWallAndCeilingHasItsFarSideGuarded() {
            int slabs = 0;
            foreach (var c in Courses(40)) {
                var env = new OB_StageEnvelope(c, Margins);
                foreach (var p in c.pieces) {
                    if (p.kind != OB_PieceKind.Platform || p.group != 1) continue;
                    slabs++;
                    int reached = Mathf.Min(p.stage, c.StageCount - 1);
                    // Standing anywhere on the slab's top face (the far side of a wall, the top of a ceiling) is out of bounds.
                    foreach (Vector3 spot in TopSamples(p, 1.5f))
                        Assert.IsFalse(env.Allows(reached, Middle(spot)), $"{Where(c, p)}: a racer can stand on top of the slab at {spot}");
                }
            }
            Assert.Greater(slabs, 300);
        }

        [Test] public void EveryOutOfBoundsGuardSitsBesideAGravityStage() {
            int guards = 0;
            foreach (var c in Courses(30))
                foreach (var p in c.pieces) {
                    if (p.kind != OB_PieceKind.OutOfBounds) continue;
                    guards++;
                    Assert.IsTrue(c.stages[p.stage].segments.Any(OB_CourseGenerator.IsGravity), $"{Where(c, p)}: guard outside a gravity stage");
                    Assert.Greater(Mathf.Min(p.size.x, p.size.y, p.size.z), 0.5f, Where(c, p));
                }
            Assert.Greater(guards, 100);
        }

        [Test] public void EveryStandableSurfaceAndGravityFieldIsInsideTheEnvelope() {
            int spots = 0;
            foreach (var c in Courses(8)) {
                var env = new OB_StageEnvelope(c, Margins);
                foreach (var p in c.pieces) {
                    int reached = Mathf.Min(p.stage, c.StageCount - 1);
                    if (IsFloor(p)) {
                        foreach (Vector3 spot in TopSamples(p, 3f)) {
                            spots++;
                            Assert.IsTrue(env.Allows(reached, Middle(spot)), $"{Where(c, p)}: a racer standing on it would be sent back");
                        }
                    } else if (p.kind == OB_PieceKind.GravityZone) {
                        // Anyone inside a gravity field (their middle can be anywhere in it) is playing normally.
                        Quaternion turn = Quaternion.Euler(0f, p.yaw, 0f);
                        for (int i = -1; i <= 1; i++)
                            for (int j = -1; j <= 1; j++)
                                for (int k = -1; k <= 1; k++) {
                                    Vector3 spot = p.position + turn * new Vector3(i * p.size.x * 0.45f, j * p.size.y * 0.45f, k * p.size.z * 0.45f);
                                    spots++;
                                    Assert.IsTrue(env.Allows(reached, spot), $"{Where(c, p)}: inside the field at {spot}");
                                }
                    }
                }
            }
            Assert.Greater(spots, 15000);
        }

        [Test] public void SkippingAheadLeavesTheEnvelope() {
            int checks = 0;
            foreach (var c in Courses(20)) {
                var env = new OB_StageEnvelope(c, Margins);
                foreach (var p in c.pieces) {
                    if (!IsFloor(p) || p.stage < 4) continue;
                    // From the lobby, the first checkpoint or the second, a standing spot four stages on is out of reach.
                    Vector3 spot = Middle(p.position + Vector3.up * (p.size.y * 0.5f));
                    for (int reached = 0; reached <= Mathf.Min(2, p.stage - 4); reached++) {
                        checks++;
                        Assert.IsFalse(env.Allows(reached, spot), $"{Where(c, p)}: reachable with only checkpoint {reached}");
                    }
                }
            }
            Assert.Greater(checks, 5000);
        }

        [Test] public void FlyingFarFromTheCourseLeavesTheEnvelope() {
            var c = OB_CourseGenerator.Generate(5, OB_CourseGenerator.DefaultStageCount, Move, Start, 0f, Lobby, Rules);
            var env = new OB_StageEnvelope(c, Margins);
            Assert.IsTrue(env.Allows(0, Lobby + Vector3.up * 2.8f), "the lobby is inside");
            Assert.IsFalse(env.Allows(0, Lobby + new Vector3(80f, 0f, 0f)), "far beside the lobby");
            Assert.IsFalse(env.Allows(c.StageCount, c.stages[1].spawn + Vector3.up * 200f), "high above the course");
            Assert.IsFalse(env.Allows(c.StageCount, c.stages[1].spawn + new Vector3(0f, 0f, -300f)), "far behind it");
        }

        /// <summary>Walk and dash input a flight may carry on top of its launch (hold forward, at the launcher's air control share).</summary>
        static IEnumerable<float> ExtraSpeeds(float share) { yield return 0f; yield return Move.walkSpeed * share; yield return Move.sprintSpeed * share; }

        [Test] public void LauncherFlightsAndDashJumpsStayInsideTheEnvelope() {
            int arcs = 0, samples = 0;
            float cannonShare = Rules.mechanics.cannons.airControl, ringShare = Rules.mechanics.rings.airControl;
            foreach (var c in Courses(10)) {
                var env = new OB_StageEnvelope(c, Margins);
                for (int i = 0; i < c.pieces.Count; i++) {
                    var p = c.pieces[i];
                    int reached = Mathf.Min(p.stage, c.StageCount - 1);
                    Quaternion turn = Quaternion.Euler(0f, p.yaw, 0f);
                    if (p.kind == OB_PieceKind.Cannon || p.kind == OB_PieceKind.Ring) {
                        bool ring = p.kind == OB_PieceKind.Ring;
                        float flight = p.speed;
                        // A cannon's vector is local; a ring's is already in world space.
                        Vector3 local = ring ? Quaternion.Inverse(turn) * p.vector : p.vector;
                        foreach (float extra in ExtraSpeeds(ring ? ringShare : cannonShare)) {
                            arcs++;
                            for (float t = 0f; t <= flight; t += 0.05f) {
                                float forward = Move.Carry(local.z, t) + extra * t, side = Move.Carry(local.x, t);
                                Vector3 feet = p.position + turn * new Vector3(side, local.y * t - 0.5f * Move.gravity * t * t, forward);
                                samples++;
                                Assert.IsTrue(env.Allows(reached, Middle(feet)), $"{Where(c, p)}: the flight is outside the envelope at t={t:0.00} (extra {extra:0.0} m/s) at {feet}");
                            }
                        }
                    } else if (p.kind == OB_PieceKind.Mover) {
                        for (float f = 0f; f <= 1f; f += 0.1f) {
                            arcs++;
                            samples++;
                            Vector3 top = p.position + p.vector * f + Vector3.up * (p.size.y * 0.5f);
                            Assert.IsTrue(env.Allows(reached, Middle(top)), $"{Where(c, p)}: riding the mover at {f:0.0} is outside the envelope");
                        }
                    } else if (p.kind == OB_PieceKind.Swing) {
                        float amp = p.vector.x * Mathf.Deg2Rad, rope = p.size.y;
                        for (float f = -1f; f <= 1f; f += 0.25f) {
                            arcs++;
                            samples++;
                            float a = amp * f;
                            Vector3 top = p.position + turn * new Vector3(0f, -rope * Mathf.Cos(a), rope * Mathf.Sin(a));
                            Assert.IsTrue(env.Allows(reached, Middle(top)), $"{Where(c, p)}: riding the swing at {f:0.00} is outside the envelope");
                        }
                    } else if (IsFloor(p) && i + 1 < c.pieces.Count) {
                        // A dash jump from this floor to the next floor of the stage: the straight line plus the jump's apex.
                        var q = c.pieces[i + 1];
                        if (!IsFloor(q) || q.stage != p.stage) continue;
                        float gap = Vector2.Distance(new Vector2(p.position.x, p.position.z), new Vector2(q.position.x, q.position.z));
                        if (gap > Move.sprintSpeed * 1.6f) continue;
                        arcs++;
                        for (float f = 0f; f <= 1f; f += 0.1f) {
                            Vector3 feet = Vector3.Lerp(p.position + Vector3.up * (p.size.y * 0.5f), q.position + Vector3.up * (q.size.y * 0.5f), f)
                                + Vector3.up * (4f * f * (1f - f) * Move.jumpHeight);
                            samples++;
                            Assert.IsTrue(env.Allows(reached, Middle(feet)), $"{Where(c, p)}: the dash jump to the next floor is outside the envelope at {f:0.0}");
                        }
                    }
                }
            }
            Assert.Greater(arcs, 800);
            Assert.Greater(samples, 8000);
        }

        [Test] public void SpeedSanityAcceptsLaunchesAndDashButNotTeleports() {
            float worst = 0f;
            foreach (var c in Courses(10))
                foreach (var p in c.pieces) {
                    if (p.kind != OB_PieceKind.Cannon && p.kind != OB_PieceKind.Ring && p.kind != OB_PieceKind.Boost && p.kind != OB_PieceKind.Spring) continue;
                    Vector3 v = p.vector;
                    worst = Mathf.Max(worst, new Vector2(v.x, v.z).magnitude);
                }
            // The fastest launch plus the Dash on top, kept over a window.
            float fastest = worst + Move.sprintSpeed;
            Assert.IsFalse(OB_SpeedSanity.Exceeds(Vector3.zero, new Vector3(0f, 90f, fastest * 0.5f), 0.5f, OB_SpeedSanity.DefaultLimit), $"launch {worst:0.0} plus dash ({fastest:0.0} m/s) must pass; climbing is ignored");
            Assert.Less(fastest, OB_SpeedSanity.DefaultLimit, "the default limit leaves room over the fastest launch plus the dash");
            Assert.IsTrue(OB_SpeedSanity.Exceeds(Vector3.zero, new Vector3(40f, 0f, 0f), 0.5f, OB_SpeedSanity.DefaultLimit), "a 40 m jump in half a second is a teleport");
        }

        [Test] public void EnvelopeMarginsMustBePositive() {
            Assert.Greater(Margins.margin, 0f);
            Assert.Greater(Margins.launchMargin, 0f);
        }
    }
}
