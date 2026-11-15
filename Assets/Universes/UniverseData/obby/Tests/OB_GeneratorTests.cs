using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.obby.Tests {
    public sealed class OB_GeneratorTests {
        static readonly Vector3 Start = new(0f, 100f, 9f), Lobby = new(0f, 100f, -3f);

        const int Stages = 30;

        static OB_CourseLayout Make(int seed, int stages = Stages) =>
            OB_CourseGenerator.Generate(seed, stages, OB_Movement.Default, Start, 0f, Lobby);

        [Test] public void SameSeedBuildsIdenticalCourse() {
            var a = Make(4242);
            var b = Make(4242);
            Assert.AreEqual(a.pieces.Count, b.pieces.Count);
            for (int i = 0; i < a.pieces.Count; i++) {
                Assert.AreEqual(a.pieces[i].kind, b.pieces[i].kind);
                Assert.AreEqual(a.pieces[i].position, b.pieces[i].position);
                Assert.AreEqual(a.pieces[i].size, b.pieces[i].size);
            }
            CollectionAssert.AreEqual(a.stages.Select(s => s.kind), b.stages.Select(s => s.kind));
        }

        [Test] public void DifferentSeedsBuildDifferentCourses() {
            var orders = new HashSet<string>();
            for (int seed = 1; seed <= 40; seed++) orders.Add(string.Join(",", Make(seed).stages.Select(s => s.kind)));
            Assert.Greater(orders.Count, 30);
        }

        [Test] public void StageCountIsHonouredAndClamped() {
            Assert.AreEqual(Stages, Make(7).StageCount);
            Assert.AreEqual(3, Make(7, 3).StageCount);
            Assert.AreEqual(OB_CourseGenerator.MaxStages, Make(7, 500).StageCount);
            Assert.GreaterOrEqual(Make(7, 0).StageCount, 1);
        }

        [Test] public void FirstStageIsGentleAndNoKindRepeatsBackToBack() {
            for (int seed = 1; seed <= 200; seed++) {
                var c = Make(seed * 7919);
                Assert.AreEqual(OB_StageKind.Hops, c.stages[0].kind, $"seed {seed}");
                for (int i = 1; i < c.StageCount; i++)
                    Assert.AreNotEqual(c.stages[i - 1].kind, c.stages[i].kind, $"seed {seed} stage {i}");
            }
        }

        [Test] public void EveryStageKindAndMechanicAppearsAcrossSeeds() {
            var kinds = new HashSet<OB_StageKind>();
            var pieces = new HashSet<OB_PieceKind>();
            for (int seed = 1; seed <= 60; seed++) {
                var c = Make(seed);
                foreach (var s in c.stages) kinds.Add(s.kind);
                foreach (var p in c.pieces) pieces.Add(p.kind);
            }
            foreach (OB_StageKind k in System.Enum.GetValues(typeof(OB_StageKind))) Assert.Contains(k, kinds.ToList());
            foreach (OB_PieceKind k in System.Enum.GetValues(typeof(OB_PieceKind))) Assert.Contains(k, pieces.ToList());
        }

        [Test] public void EveryRequiredJumpIsWithinReach() {
            int checks = 0;
            for (int seed = 1; seed <= 500; seed++) {
                foreach (var j in Make(seed * 31337).jumpChecks) {
                    checks++;
                    Assert.LessOrEqual(j.gap, j.maxGap + 1e-3f, $"seed {seed} stage {j.stage} gap");
                    Assert.LessOrEqual(j.rise, j.maxRise + 1e-3f, $"seed {seed} stage {j.stage} rise");
                }
            }
            Assert.Greater(checks, 1000);
        }

        [Test] public void StagesChainFromCheckpointsToFinish() {
            for (int seed = 1; seed <= 100; seed++) {
                var c = Make(seed);
                Assert.AreEqual(Lobby, c.stages[0].spawn);
                for (int i = 1; i < c.StageCount; i++) {
                    var s = c.stages[i];
                    Assert.IsTrue(s.checkpoint.Contains(s.spawn + Vector3.up * 0.5f), $"seed {seed} stage {i} spawn outside checkpoint");
                    Assert.Less(s.killY, s.spawn.y);
                }
                Assert.IsTrue(c.finish.Contains(c.finishTop + Vector3.up * 0.5f));
                Assert.AreEqual(1, c.pieces.Count(p => p.kind == OB_PieceKind.Finish));
                Assert.AreEqual(c.StageCount - 1, c.pieces.Count(p => p.kind == OB_PieceKind.CheckpointPad));
                Assert.AreEqual(c.StageCount, c.pieces.Count(p => p.kind == OB_PieceKind.Coin));
            }
        }

        /// <summary>
        /// A field that turns gravity must end flush against a solid surface in its gravity direction,
        /// so a player pulled across it lands on something instead of falling straight through.
        /// </summary>
        [Test] public void DirectionalGravityFieldsEndOnASurface() {
            int fields = 0;
            for (int seed = 1; seed <= 150; seed++) {
                var c = Make(seed);
                foreach (var zone in c.pieces.Where(p => p.kind == OB_PieceKind.GravityZone && p.vector != Vector3.zero)) {
                    fields++;
                    Quaternion toLocal = Quaternion.Inverse(Quaternion.Euler(0f, zone.yaw, 0f));
                    Vector3 down = toLocal * zone.vector;
                    int axis = Mathf.Abs(down.x) > 0.99f ? 0 : Mathf.Abs(down.y) > 0.99f ? 1 : 2;
                    Assert.Greater(Mathf.Abs(down[axis]), 0.99f, $"seed {seed}: field direction is not along a box axis");
                    float sign = Mathf.Sign(down[axis]);
                    float face = sign * zone.size[axis] * 0.5f;

                    bool supported = c.pieces.Any(p => {
                        if (p.kind != OB_PieceKind.Platform || p.stage != zone.stage) return false;
                        Vector3 centre = toLocal * (p.position - zone.position);
                        if (Mathf.Abs(centre[axis] - sign * p.size[axis] * 0.5f - face) > 0.01f) return false;
                        for (int other = 0; other < 3; other++) {
                            if (other == axis) continue;
                            float overlap = Mathf.Min(centre[other] + p.size[other] * 0.5f, zone.size[other] * 0.5f)
                                - Mathf.Max(centre[other] - p.size[other] * 0.5f, -zone.size[other] * 0.5f);
                            if (overlap < 1f) return false;
                        }
                        return true;
                    });
                    Assert.IsTrue(supported, $"seed {seed} stage {zone.stage}: nothing to stand on at the end of the gravity field");
                }
            }
            Assert.Greater(fields, 50);
        }

        [Test] public void GravityStageCoinsSitInsideTheirField() {
            for (int seed = 1; seed <= 150; seed++) {
                var c = Make(seed);
                foreach (var stage in c.stages.Where(s => s.kind is OB_StageKind.WallWalk or OB_StageKind.CeilingRun)) {
                    var zone = c.pieces.Single(p => p.stage == stage.index && p.kind == OB_PieceKind.GravityZone);
                    Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, zone.yaw, 0f)) * (stage.coin - zone.position);
                    Assert.IsTrue(new Bounds(Vector3.zero, zone.size).Contains(local), $"seed {seed} stage {stage.index}: coin outside the field");
                }
            }
        }

        [Test] public void TiersRunFromBasicToExtremeInOrder() {
            foreach (int n in new[] { 6, 14, 30, 37, OB_CourseGenerator.MaxStages }) {
                var c = Make(11, n);
                Assert.AreEqual(OB_Tier.Basic, c.stages[0].tier, $"{n} stages");
                Assert.AreEqual(OB_Tier.Extreme, c.stages[n - 1].tier, $"{n} stages");
                for (int i = 1; i < n; i++) {
                    int step = c.stages[i].tier - c.stages[i - 1].tier;
                    Assert.IsTrue(step is 0 or 1, $"{n} stages: tier jumps at stage {i}");
                }
                for (int t = 0; t < OB_Tiers.Count; t++)
                    Assert.AreEqual(t, (int)c.stages[OB_Tiers.FirstStage(t, n)].tier, $"{n} stages: first stage of tier {t}");
            }
        }

        [Test] public void DifficultyClimbsThroughEachTierAndEasesOffAtTheNext() {
            var c = Make(5);
            Assert.AreEqual(0f, c.stages[0].difficulty, 1e-4f);
            Assert.Greater(c.stages[Stages - 1].difficulty, 0.95f);
            int breathers = 0;
            for (int i = 1; i < Stages; i++) {
                float previous = c.stages[i - 1].difficulty, current = c.stages[i].difficulty;
                if (c.stages[i].tier == c.stages[i - 1].tier) Assert.Greater(current, previous, $"stage {i}");
                else {
                    breathers++;
                    Assert.Less(current, previous, $"stage {i}: a new tier should start with a breather");
                    Assert.Greater(current, c.stages[OB_Tiers.FirstStage((int)c.stages[i - 1].tier, Stages)].difficulty,
                        $"stage {i}: each tier starts harder than the one before");
                }
            }
            Assert.AreEqual(OB_Tiers.Count - 1, breathers);
        }

        [Test] public void ExtremeStagesAreMuchHarderThanBasicOnes() {
            // Hops is the baseline stage: compare its jumps at the bottom and top of the curve.
            float basicGap = 0f, extremeGap = 0f;
            int basic = 0, extreme = 0;
            for (int seed = 1; seed <= 200; seed++) {
                var c = Make(seed);
                foreach (var j in c.jumpChecks) {
                    var s = c.stages[Mathf.Min(j.stage, Stages - 1)];
                    if (s.kind != OB_StageKind.Hops) continue;
                    if (s.tier == OB_Tier.Basic) { basicGap += j.gap; basic++; }
                    else if (s.tier == OB_Tier.Extreme) { extremeGap += j.gap; extreme++; }
                }
            }
            Assert.Greater(basic, 0);
            Assert.Greater(extreme, 0);
            Assert.Greater(extremeGap / extreme, 1.6f * basicGap / basic);
        }
    }
}
