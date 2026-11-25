using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.obby.Tests {
    public sealed class OB_GeneratorTests {
        static readonly Vector3 Start = new(0f, 100f, 9f), Lobby = new(0f, 100f, -3f);
        static readonly OB_Movement Move = OB_Movement.Default;
        static readonly OB_CourseRules Rules = OB_CourseRules.Default;

        const int Stages = OB_CourseGenerator.DefaultStageCount;
        /// <summary>Course lengths the sweeping tests build: short, odd, the shipped length, the old one and the longest.</summary>
        static readonly int[] Lengths = { 6, 13, Stages, 30, OB_CourseGenerator.MaxStages };

        static OB_CourseLayout Make(int seed, int stages = Stages) =>
            OB_CourseGenerator.Generate(seed, stages, Move, Start, 0f, Lobby, Rules);

        /// <summary>Many courses: every length, many seeds.</summary>
        static IEnumerable<OB_CourseLayout> Courses(int seeds) {
            foreach (int n in Lengths)
                for (int seed = 1; seed <= seeds; seed++)
                    yield return Make(seed * 7919 + n, n);
        }

        static string Where(OB_CourseLayout c, int stage, int segment) {
            if (stage >= c.StageCount) return $"seed {c.seed} finish";
            var s = c.stages[stage];
            return $"seed {c.seed} ({c.StageCount} stages) stage {stage} {s.tier} {(segment >= 0 ? s.segments[segment].ToString() : "-")}";
        }

        // ---------------------------------------------------------------- determinism and shape
        [Test] public void SameSeedBuildsIdenticalCourse() {
            var a = Make(4242);
            var b = Make(4242);
            Assert.AreEqual(a.pieces.Count, b.pieces.Count);
            for (int i = 0; i < a.pieces.Count; i++) {
                Assert.AreEqual(a.pieces[i].kind, b.pieces[i].kind);
                Assert.AreEqual(a.pieces[i].position, b.pieces[i].position);
                Assert.AreEqual(a.pieces[i].size, b.pieces[i].size);
                Assert.AreEqual(a.pieces[i].channel, b.pieces[i].channel);
            }
            for (int s = 0; s < a.StageCount; s++) {
                CollectionAssert.AreEqual(a.stages[s].segments, b.stages[s].segments);
                Assert.AreEqual(a.stages[s].hasStar, b.stages[s].hasStar);
                Assert.AreEqual(a.stages[s].par, b.stages[s].par);
            }
        }

        [Test] public void DifferentSeedsBuildDifferentCourses() {
            var orders = new HashSet<string>();
            for (int seed = 1; seed <= 40; seed++)
                orders.Add(string.Join("|", Make(seed).stages.Select(s => string.Join(",", s.segments))));
            Assert.Greater(orders.Count, 38);
        }

        [Test] public void StageCountIsHonouredAndClamped() {
            Assert.AreEqual(Stages, Make(7).StageCount);
            Assert.AreEqual(3, Make(7, 3).StageCount);
            Assert.AreEqual(OB_CourseGenerator.MaxStages, Make(7, 500).StageCount);
            Assert.GreaterOrEqual(Make(7, 0).StageCount, 1);
        }

        [Test] public void StagesChainSegmentsByTheirTierRule() {
            foreach (var c in Courses(40)) {
                Assert.AreEqual(OB_StageKind.Hops, c.stages[0].segments[0], $"seed {c.seed}: the lobby run starts with plain hops");
                for (int i = 0; i < c.StageCount; i++) {
                    var s = c.stages[i];
                    var rule = Rules.Tier(s.tier);
                    Assert.That(s.segments.Length, Is.InRange(rule.segments.x, rule.segments.y), Where(c, i, -1));
                    Assert.AreEqual(s.segments.Length, s.segments.Distinct().Count(), $"{Where(c, i, -1)}: a mechanic repeats inside the stage");
                    Assert.LessOrEqual(s.segments.Count(OB_CourseGenerator.IsGravity), 1, $"{Where(c, i, -1)}: two gravity mechanics");
                    Assert.Contains(s.kind, s.segments, Where(c, i, -1));
                    if (i > 0) Assert.AreNotEqual(c.stages[i - 1].kind, s.kind, $"{Where(c, i, -1)}: two stages in a row share a name");
                    // Only mechanics this tier or an earlier one introduces.
                    foreach (var kind in s.segments) {
                        int introduced = Enumerable.Range(0, OB_Tiers.Count).First(t => Rules.tiers[t].introduces.Contains(kind) || t == OB_Tiers.Count - 1);
                        Assert.LessOrEqual(introduced, (int)s.tier, $"{Where(c, i, -1)}: {kind} before its tier");
                    }
                }
            }
        }

        [Test] public void EachTierOpensWithOneOfItsNewMechanics() {
            foreach (var c in Courses(20)) {
                for (int t = 1; t < OB_Tiers.Count; t++) {
                    var introduces = Rules.tiers[t].introduces;
                    if (introduces == null || introduces.Length == 0) continue;
                    var first = c.stages[OB_Tiers.FirstStage(t, c.StageCount)];
                    Assert.Contains(first.segments[0], introduces, $"seed {c.seed} ({c.StageCount} stages) tier {(OB_Tier)t}");
                }
            }
        }

        [Test] public void EveryMechanicAndPieceAppearsAcrossSeeds() {
            var kinds = new HashSet<OB_StageKind>();
            var pieces = new HashSet<OB_PieceKind>();
            for (int seed = 1; seed <= 30; seed++) {
                var c = Make(seed);
                foreach (var s in c.stages) kinds.UnionWith(s.segments);
                foreach (var p in c.pieces) pieces.Add(p.kind);
            }
            foreach (OB_StageKind k in System.Enum.GetValues(typeof(OB_StageKind))) Assert.Contains(k, kinds.ToList());
            foreach (OB_PieceKind k in System.Enum.GetValues(typeof(OB_PieceKind))) Assert.Contains(k, pieces.ToList());
        }

        [Test] public void StagesChainFromCheckpointsToFinish() {
            foreach (var c in Courses(25)) {
                Assert.AreEqual(Lobby, c.stages[0].spawn);
                for (int i = 1; i < c.StageCount; i++) {
                    var s = c.stages[i];
                    Assert.IsTrue(s.checkpoint.Contains(s.spawn + Vector3.up * 0.5f), $"{Where(c, i, -1)}: spawn outside checkpoint");
                    Assert.Less(s.killY, s.spawn.y);
                    Assert.Greater(s.killTop, s.spawn.y);
                }
                Assert.IsTrue(c.finish.Contains(c.finishTop + Vector3.up * 0.5f));
                Assert.AreEqual(1, c.pieces.Count(p => p.kind == OB_PieceKind.Finish));
                Assert.AreEqual(c.StageCount - 1, c.pieces.Count(p => p.kind == OB_PieceKind.CheckpointPad));
                Assert.AreEqual(c.StageCount, c.pieces.Count(p => p.kind == OB_PieceKind.Coin));
                Assert.AreEqual(c.StarCount, c.pieces.Count(p => p.kind == OB_PieceKind.Star));
                Assert.IsTrue(c.pieces.All(p => p.segment < 0 || p.segment < c.stages[Mathf.Min(p.stage, c.StageCount - 1)].segments.Length));
            }
        }

        // ---------------------------------------------------------------- jumps and the difficulty curve
        [Test] public void EveryRequiredJumpIsWithinTheTierAndPhysicalLimits() {
            int checks = 0;
            foreach (var c in Courses(120)) {
                foreach (var j in c.jumpChecks) {
                    checks++;
                    string at = Where(c, j.stage, j.segment) + (j.pro ? " (pro)" : "");
                    Assert.LessOrEqual(j.gap, j.maxGap + 1e-3f, $"{at}: gap");
                    Assert.LessOrEqual(j.rise, j.maxRise + 1e-3f, $"{at}: rise");
                    // Whatever the tier rules say, a jump never asks for more than a sprint jump's reach.
                    if (j.airTime > 0f) {
                        Assert.LessOrEqual(j.gap, Move.walkSpeed * j.airTime * Rules.physicalGapCeiling + 1e-3f, $"{at}: air steering");
                        continue;
                    }
                    float flight = Move.LandTime(j.rise, j.gravityScale, j.launchSpeed);
                    Assert.Greater(flight, 0f, $"{at}: the landing is out of reach");
                    Assert.LessOrEqual(j.gap, Move.sprintSpeed * flight * Rules.physicalGapCeiling + 1e-3f, $"{at}: physical gap");
                    Assert.LessOrEqual(j.rise, Move.Apex(j.gravityScale, j.launchSpeed) * Rules.physicalRiseCeiling + 1e-3f, $"{at}: physical rise");
                }
            }
            Assert.Greater(checks, 20000);
        }

        [Test] public void EveryRequiredJumpIsFairAtWalkSpeedAndOnlyProRoutesAskForADash() {
            int required = 0, pro = 0, dashOnly = 0;
            foreach (var c in Courses(60)) {
                foreach (var j in c.jumpChecks) {
                    string at = Where(c, j.stage, j.segment) + (j.pro ? " (pro)" : "");
                    if (j.pro) {
                        pro++;
                        Assert.GreaterOrEqual(j.speed, Move.walkSpeed, at);
                        Assert.LessOrEqual(j.speed, Move.sprintSpeed, at);
                        if (j.airTime <= 0f && j.gap > Move.walkSpeed * Move.LandTime(j.rise, j.gravityScale, j.launchSpeed) * Rules.requiredGapCeiling) dashOnly++;
                        continue;
                    }
                    required++;
                    Assert.LessOrEqual(j.speed, Move.walkSpeed + 1e-3f, $"{at}: required jumps are designed for walk speed");
                    if (j.airTime > 0f) continue;
                    float walkReach = Move.walkSpeed * Move.LandTime(j.rise, j.gravityScale, j.launchSpeed);
                    Assert.LessOrEqual(j.gap, walkReach * Rules.requiredGapCeiling + 1e-3f, $"{at}: a walking jump cannot cross {j.gap:F2} m (reach {walkReach:F2} m)");
                }
            }
            Assert.Greater(required, 10000);
            Assert.Greater(pro, 100);
            Assert.Greater(dashOnly, pro / 4, "pro routes are where a dash pays off");
        }

        /// <summary>The hardest main-path jump of each tier, as a fraction of a full WALKING jump.</summary>
        static float[] HardestJumps(int seeds) {
            var hardest = new float[OB_Tiers.Count];
            for (int seed = 1; seed <= seeds; seed++) {
                var c = Make(seed);
                foreach (var j in c.jumpChecks) {
                    if (j.pro || j.airTime > 0f || j.stage >= c.StageCount) continue;
                    float fraction = j.gap / (Move.walkSpeed * Move.LandTime(j.rise, j.gravityScale, j.launchSpeed));
                    int t = (int)c.stages[j.stage].tier;
                    hardest[t] = Mathf.Max(hardest[t], fraction);
                }
            }
            return hardest;
        }

        [Test] public void BasicHasRealJumpsAndExtremeIsASkillTest() {
            float[] hardest = HardestJumps(150);
            Assert.Greater(hardest[(int)OB_Tier.Basic], 0.45f, "Basic still asks for real jumps");
            Assert.Less(hardest[(int)OB_Tier.Basic], 0.7f, "Basic stays forgiving");
            Assert.Greater(hardest[(int)OB_Tier.Hard], 0.65f, "Hard needs a clean take-off");
            Assert.Greater(hardest[(int)OB_Tier.Extreme], 0.82f, "Extreme goes near the limit of a walking jump");
            for (int t = 1; t < OB_Tiers.Count; t++)
                Assert.Greater(hardest[t], hardest[t - 1], $"tier {(OB_Tier)t} is harder than the one before");
        }

        [Test] public void ExtremeStagesAreMuchHarderThanBasicOnes() {
            float basicGap = 0f, extremeGap = 0f, basicSize = 0f, extremeSize = 0f;
            int basic = 0, extreme = 0, basicPlatforms = 0, extremePlatforms = 0;
            for (int seed = 1; seed <= 150; seed++) {
                var c = Make(seed);
                foreach (var j in c.jumpChecks) {
                    if (j.pro || j.airTime > 0f || j.stage >= Stages || c.stages[j.stage].segments[Mathf.Max(0, j.segment)] != OB_StageKind.Hops) continue;
                    if (c.stages[j.stage].tier == OB_Tier.Basic) { basicGap += j.gap; basic++; }
                    else if (c.stages[j.stage].tier == OB_Tier.Extreme) { extremeGap += j.gap; extreme++; }
                }
                foreach (var p in c.pieces) {
                    if (p.kind != OB_PieceKind.Platform || p.stage >= Stages || p.segment < 0 || c.stages[p.stage].segments[p.segment] != OB_StageKind.Hops) continue;
                    if (c.stages[p.stage].tier == OB_Tier.Basic) { basicSize += p.size.x; basicPlatforms++; }
                    else if (c.stages[p.stage].tier == OB_Tier.Extreme) { extremeSize += p.size.x; extremePlatforms++; }
                }
            }
            Assert.Greater(basic, 0);
            Assert.Greater(extreme, 0);
            Assert.Greater(extremeGap / extreme, 1.6f * basicGap / basic, "Extreme gaps are much longer");
            Assert.Less(extremeSize / extremePlatforms, 0.6f * basicSize / basicPlatforms, "Extreme platforms are much smaller");
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

        [Test] public void ParGrowsWithTheTiers() {
            var perTier = new float[OB_Tiers.Count];
            var counts = new int[OB_Tiers.Count];
            for (int seed = 1; seed <= 40; seed++) {
                var c = Make(seed);
                Assert.AreEqual(c.stages.Sum(s => s.par), c.TotalPar, 1e-2f);
                Assert.AreEqual(0f, c.ParBefore(0));
                foreach (var s in c.stages) {
                    Assert.Greater(s.par, 1f, $"seed {seed} stage {s.index}: par");
                    perTier[(int)s.tier] += s.par;
                    counts[(int)s.tier]++;
                }
            }
            Assert.Greater(perTier[(int)OB_Tier.Extreme] / counts[(int)OB_Tier.Extreme], 2f * perTier[0] / counts[0],
                "four-segment Extreme stages take much longer than two-segment Basic ones");
        }

        // ---------------------------------------------------------------- layout
        [Test] public void SegmentsAndStagesNeverOverlap() {
            int pairs = 0;
            foreach (var c in Courses(30)) {
                var solid = new List<(OB_Piece piece, OB_Volume volume)>();
                foreach (var p in c.pieces)
                    if (OB_PieceVolumes.TryGet(p, out var v)) solid.Add((p, v));
                for (int a = 0; a < solid.Count; a++) {
                    for (int b = a + 1; b < solid.Count; b++) {
                        var pa = solid[a].piece;
                        var pb = solid[b].piece;
                        if (Mathf.Abs(pa.stage - pb.stage) > 1 || (pa.stage == pb.stage && pa.segment == pb.segment)) continue;
                        pairs++;
                        Assert.IsFalse(solid[a].volume.Overlaps(solid[b].volume, -0.05f),
                            $"{Where(c, pa.stage, pa.segment)} {pa.kind} overlaps {Where(c, pb.stage, pb.segment)} {pb.kind}");
                    }
                }
            }
            Assert.Greater(pairs, 100000);
        }

        /// <summary>
        /// A field that turns gravity must end flush against a solid surface in its gravity direction,
        /// so a player pulled across it lands on something instead of falling straight through.
        /// </summary>
        [Test] public void DirectionalGravityFieldsEndOnASurface() {
            int fields = 0;
            for (int seed = 1; seed <= 120; seed++) {
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
                    Assert.IsTrue(supported, $"{Where(c, zone.stage, zone.segment)}: nothing to stand on at the end of the gravity field");
                }
            }
            Assert.Greater(fields, 50);
        }

        [Test] public void WallAndCeilingCoinsSitInsideTheirField() {
            int coins = 0;
            for (int seed = 1; seed <= 150; seed++) {
                var c = Make(seed);
                foreach (var stage in c.stages) {
                    var kind = stage.segments[stage.coinSegment];
                    if (kind is not (OB_StageKind.WallWalk or OB_StageKind.CeilingRun)) continue;
                    coins++;
                    bool inside = c.pieces.Any(zone => zone.kind == OB_PieceKind.GravityZone && zone.stage == stage.index && zone.segment == stage.coinSegment
                        && new Bounds(Vector3.zero, zone.size).Contains(Quaternion.Inverse(Quaternion.Euler(0f, zone.yaw, 0f)) * (stage.coin - zone.position)));
                    Assert.IsTrue(inside, $"{Where(c, stage.index, stage.coinSegment)}: coin outside the field");
                }
            }
            Assert.Greater(coins, 10);
        }

        // ---------------------------------------------------------------- stars
        [Test] public void EveryTierHidesItsStars() {
            foreach (var c in Courses(40)) {
                for (int t = 0; t < OB_Tiers.Count; t++) {
                    int stages = OB_Tiers.FirstStage(t + 1, c.StageCount) - OB_Tiers.FirstStage(t, c.StageCount);
                    Assert.AreEqual(Mathf.Min(Rules.starsPerTier, stages), c.StarsInTier((OB_Tier)t), $"seed {c.seed} ({c.StageCount} stages) tier {(OB_Tier)t}");
                }
            }
        }

        [Test] public void StarsWaitAtTheEndOfAHarderProRoute() {
            int stars = 0;
            foreach (var c in Courses(30)) {
                foreach (var s in c.stages.Where(s => s.hasStar)) {
                    stars++;
                    // On top of a pro-route platform of the same stage...
                    bool perched = c.pieces.Any(p => p.kind == OB_PieceKind.Platform && p.stage == s.index
                        && Mathf.Abs(p.position.y + p.size.y * 0.5f + OB_CourseGenerator.StarLift - s.star.y) < 0.01f
                        && new Vector2(p.position.x - s.star.x, p.position.z - s.star.z).magnitude < 0.01f);
                    Assert.IsTrue(perched, $"{Where(c, s.index, -1)}: the star floats over nothing");
                    // ...reached by recorded pro jumps, which are harder than the stage's main path allows.
                    var pro = c.jumpChecks.Where(j => j.stage == s.index && j.pro).ToList();
                    Assert.GreaterOrEqual(pro.Count, Rules.Tier(s.tier).proHops, $"{Where(c, s.index, -1)}: no recorded pro route");
                    var rule = Rules.Tier(s.tier);
                    Assert.Greater(rule.proGapFraction, rule.gapFraction.y, $"tier {s.tier}: pro routes are harder");
                    Assert.Greater(pro.Max(j => j.gap / j.maxGap), 0.75f, $"{Where(c, s.index, -1)}: the pro route is no test");
                }
            }
            Assert.Greater(stars, 500);
        }

        // ---------------------------------------------------------------- mechanics
        static IEnumerable<(OB_CourseLayout course, OB_Piece piece)> PiecesOf(OB_PieceKind kind, int seeds) {
            for (int seed = 1; seed <= seeds; seed++) {
                var c = Make(seed);
                foreach (var p in c.pieces)
                    if (p.kind == kind) yield return (c, p);
            }
        }

        /// <summary>The top of the highest standable piece of the same stage under <paramref name="point"/> (−∞ when none).</summary>
        static float FloorUnder(OB_CourseLayout c, int stage, Vector3 point) {
            float best = float.NegativeInfinity;
            foreach (var p in c.pieces) {
                if (p.stage != stage || p.kind is not (OB_PieceKind.Platform or OB_PieceKind.Beam or OB_PieceKind.OnOffBlock)) continue;
                Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, p.yaw, 0f)) * (point - p.position);
                float top = p.position.y + p.size.y * 0.5f;
                if (Mathf.Abs(local.x) <= p.size.x * 0.5f && Mathf.Abs(local.z) <= p.size.z * 0.5f && top <= point.y + 0.01f) best = Mathf.Max(best, top);
            }
            return best;
        }

        [Test] public void OnOffBlocksHaveSwitchesOnTheirChannelInBothColours() {
            int segments = 0;
            for (int seed = 1; seed <= 60; seed++) {
                var c = Make(seed);
                foreach (var group in c.pieces.Where(p => p.kind == OB_PieceKind.OnOffBlock).GroupBy(p => p.channel)) {
                    segments++;
                    var switches = c.pieces.Where(p => p.kind == OB_PieceKind.OnOffSwitch && p.channel == group.Key).ToList();
                    Assert.GreaterOrEqual(switches.Count, 2, $"seed {seed} channel {group.Key}: a run without its switch");
                    Assert.IsTrue(group.Any(p => p.group == 0) && group.Any(p => p.group == 1), $"seed {seed} channel {group.Key}: one colour only");
                    Assert.AreEqual(1, group.Select(p => p.stage).Distinct().Count(), "a channel belongs to one segment");
                    // A "!" block is out of reach walking under it and in reach of a standing jump.
                    foreach (var block in switches.Where(p => p.group == 1)) {
                        float floor = FloorUnder(c, block.stage, block.position);
                        float underside = block.position.y - OB_CourseGenerator.SwitchBlockSize * 0.5f - floor;
                        Assert.Greater(underside, Move.bodyHeight, $"seed {seed}: walking under the switch block hits it");
                        Assert.Less(underside, Move.bodyHeight + Move.jumpHeight, $"seed {seed}: the switch block is out of reach");
                    }
                }
                var channels = c.pieces.Where(p => p.kind is OB_PieceKind.OnOffBlock or OB_PieceKind.OnOffSwitch).Select(p => (p.stage, p.segment, p.channel)).Distinct().ToList();
                Assert.AreEqual(channels.Select(x => x.channel).Distinct().Count(), channels.Count, $"seed {seed}: two segments share a channel");
            }
            Assert.Greater(segments, 20);
        }

        [Test] public void AForcedBumpRuleMakesEverySwitchABlockWithRoomToJumpPastIt() {
            // The rule a designer (or a capture) sets to see "!" blocks on every ON/OFF segment.
            var rules = OB_CourseRules.Default;
            rules.mechanics.onOff.bumpChance = 1f;
            rules.mechanics.onOff.bumpFromDifficulty = 0f;
            int blocks = 0;
            for (int seed = 1; seed <= 40; seed++) {
                var c = OB_CourseGenerator.Generate(seed, Stages, Move, Start, 0f, Lobby, rules);
                foreach (var sw in c.pieces.Where(p => p.kind == OB_PieceKind.OnOffSwitch)) {
                    Assert.AreEqual(1, sw.group, $"seed {seed}: a floor button under a forced bump rule");
                    blocks++;
                    // The platform under it reaches past the block on every side, so the jump to the
                    // next run can start from open sky, not from under the block.
                    var under = c.pieces.Where(p => p.kind == OB_PieceKind.Platform && p.stage == sw.stage).Where(p => {
                        Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, p.yaw, 0f)) * (sw.position - p.position);
                        return Mathf.Abs(local.x) <= p.size.x * 0.5f && Mathf.Abs(local.z) <= p.size.z * 0.5f && p.position.y < sw.position.y;
                    }).OrderByDescending(p => p.position.y).First();
                    Assert.GreaterOrEqual(Mathf.Min(under.size.x, under.size.z), OB_CourseGenerator.SwitchBlockSize + 2f, $"seed {seed}: the block covers its platform");
                }
            }
            Assert.Greater(blocks, 40);
        }

        [Test] public void HammersSwingThroughTheRobotAndClearTheBridge() {
            int hammers = 0;
            foreach (var (c, p) in PiecesOf(OB_PieceKind.Pendulum, 60)) {
                hammers++;
                float floor = FloorUnder(c, p.stage, p.position + Vector3.down * (p.size.y + p.size.x));
                Assert.Greater(floor, float.NegativeInfinity, $"{Where(c, p.stage, p.segment)}: hammer over nothing");
                float bottom = p.position.y - p.size.y - p.size.x * 0.5f;
                Assert.Greater(bottom - floor, 0.5f, $"{Where(c, p.stage, p.segment)}: the hammer scrapes the bridge");
                Assert.Less(bottom - floor, Move.bodyHeight * 0.6f, $"{Where(c, p.stage, p.segment)}: the hammer misses a robot");
                Assert.That(p.vector.x, Is.InRange(30f, 80f));
                Assert.Greater(p.period, 1f);
            }
            Assert.Greater(hammers, 30);
        }

        [Test] public void CrushersLandOnTheFloorAndClearTheRobotWhenRaised() {
            int crushers = 0;
            foreach (var (c, p) in PiecesOf(OB_PieceKind.Crusher, 60)) {
                crushers++;
                float underside = p.position.y - p.size.y * 0.5f;
                float floor = FloorUnder(c, p.stage, p.position);
                Assert.AreEqual(floor, underside - p.vector.y, 0.01f, $"{Where(c, p.stage, p.segment)}: the crusher stops short of the floor");
                Assert.Greater(underside - floor, Move.bodyHeight + 0.5f, $"{Where(c, p.stage, p.segment)}: no headroom when raised");
                Assert.Greater(p.period, p.speed + 1f, "a crusher has an open window");
            }
            Assert.Greater(crushers, 30);
        }

        [Test] public void LaserCurtainsCannotBeJumpedAndBlinkWithGaps() {
            int curtains = 0, beams = 0;
            foreach (var (c, p) in PiecesOf(OB_PieceKind.Laser, 60)) {
                float floor = FloorUnder(c, p.stage, p.position);
                if (p.period <= 0f) {
                    // An always-on low beam: low enough to jump.
                    beams++;
                    Assert.Less(p.position.y + p.size.y * 0.5f - floor, Move.jumpHeight * 0.6f, $"{Where(c, p.stage, p.segment)}: low beam too high");
                    continue;
                }
                curtains++;
                Assert.Greater(p.position.y + p.size.y * 0.5f - floor, Move.bodyHeight + Move.jumpHeight, $"{Where(c, p.stage, p.segment)}: curtain can be jumped");
                Assert.That(p.speed, Is.InRange(0.3f, 0.6f), "on fraction");
            }
            Assert.Greater(curtains, 50);
            Assert.Greater(beams, 5);
        }

        [Test] public void RisingLavaStopsUnderTheTopAndCanBeOutrun() {
            int chases = 0;
            foreach (var (c, p) in PiecesOf(OB_PieceKind.RisingLava, 80)) {
                chases++;
                float rest = p.position.y + p.size.y * 0.5f, stop = rest + p.vector.y;
                // The climb is the segment's platforms up to its top; a star's pro route may branch off after it.
                var platforms = c.pieces.Where(q => q.stage == p.stage && q.segment == p.segment && q.kind == OB_PieceKind.Platform).ToList();
                int last = platforms.FindIndex(q => Mathf.Abs(q.position.y + q.size.y * 0.5f - stop - OB_CourseGenerator.LavaStopBelow) < 0.01f);
                Assert.GreaterOrEqual(last, 0, $"{Where(c, p.stage, p.segment)}: the lava does not stop just under a platform");
                var climb = platforms.Take(last + 1).ToList();
                float top = climb.Max(q => q.position.y + q.size.y * 0.5f), bottom = climb.Min(q => q.position.y + q.size.y * 0.5f);
                Assert.Less(stop, top - 0.5f, $"{Where(c, p.stage, p.segment)}: the lava covers the top platform");
                Assert.Less(rest, bottom, $"{Where(c, p.stage, p.segment)}: the lava starts over the base");
                Assert.Greater(p.speed, 0f);
                // Every climb platform is over the footprint, so the chase covers the whole climb.
                foreach (var q in climb)
                    Assert.IsTrue(RyanAssets.Platformer.RisingLava.IsOver(q.position, p.position, p.yaw, new Vector2(p.size.x, p.size.z)),
                        $"{Where(c, p.stage, p.segment)}: a step outside the lava");
            }
            Assert.Greater(chases, 20);
        }

        [Test] public void RisingLavaLeavesAFastClimberTimeOnEveryStep() {
            // A fast climber: sprints from the footprint's near edge across the base, then makes every hop
            // in the longer of the run across (centre to centre) and the jump's flight. Each step must
            // still be clear of the lava when such a climber, a quarter slower, lands on it.
            int chases = 0;
            foreach (var (c, p) in PiecesOf(OB_PieceKind.RisingLava, 80)) {
                chases++;
                float rest = p.position.y + p.size.y * 0.5f, stop = rest + p.vector.y;
                var platforms = c.pieces.Where(q => q.stage == p.stage && q.segment == p.segment && q.kind == OB_PieceKind.Platform).ToList();
                int last = platforms.FindIndex(q => Mathf.Abs(q.position.y + q.size.y * 0.5f - stop - OB_CourseGenerator.LavaStopBelow) < 0.01f);
                Assert.GreaterOrEqual(last, 1);
                float t = platforms[0].size.z * 0.5f / Move.sprintSpeed;
                for (int i = 1; i <= last; i++) {
                    OB_Piece a = platforms[i - 1], b = platforms[i];
                    var run = b.position - a.position; run.y = 0f;
                    float rise = b.position.y - a.position.y;
                    t += Mathf.Max(run.magnitude / Move.sprintSpeed, Move.LandTime(Mathf.Min(rise, Move.Apex() * 0.95f)));
                    float lava = Mathf.Min(stop, rest + p.speed * Mathf.Max(0f, t * 1.25f - p.period));
                    Assert.Less(lava, b.position.y + b.size.y * 0.5f,
                        $"{Where(c, p.stage, p.segment)}: the lava is over step {i} before a fast climber lands on it");
                }
            }
            Assert.Greater(chases, 20);
        }

        [Test] public void GravityFieldsDropASprintingPlayerOntoTheirLanding() {
            // Leaving a wall walk or a corkscrew, normal gravity drops the player from the wall onto a
            // landing floor. Falling from the wall's top edge at a sprint must still come down on it.
            int landings = 0;
            foreach (var c in Courses(6))
                for (int st = 0; st < c.StageCount; st++)
                    for (int seg = 0; seg < c.stages[st].segments.Length; seg++) {
                        var kind = c.stages[st].segments[seg];
                        if (kind != OB_StageKind.Corkscrew && kind != OB_StageKind.WallWalk) continue;
                        var platforms = c.pieces.Where(q => q.stage == st && q.segment == seg && q.kind == OB_PieceKind.Platform).ToList();
                        var walls = platforms.Where(q => Mathf.Min(q.size.x, q.size.z) <= 1.01f && q.size.y > 2f).ToList();
                        Assert.IsNotEmpty(walls, Where(c, st, seg));
                        // The landing is the first floor after the last wall (a star's pro route may follow it).
                        var landing = platforms[platforms.IndexOf(walls[walls.Count - 1]) + 1];
                        float landingTop = landing.position.y + landing.size.y * 0.5f;
                        float drop = walls.Max(q => q.position.y + q.size.y * 0.5f) - landingTop;
                        float drift = Move.sprintSpeed * Mathf.Sqrt(2f * drop / Move.gravity);
                        Assert.GreaterOrEqual(landing.size.z, drift + 1f, $"{Where(c, st, seg)}: a sprinting fall from the wall overshoots the landing");
                        landings++;
                    }
            Assert.Greater(landings, 20);
        }

        [Test] public void CeilingRunsLeaveASprintingPlayerRoomToLandBeforeTheLava() {
            // Walking into a ceiling run's field, the player falls up onto the ceiling and drifts forward
            // as they go; holding sprint they must not come down on the first lava patch.
            int runs = 0;
            foreach (var c in Courses(6))
                for (int st = 0; st < c.StageCount; st++)
                    for (int seg = 0; seg < c.stages[st].segments.Length; seg++) {
                        if (c.stages[st].segments[seg] != OB_StageKind.CeilingRun) continue;
                        var inSeg = c.pieces.Where(q => q.stage == st && q.segment == seg).ToList();
                        var field = inSeg.First(q => q.kind == OB_PieceKind.GravityZone);
                        var floor = inSeg.First(q => q.kind == OB_PieceKind.Platform);
                        var frame = Quaternion.Inverse(Quaternion.Euler(0f, field.yaw, 0f));
                        float fieldStart = -field.size.z * 0.5f;
                        foreach (var lava in inSeg.Where(q => q.kind == OB_PieceKind.Lava)) {
                            float rise = lava.position.y - (floor.position.y + floor.size.y * 0.5f);
                            float drift = Move.sprintSpeed * Mathf.Sqrt(2f * rise / Move.gravity);
                            float nearEdge = (frame * (lava.position - field.position)).z - lava.size.z * 0.5f;
                            Assert.GreaterOrEqual(nearEdge - fieldStart, drift + 1f, $"{Where(c, st, seg)}: a sprinting fall up lands in the lava");
                        }
                        runs++;
                    }
            Assert.Greater(runs, 5);
        }

        [Test] public void MushroomsBounceHigherThanAJump() {
            int caps = 0;
            foreach (var (c, p) in PiecesOf(OB_PieceKind.Mushroom, 40)) {
                caps++;
                Assert.Greater(p.vector.y, Move.JumpSpeed, $"{Where(c, p.stage, p.segment)}");
                Assert.Greater(p.size.x, 1f);
            }
            Assert.Greater(caps, 30);
        }

        [Test] public void FlipStagesTurnGravityBackAndHaveAnUpperKillHeight() {
            int stages = 0;
            for (int seed = 1; seed <= 80; seed++) {
                var c = Make(seed);
                foreach (var s in c.stages.Where(s => s.segments.Contains(OB_StageKind.GravityFlip))) {
                    stages++;
                    var pads = c.pieces.Where(p => p.kind == OB_PieceKind.GravityFlip && p.stage == s.index).ToList();
                    int up = pads.Count(p => p.vector.y > 0.5f), down = pads.Count(p => p.vector.y < -0.5f);
                    Assert.AreEqual(up, down, $"{Where(c, s.index, -1)}: every flip up has a flip back");
                    Assert.Less(s.killTop, float.PositiveInfinity, $"{Where(c, s.index, -1)}: no upper kill height");
                    Assert.IsTrue(pads.All(p => p.position.y < s.killTop), $"{Where(c, s.index, -1)}: a flip pad above the kill height");
                }
            }
            Assert.Greater(stages, 10);
        }

        [Test] public void TurntablesCarryTwoToFourPosts() {
            int tables = 0;
            foreach (var (c, p) in PiecesOf(OB_PieceKind.Turntable, 40)) {
                tables++;
                Assert.That(p.group, Is.InRange(2, 4));
                Assert.Greater(Mathf.Abs(p.speed), 10f);
                Assert.GreaterOrEqual(p.size.x, 8f, "room to stand between the posts");
            }
            Assert.Greater(tables, 20);
        }

        [Test] public void WindGapsLeaveHeadwayAgainstTheStrongestGust() {
            int fields = 0, jumps = 0;
            for (int seed = 1; seed <= 60; seed++) {
                var c = Make(seed);
                foreach (var segment in c.pieces.Where(p => p.kind == OB_PieceKind.Wind).GroupBy(p => (p.stage, p.segment))) {
                    var windFields = segment.ToList();
                    float strongest = 0f;
                    foreach (var field in windFields) {
                        fields++;
                        strongest = Mathf.Max(strongest, field.vector.magnitude + field.speed);
                        Assert.Less(Mathf.Abs(field.vector.y), 1e-3f, "wind blows across, never up");
                        Assert.Greater(field.period, 0f);
                    }
                    string at = Where(c, segment.Key.stage, segment.Key.segment);
                    Assert.Less(strongest, Move.walkSpeed, $"{at}: walking cannot hold against the gust");
                    // The segment's first jumps are the windy ones, one per field (a rest or star perch may follow, out of the wind).
                    var windy = c.jumpChecks.Where(j => j.stage == segment.Key.stage && j.segment == segment.Key.segment && !j.pro).Take(windFields.Count).ToList();
                    Assert.AreEqual(windFields.Count, windy.Count, at);
                    foreach (var j in windy) {
                        jumps++;
                        float headway = Mathf.Sqrt(1f - strongest * strongest / (j.speed * j.speed));
                        Assert.LessOrEqual(j.gap, j.maxGap * headway + 1e-3f, $"{at}: gap too long to steer upwind");
                    }
                }
            }
            Assert.Greater(fields, 30);
            Assert.Greater(jumps, 30);
        }

        [Test] public void IceIsSlipperyAndEndsOnSolidGround() {
            int sheets = 0;
            for (int seed = 1; seed <= 60; seed++) {
                var c = Make(seed);
                foreach (var group in c.pieces.Where(p => p.kind == OB_PieceKind.Ice).GroupBy(p => (p.stage, p.segment))) {
                    foreach (var sheet in group) {
                        sheets++;
                        Assert.That(sheet.speed, Is.InRange(0.02f, 0.99f), "traction");
                        Assert.GreaterOrEqual(sheet.size.z, 6f, "room to slide on");
                    }
                    var after = c.pieces.Where(p => p.stage == group.Key.stage && p.segment == group.Key.segment
                        && p.kind is OB_PieceKind.Platform or OB_PieceKind.Ice).Last();
                    Assert.AreEqual(OB_PieceKind.Platform, after.kind, $"{Where(c, group.Key.stage, group.Key.segment)}: the rink ends on ice");
                }
            }
            Assert.Greater(sheets, 30);
        }

        [Test] public void EveryIceGapCanBeMadeFromAStandingStartOnTheSheetBeforeIt() {
            // A player who stops or respawns on a sheet only gets the speed the ice's grip lets them
            // build up over its length again; every jump off a sheet must be makeable at that speed.
            float margin = Rules.mechanics.ice.runUpMargin;
            int checkedJumps = 0;
            foreach (var c in Courses(25)) {
                foreach (var group in c.pieces.Where(p => p.kind == OB_PieceKind.Ice).GroupBy(p => (p.stage, p.segment))) {
                    var sheets = group.ToArray();
                    var jumps = c.jumpChecks.Where(j => j.stage == group.Key.stage && j.segment == group.Key.segment && !j.pro).ToArray();
                    Assert.GreaterOrEqual(jumps.Length, sheets.Length + 1, $"{Where(c, group.Key.stage, group.Key.segment)}: one jump onto each sheet and one off the last");
                    for (int i = 0; i < sheets.Length; i++) {
                        var off = jumps[i + 1];
                        float fromRest = Mathf.Min(Move.SpeedFromRest(sheets[i].speed, sheets[i].size.z - margin), Move.sprintSpeed);
                        float reach = fromRest * Move.LandTime(off.rise, off.gravityScale, off.launchSpeed);
                        Assert.LessOrEqual(off.speed, fromRest + 0.01f, $"{Where(c, group.Key.stage, group.Key.segment)} sheet {i}: designed at {off.speed:F1} m/s, ice allows {fromRest:F1}");
                        Assert.LessOrEqual(off.gap, reach * Rules.physicalGapCeiling + 0.01f, $"{Where(c, group.Key.stage, group.Key.segment)} sheet {i}: gap {off.gap:F2} m, reach from rest {reach:F2} m");
                        checkedJumps++;
                    }
                }
            }
            Assert.Greater(checkedJumps, 100);
        }

        [Test] public void IceFromRestIsSlowerThanFullGrip() {
            Assert.IsTrue(float.IsPositiveInfinity(Move.SpeedFromRest(1f, 5f)), "full grip reaches any speed at once");
            float extreme = Move.SpeedFromRest(0.1f, 8f);
            Assert.AreEqual(Mathf.Sqrt(2f * 0.1f * Move.slipAcceleration * 8f), extreme, 1e-4f);
            Assert.Less(extreme, Move.sprintSpeed, "the critic's case: 8 m of Extreme ice cannot reach sprint speed");
        }

        [Test] public void StandingOnAnyReachedStageNeverCountsAsAFall() {
            int standings = 0;
            foreach (var c in Courses(6)) {
                int last = c.StageCount - 1;
                foreach (var piece in c.pieces.Where(p => p.kind == OB_PieceKind.Platform && p.stage <= last)) {
                    Vector3 top = piece.position + Vector3.up * (piece.size.y * 0.5f);
                    for (int reached = piece.stage; reached <= last; reached += Mathf.Max(1, c.StageCount / 5)) {
                        c.KillHeights(reached, top + Vector3.up * 0.1f, out float killY, out float killTop);
                        Assert.Less(killY, top.y, $"{Where(c, piece.stage, piece.segment)}: standing there with checkpoint {reached} kills");
                        // The server judges the body's top: a full jump from anywhere stays under the upper limit.
                        Assert.Greater(killTop, top.y + Move.bodyHeight + Move.jumpHeight,
                            $"{Where(c, piece.stage, piece.segment)}: a jump there crosses the upper kill height");
                        standings++;
                    }
                }
            }
            Assert.Greater(standings, 1000);
        }

        [Test] public void KillHeightsOnlyUseReachedStagesAndMatchTheOwnStageOnItsOwnGround() {
            foreach (var c in Courses(6)) {
                for (int reached = 0; reached < c.StageCount; reached++) {
                    var own = c.stages[reached];
                    Vector3 spawn = own.spawn + Vector3.up * 0.1f;
                    Assert.LessOrEqual(c.StageAt(reached, spawn), reached);
                    c.KillHeights(reached, spawn, out float killY, out float killTop);
                    Assert.LessOrEqual(killY, own.killY, $"{Where(c, reached, -1)}: stricter than its own stage");
                    float lowest = Enumerable.Range(0, reached + 1).Min(s => c.stages[s].killY);
                    Assert.GreaterOrEqual(killY, lowest, $"{Where(c, reached, -1)}: an unreached stage's height was used");
                    // Far from every stage: the own stage's heights.
                    c.KillHeights(reached, spawn + new Vector3(5000f, 0f, 0f), out float farY, out _);
                    Assert.AreEqual(own.killY, farY);
                    Assert.AreEqual(reached, c.StageAt(reached, spawn + new Vector3(5000f, 0f, 0f)));
                }
                // Every stage's region covers its own checkpoint and pieces.
                for (int s = 1; s < c.StageCount; s++) Assert.IsTrue(c.IsOver(s, c.stages[s].spawn), $"{Where(c, s, -1)}: region misses its spawn");
            }
        }

        [Test] public void EveryPickupLiesOverItsOwnStageOrANeighbour() {
            // The server only tests the pickups of the stage a player is over and its neighbours.
            foreach (var c in Courses(10))
                for (int s = 0; s < c.StageCount; s++)
                    for (int reached = s; reached < c.StageCount; reached++) {
                        var stage = c.stages[s];
                        Assert.LessOrEqual(Mathf.Abs(c.StageAt(reached, stage.coin) - s), 1, $"{Where(c, s, -1)}: coin");
                        if (stage.hasStar) Assert.LessOrEqual(Mathf.Abs(c.StageAt(reached, stage.star) - s), 1, $"{Where(c, s, -1)}: star");
                    }
        }

        [Test] public void EveryCrumblingPieceHoldsLongEnoughToRunAcross() {
            int crumbles = 0;
            foreach (var c in Courses(10))
                foreach (var p in c.pieces.Where(p => p.kind == OB_PieceKind.Crumble)) {
                    crumbles++;
                    float cross = Mathf.Max(p.size.x, p.size.z) / Move.sprintSpeed + Rules.crumbleCrossMargin;
                    Assert.GreaterOrEqual(p.speed, cross - 0.001f, $"{Where(c, p.stage, p.segment)}: a {Mathf.Max(p.size.x, p.size.z):F1} m crumble drops after {p.speed:F2} s");
                }
            Assert.Greater(crumbles, 50);
        }

        [Test] public void TheCourseKeyCoversStageCountAndRules() {
            // The same seed with another stage count, or other rules, is another course: a client that
            // keyed its build on the seed alone would show the wrong one.
            var full = Make(4242, Stages);
            var shorter = Make(4242, Stages - 6);
            Assert.AreNotEqual(full.pieces.Count, shorter.pieces.Count);
            Assert.AreEqual(Make(4242, Stages - 6).pieces.Count, shorter.pieces.Count, "same seed, count and rules: same course");

            Assert.AreEqual(OB_CourseRules.Default.Hash(), OB_CourseRules.Default.Hash(), "equal rules, equal hash");
            var bumpy = OB_CourseRules.Default;
            bumpy.mechanics.onOff.bumpChance = 1f;
            Assert.AreNotEqual(OB_CourseRules.Default.Hash(), bumpy.Hash(), "a mechanic tuning changes the hash");
            var wide = OB_CourseRules.Default;
            wide.tiers[2].beamWidth += 0.1f;
            Assert.AreNotEqual(OB_CourseRules.Default.Hash(), wide.Hash(), "a tier rule changes the hash");
        }

        [Test] public void ClassicAndGravityTunablesDriveTheCourse() {
            var rules = OB_CourseRules.Default;
            rules.mechanics.conveyors.length = 7f;
            rules.mechanics.spinner.speed = new OB_Span(80f, 80f);
            rules.mechanics.ceilingRun.landingWidth = 7.5f;
            int belts = 0, bars = 0;
            for (int seed = 1; seed <= 40; seed++) {
                var c = OB_CourseGenerator.Generate(seed * 31, Stages, Move, Start, 0f, Lobby, rules);
                foreach (var p in c.pieces) {
                    if (p.kind == OB_PieceKind.Conveyor) { belts++; Assert.AreEqual(7f, p.size.z, 1e-4f, "belt length"); }
                    if (p.kind is OB_PieceKind.SpinnerBar or OB_PieceKind.SpinnerCross) { bars++; Assert.AreEqual(80f, Mathf.Abs(p.speed), 1e-3f, "spinner speed"); }
                }
            }
            Assert.Greater(belts, 10);
            Assert.Greater(bars, 3);
        }

        /// <summary>
        /// A player hops each spinner arm, lands and hops the next: arms never pass a spot sooner than a
        /// jump takes to come down again (plus the slack), so even an Extreme cross keeps that rhythm.
        /// </summary>
        [Test] public void SpinnerArmsLeaveTimeToLandBetweenHops() {
            var slack = OB_CourseRules.Default.mechanics.spinner.rhythmSlack;
            int crosses = 0;
            for (int seed = 1; seed <= 60; seed++) {
                var c = OB_CourseGenerator.Generate(seed * 17, Stages, Move, Start, 0f, Lobby, OB_CourseRules.Default);
                foreach (var p in c.pieces) {
                    if (p.kind is not (OB_PieceKind.SpinnerBar or OB_PieceKind.SpinnerCross)) continue;
                    bool cross = p.kind == OB_PieceKind.SpinnerCross;
                    if (cross) crosses++;
                    float interval = OB_CourseGenerator.SpinnerArmSpacing(cross) / Mathf.Abs(p.speed);
                    Assert.GreaterOrEqual(interval, Move.AirTime() + slack - 1e-3f, $"seed {seed * 17}: a {p.kind} at {p.speed:F0} deg/s");
                }
            }
            Assert.Greater(crosses, 10, "crosses are tested");
        }

        [Test] public void ClassicAndGravityTunablesThatCannotBuildAreRejected() {
            void Rejects(System.Action<OB_MechanicRules> breakIt, string what) {
                var rules = OB_CourseRules.Default;
                breakIt(rules.mechanics);
                Assert.Throws<System.ArgumentException>(() => OB_CourseGenerator.Generate(1, 10, Move, Start, 0f, Lobby, rules), what);
            }
            Rejects(t => t.beat.period = new OB_Span(0f, 2f), "a beat with no period");
            Rejects(t => t.moonLeap.gravity = new OB_Span(0.4f, 0f), "zero gravity");
            Rejects(t => t.moonLeap.gravity = new OB_Span(1.5f, 0.3f), "more than normal gravity");
            Rejects(t => t.ice.rise = new Vector2(0.5f, -0.5f), "a range running backwards");
            Rejects(t => t.crumble.blocks = new OB_Span(0f, 0f), "a crumble bridge of no blocks");
            Rejects(t => t.wallWalk.obstacles = new OB_Span(0f, 3f), "a wall walk with nothing on it at the easiest");
            Rejects(t => t.movers.ferrySpeed = new OB_Span(2f, 0f), "a ferry that never moves");
            Assert.DoesNotThrow(() => OB_CourseGenerator.Generate(1, 10, Move, Start, 0f, Lobby, OB_CourseRules.Default));
        }

        [Test] public void RulesThatCannotBuildACourseAreRejected() {
            var broken = OB_CourseRules.Default;
            broken.tiers = broken.tiers.Take(3).ToArray();
            Assert.Throws<System.ArgumentException>(() => OB_CourseGenerator.Generate(1, 10, Move, Start, 0f, Lobby, broken));
            var empty = OB_CourseRules.Default;
            empty.tiers[0].segments = new Vector2Int(0, 2);
            Assert.Throws<System.ArgumentException>(() => OB_CourseGenerator.Generate(1, 10, Move, Start, 0f, Lobby, empty));
        }
    }
}
