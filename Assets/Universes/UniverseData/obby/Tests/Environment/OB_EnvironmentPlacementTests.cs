using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Universes.UniverseData.obby.Client;

namespace Universes.UniverseData.obby.Tests.Environment {
    /// <summary>
    /// The world around the course is placed from the course seed and must never get in a racer's way:
    /// scenery keeps clear of every piece and of the jump corridors between surfaces, dressing leaves the
    /// space above the route open, and the same seed always dresses the course the same way.
    /// </summary>
    public sealed class OB_EnvironmentPlacementTests {
        static readonly Vector3 Start = new(0f, 100f, 9f), Lobby = new(0f, 100f, -3f);
        const int Stages = 24;
        static readonly int[] Seeds = { 1, 7, 42, 4242, 20261115 };
        /// <summary>A stand-in for the landmark prefabs: footprint radius and height (m).</summary>
        static readonly Vector2 LandmarkSize = new(90f, 160f);
        const float Tolerance = 1e-3f;

        sealed class Planned {
            public OB_CourseLayout layout;
            public OB_CourseClearance clearance;
            public OB_ScenePlanner scene;
            public OB_DressingPlanner dressing;
        }

        static Planned Plan(int seed) {
            var planned = new Planned {
                layout = OB_CourseGenerator.Generate(seed, Stages, OB_Movement.Default, Start, 0f, Lobby),
                clearance = new OB_CourseClearance(),
                scene = new OB_ScenePlanner(),
                dressing = new OB_DressingPlanner()
            };
            // The order OB_Backdrop.Arrange uses: dressing reserves its space before scenery is scattered.
            planned.clearance.Build(planned.layout, Lobby, OB_ClearanceSettings.Default);
            planned.dressing.Plan(planned.layout, planned.clearance, OB_DressingSettings.Default);
            planned.scene.Plan(seed, planned.layout.StageCount, planned.clearance, OB_SceneSettings.Default, (tier, index) => LandmarkSize);
            return planned;
        }

        /// <summary>A piece's keep-out: its box, a margin round it and headroom above it.</summary>
        static Bounds KeepOut(in OB_Piece piece, in OB_ClearanceSettings s) {
            Bounds box = OB_CourseClearance.PieceBounds(piece, s.defaultPieceSize);
            box.SetMinMax(box.min - Vector3.one * s.margin, box.max + new Vector3(s.margin, s.headroom, s.margin));
            return box;
        }

        /// <summary>The open space above the route: columns along the line between consecutive surfaces.</summary>
        static List<Bounds> Corridor(OB_CourseClearance clearance, in OB_ClearanceSettings s) {
            const float aboveSurface = 0.5f;   // dressing may sit flush under a surface, never above it
            var columns = new List<Bounds>();
            var points = clearance.RoutePoints;
            for (int i = 1; i < points.Count; i++) {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(points[i - 1], points[i]) / s.routeStep));
                for (int k = 0; k <= steps; k++) {
                    Vector3 p = Vector3.Lerp(points[i - 1], points[i], k / (float)steps);
                    var column = new Bounds();
                    column.SetMinMax(p + new Vector3(-s.routeRadius, aboveSurface, -s.routeRadius), p + new Vector3(s.routeRadius, s.headroom, s.routeRadius));
                    columns.Add(column);
                }
            }
            return columns;
        }

        static bool Overlaps(Bounds a, Bounds b) {
            a.Expand(-Tolerance);
            return a.Intersects(b);
        }

        // ---------------------------------------------------------------- determinism
        [Test] public void AShortCourseStylesItsSceneryAfterOneTier() {
            var layout = OB_CourseGenerator.Generate(18747, 4, OB_Movement.Default, Start, 0f, Lobby);
            var clearance = new OB_CourseClearance();
            clearance.Build(layout, Lobby, OB_ClearanceSettings.Default);
            var scene = new OB_ScenePlanner();
            scene.Plan(layout.seed, layout.StageCount, clearance, OB_SceneSettings.Default, (tier, index) => LandmarkSize, OB_Tier.Insane);
            int styled = 0;
            foreach (var placement in scene.Placements) {
                if (placement.kind is not ((byte)OB_SceneryKind.Rock or (byte)OB_SceneryKind.Island or (byte)OB_SceneryKind.Cloud)) continue;
                Assert.AreEqual(OB_Tier.Insane, placement.tier, $"{(OB_SceneryKind)placement.kind} at {placement.bounds.center}");
                styled++;
            }
            Assert.Greater(styled, 0);
        }

        [Test] public void SameSeedPlansTheSameWorld() {
            var a = Plan(4242);
            var b = Plan(4242);
            AssertSame(a.scene.Placements, b.scene.Placements);
            AssertSame(a.dressing.Placements, b.dressing.Placements);
        }

        static void AssertSame(IReadOnlyList<OB_Placement> a, IReadOnlyList<OB_Placement> b) {
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) {
                Assert.AreEqual(a[i].kind, b[i].kind);
                Assert.AreEqual(a[i].variant, b[i].variant);
                Assert.AreEqual(a[i].tier, b[i].tier);
                Assert.AreEqual(a[i].matrix, b[i].matrix);
            }
        }

        [Test] public void DifferentSeedsPlanDifferentScenery() {
            var firsts = new HashSet<Vector3>();
            foreach (int seed in Seeds) {
                foreach (var placement in Plan(seed).scene.Placements)
                    if (placement.kind == (byte)OB_SceneryKind.Rock) {
                        firsts.Add(placement.bounds.center);
                        break;
                    }
            }
            Assert.AreEqual(Seeds.Length, firsts.Count);
        }

        [Test] public void PlanningIsRichEnoughToFillTheSky() {
            var planned = Plan(7);
            int rocks = 0, islands = 0, clouds = 0, dressing = planned.dressing.Placements.Count;
            foreach (var placement in planned.scene.Placements)
                switch ((OB_SceneryKind)placement.kind) {
                    case OB_SceneryKind.Rock: rocks++; break;
                    case OB_SceneryKind.Island: islands++; break;
                    case OB_SceneryKind.Cloud: clouds++; break;
                }
            Assert.Greater(rocks, Stages * 4, "rocks");
            Assert.Greater(islands, Stages / 2, "islands");
            Assert.Greater(clouds, 20, "clouds");
            Assert.Greater(dressing, Stages * 4, "dressing");
        }

        // ---------------------------------------------------------------- clearance
        [Test] public void SceneryKeepsClearOfEveryPieceAndTheRoute() {
            var s = OB_ClearanceSettings.Default;
            foreach (int seed in Seeds) {
                var planned = Plan(seed);
                var corridor = Corridor(planned.clearance, s);
                // Keep-outs once per course; messages only on a failure (formatting one per check is what made this slow).
                var keepOuts = new List<Bounds>(planned.layout.pieces.Count);
                foreach (var piece in planned.layout.pieces) keepOuts.Add(KeepOut(piece, s));
                foreach (var placement in planned.scene.Placements) {
                    if (placement.kind == (byte)OB_SceneryKind.Landmark) continue;
                    Vector3 centre = placement.bounds.center;
                    float radius = placement.bounds.extents.x;
                    float reach = Mathf.Max(0f, radius - Tolerance), reachSq = reach * reach;
                    for (int i = 0; i < keepOuts.Count; i++)
                        if (keepOuts[i].SqrDistance(centre) < reachSq)
                            Assert.Fail($"seed {seed}: {(OB_SceneryKind)placement.kind} at {centre} reaches {planned.layout.pieces[i].kind} at {planned.layout.pieces[i].position}");
                    foreach (var column in corridor)
                        if (column.SqrDistance(centre) < reachSq)
                            Assert.Fail($"seed {seed}: {(OB_SceneryKind)placement.kind} at {centre} reaches the route at {column.center}");
                }
            }
        }

        [Test] public void LandmarksStandFarOffAndApart() {
            var s = OB_ClearanceSettings.Default;
            var scene = OB_SceneSettings.Default;
            foreach (int seed in Seeds) {
                var planned = Plan(seed);
                var placed = new List<OB_Placement>();
                foreach (var placement in planned.scene.Placements) {
                    if (placement.kind != (byte)OB_SceneryKind.Landmark) continue;
                    Vector3 origin = placement.matrix.GetColumn(3);
                    // Its whole column, from the base island to the top, keeps its distance.
                    foreach (Vector3 point in new[] { origin, origin + Vector3.up * LandmarkSize.y })
                        foreach (var piece in planned.layout.pieces)
                            Assert.GreaterOrEqual(Mathf.Sqrt(KeepOut(piece, s).SqrDistance(point)) + Tolerance, LandmarkSize.x + scene.landmarkClearance,
                                $"seed {seed}: a {placement.tier} landmark stands too close to {piece.kind} at {piece.position}");
                    foreach (var other in placed) {
                        if (other.tier != placement.tier) continue;
                        Vector3 otherOrigin = other.matrix.GetColumn(3);
                        Vector2 apart = new(origin.x - otherOrigin.x, origin.z - otherOrigin.z);
                        Assert.GreaterOrEqual(apart.magnitude, LandmarkSize.x * 2f, $"seed {seed}: two {placement.tier} landmarks overlap");
                    }
                    placed.Add(placement);
                }
            }
        }

        [Test] public void EveryTierOnTheCourseGetsALandmark() {
            foreach (int seed in Seeds) {
                var planned = Plan(seed);
                var tiers = new HashSet<OB_Tier>();
                foreach (var stage in planned.layout.stages) tiers.Add(stage.tier);
                foreach (var placement in planned.scene.Placements)
                    if (placement.kind == (byte)OB_SceneryKind.Landmark) tiers.Remove(placement.tier);
                Assert.IsEmpty(tiers, $"seed {seed}: tiers without a landmark");
            }
        }

        [Test] public void DressingLeavesTheSpaceAboveTheRouteOpen() {
            var s = OB_ClearanceSettings.Default;
            foreach (int seed in Seeds) {
                var planned = Plan(seed);
                var corridor = Corridor(planned.clearance, s);
                foreach (var placement in planned.dressing.Placements) {
                    var prop = (OB_DressingProp)placement.kind;
                    // The route runs through checkpoint arches on purpose; their banners hang off the posts.
                    if (prop is OB_DressingProp.Arch or OB_DressingProp.Banner) continue;
                    foreach (var column in corridor)
                        if (Overlaps(placement.bounds, column))
                            Assert.Fail($"seed {seed}: {prop} at {placement.bounds.center} enters the route at {column.center}");
                }
            }
        }

        [Test] public void DressingStaysOutOfHazardsAndGravityTurns() {
            var s = OB_ClearanceSettings.Default;
            var parts = new List<Bounds>();
            foreach (int seed in Seeds) {
                var planned = Plan(seed);
                foreach (var piece in planned.layout.pieces) {
                    bool turnsGravity = piece.kind is OB_PieceKind.GravityZone or OB_PieceKind.GravityFlip && piece.vector != Vector3.zero;
                    bool hazard = piece.kind is OB_PieceKind.SpinnerBar or OB_PieceKind.SpinnerCross or OB_PieceKind.Pendulum
                        or OB_PieceKind.Crusher or OB_PieceKind.Laser;
                    if (!turnsGravity && !hazard) continue;
                    Bounds box = OB_CourseClearance.PieceBounds(piece, s.defaultPieceSize);
                    Bounds wide = box;
                    if (turnsGravity) wide.Expand(s.turnedGravityMargin * 2f);
                    foreach (var placement in planned.dressing.Placements) {
                        var prop = (OB_DressingProp)placement.kind;
                        // Arches and their banners dissolve near the camera: around a gravity turn they only stay out of the piece itself.
                        bool dissolves = prop is OB_DressingProp.Arch or OB_DressingProp.Banner;
                        Bounds keepOut = dissolves ? box : wide;
                        parts.Clear();
                        if (prop == OB_DressingProp.Arch) OB_DressingPlanner.ArchParts(placement.matrix, OB_DressingSettings.Default, parts);
                        else parts.Add(placement.bounds);
                        foreach (var part in parts)
                            if (Overlaps(part, keepOut))
                                Assert.Fail($"seed {seed}: {prop} at {part.center} enters {piece.kind} at {piece.position}");
                    }
                }
            }
        }

        [Test] public void CheckpointPadsKeepTheirArches() {
            int pads = 0, arched = 0;
            foreach (int seed in Seeds) {
                var planned = Plan(seed);
                foreach (var piece in planned.layout.pieces) {
                    if (piece.kind != OB_PieceKind.CheckpointPad) continue;
                    pads++;
                    foreach (var placement in planned.dressing.Placements)
                        if (placement.kind == (byte)OB_DressingProp.Arch && Vector3.Distance(placement.matrix.GetColumn(3), piece.position) < OB_CourseGenerator.PadSize) {
                            arched++;
                            break;
                        }
                }
            }
            // A pad standing inside a gravity field or hard against a turntable may go without; the fallbacks cover the rest.
            Assert.GreaterOrEqual(arched, pads * 0.85f, $"{arched} of {pads} checkpoint pads have an arch");
        }

        // ---------------------------------------------------------------- keep-out shapes of moving pieces
        [Test] public void PendulumKeepOutCoversItsWholeSwing() {
            var pendulum = new OB_Piece {
                kind = OB_PieceKind.Pendulum, position = new Vector3(0f, 50f, 0f), size = new Vector3(3f, 12f, 3f), vector = new Vector3(60f, 0f, 0f)
            };
            Bounds keepOut = OB_CourseClearance.PieceBounds(pendulum, Vector3.one);
            float arm = pendulum.size.y, swing = 60f * Mathf.Deg2Rad;
            foreach (float angle in new[] { -swing, 0f, swing }) {
                Vector3 head = pendulum.position + new Vector3(Mathf.Sin(angle), -Mathf.Cos(angle), 0f) * arm;
                Assert.IsTrue(keepOut.Contains(head), $"head at {angle * Mathf.Rad2Deg} degrees");
                Vector3 across = pendulum.position + new Vector3(0f, -Mathf.Cos(angle), Mathf.Sin(angle)) * arm;
                Assert.IsTrue(keepOut.Contains(across), $"head swinging the other way at {angle * Mathf.Rad2Deg} degrees");
            }
        }

        [Test] public void TurntableKeepOutSitsUnderItsTop() {
            var turntable = new OB_Piece { kind = OB_PieceKind.Turntable, position = new Vector3(0f, 20f, 0f), size = new Vector3(10f, 1f, 10f) };
            Bounds keepOut = OB_CourseClearance.PieceBounds(turntable, Vector3.one);
            Assert.AreEqual(turntable.position.y, keepOut.max.y, 1e-4f);
            Assert.GreaterOrEqual(keepOut.extents.x, turntable.size.x * 0.5f);
        }

        [Test] public void CrusherKeepOutCoversItsDrop() {
            var crusher = new OB_Piece { kind = OB_PieceKind.Crusher, position = new Vector3(0f, 30f, 0f), size = new Vector3(4f, 2f, 4f), vector = new Vector3(0f, 6f, 0f) };
            var layout = new OB_CourseLayout { seed = 1, stages = new[] { new OB_Stage { tier = OB_Tier.Basic, spawn = crusher.position } } };
            layout.pieces.Add(crusher);
            var clearance = new OB_CourseClearance();
            clearance.Build(layout, new Vector3(500f, 0f, 500f), OB_ClearanceSettings.Default);
            Vector3 bottom = crusher.position + Vector3.down * (crusher.size.y * 0.5f + 6f - 0.1f);
            Assert.AreEqual(0f, clearance.Clearance(bottom, 1f), 1e-4f, "the crusher's lowest point is kept clear");
        }
    }
}
