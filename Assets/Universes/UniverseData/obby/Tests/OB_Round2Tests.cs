using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.obby.Tests {
    /// <summary>The round-2 mechanics: Lift Shaft, Ghost Path, Honey Swamp, Boost Rings, Swings, Rope Bridge.</summary>
    public sealed class OB_Round2Tests {
        static readonly Vector3 Start = new(0f, 100f, 9f), Lobby = new(0f, 100f, -3f);
        static readonly OB_Movement Move = OB_Movement.Default;
        static readonly OB_CourseRules Rules = OB_CourseRules.Default;

        static IEnumerable<OB_CourseLayout> Courses(int seeds) {
            foreach (int n in new[] { 13, OB_CourseGenerator.DefaultStageCount, 30 })
                for (int seed = 1; seed <= seeds; seed++)
                    yield return OB_CourseGenerator.Generate(seed * 104729 + n, n, Move, Start, 0f, Lobby, Rules);
        }


        static string Where(OB_CourseLayout c, in OB_Piece p) => $"seed {c.seed} ({c.StageCount} stages) stage {p.stage} segment {p.segment} {p.kind}";

        /// <summary>The pieces of one segment, in placement order.</summary>
        static IEnumerable<(OB_CourseLayout course, List<OB_Piece> pieces, int stage, int segment)> Segments(OB_StageKind kind, int seeds) {
            foreach (var c in Courses(seeds))
                foreach (var s in c.stages)
                    for (int i = 0; i < s.segments.Length; i++)
                        if (s.segments[i] == kind)
                            yield return (c, c.pieces.Where(p => p.stage == s.index && p.segment == i).ToList(), s.index, i);
        }

        [Test] public void EveryRoundTwoMechanicAppearsAndIsIntroducedByATier() {
            var kinds = new[] { OB_StageKind.LiftShaft, OB_StageKind.GhostPath, OB_StageKind.HoneySwamp, OB_StageKind.BoostRings, OB_StageKind.Swings, OB_StageKind.RopeBridge };
            foreach (var kind in kinds) {
                Assert.IsTrue(Rules.tiers.Any(t => t.introduces.Contains(kind)), $"{kind} is introduced by no tier");
                Assert.Greater(Segments(kind, 12).Count(), 5, $"{kind} is rarely generated");
            }
        }

        [Test] public void EveryDefaultCourseHasEnoughStagesForTheNewMechanicChallenges() {
            // A daily challenge must be doable in one full race of the default course.
            var stats = new Dictionary<string, OB_StageKind[]> {
                [OB_Retention.RingsStat] = new[] { OB_StageKind.BoostRings },
                [OB_Retention.RidesStat] = new[] { OB_StageKind.Swings, OB_StageKind.LiftShaft },
                [OB_Retention.VanishStat] = new[] { OB_StageKind.GhostPath, OB_StageKind.RopeBridge },
            };
            for (int seed = 1; seed <= 40; seed++) {
                var c = OB_CourseGenerator.Generate(seed * 7919, OB_CourseGenerator.DefaultStageCount, Move, Start, 0f, Lobby, Rules);
                foreach (var pair in stats) {
                    int target = OB_Retention.Challenges.Single(ch => ch.statKey == pair.Key).target;
                    int stages = c.stages.Count(s => s.segments.Any(k => pair.Value.Contains(k)));
                    Assert.GreaterOrEqual(stages, target, $"seed {c.seed}: only {stages} stages count for {pair.Key}");
                }
            }
        }

        [Test] public void HintMasksHoldEveryStageKind() {
            foreach (OB_StageKind kind in System.Enum.GetValues(typeof(OB_StageKind))) {
                Assert.AreNotEqual(0UL, OB_StageHints.Bit(kind), $"{kind} has no seen bit");
                Assert.IsTrue(OB_StageHints.Has(OB_StageHints.Bit(kind), kind));
                Assert.IsFalse(OB_StageHints.Has(0UL, kind));
            }
        }

        [Test] public void LiftsRideBetweenLedgesOneShortHopFromTheirStops() {
            int lifts = 0;
            foreach (var (c, pieces, stage, segment) in Segments(OB_StageKind.LiftShaft, 25)) {
                foreach (var p in pieces.Where(p => p.kind == OB_PieceKind.Mover)) {
                    lifts++;
                    Assert.Greater(Mathf.Abs(p.vector.y), 4f, Where(c, p));
                    Assert.AreEqual(0f, new Vector2(p.vector.x, p.vector.z).magnitude, 1e-3f, $"{Where(c, p)}: a lift only moves vertically");
                    Assert.Less(p.speed, 4.2f, $"{Where(c, p)}: too fast to board");
                }
                foreach (var j in c.jumpChecks.Where(j => j.stage == stage && j.segment == segment && !j.pro).Take(2 * pieces.Count(q => q.kind == OB_PieceKind.Mover))) {
                    Assert.Less(j.gap, 3.1f, $"seed {c.seed}: a lift hop wider than 3 m");
                    Assert.AreEqual(0f, j.rise, 1e-3f);
                }
            }
            Assert.Greater(lifts, 10);
        }

        [Test] public void GhostWindowsOverlapLongEnoughToCrossBeforeTheOneBehindVanishes() {
            int pairs = 0;
            float flight = Move.AirTime();
            foreach (var (c, pieces, stage, segment) in Segments(OB_StageKind.GhostPath, 25)) {
                var ghosts = pieces.Where(p => p.kind == OB_PieceKind.Ghost).ToList();
                Assert.GreaterOrEqual(ghosts.Count, 5);
                for (int i = 0; i < ghosts.Count; i++) {
                    float window = ghosts[i].period * ghosts[i].speed;
                    Assert.Greater(window, flight + 1.0f, $"{Where(c, ghosts[i])}: a jump needs the next ghost solid for its whole flight");
                    Assert.LessOrEqual(window, ghosts[i].period);
                    if (i == 0) continue;
                    pairs++;
                    // Ghost i is solid from phase_i on (cycle = time - phase): it appears 'stride' after the one before it, which is still solid for 'overlap' more.
                    float stride = ghosts[i].phase - ghosts[i - 1].phase;
                    float overlap = window - stride;
                    Assert.Greater(stride, 0f, Where(c, ghosts[i]));
                    Assert.GreaterOrEqual(overlap, 0.95f, $"{Where(c, ghosts[i])}: the ghost before it vanishes before a jump can start");
                    Assert.AreEqual(ghosts[i - 1].period, ghosts[i].period, 1e-4f);
                }
            }
            Assert.Greater(pairs, 100);
        }

        [Test] public void HoneyIsFollowedByFirmGroundAndTheHopOffItIsSizedForTheSlowedPace() {
            int sheets = 0;
            foreach (var (c, pieces, stage, segment) in Segments(OB_StageKind.HoneySwamp, 25)) {
                var segJumps = c.jumpChecks.Where(j => j.stage == stage && j.segment == segment).ToList();
                foreach (var p in pieces.Where(p => p.kind == OB_PieceKind.Honey)) {
                    sheets++;
                    Assert.Greater(p.speed, 0.3f, Where(c, p));
                    Assert.Less(p.speed, 0.7f, Where(c, p));
                    int at = pieces.IndexOf(p);
                    Assert.IsTrue(at + 1 < pieces.Count && pieces[at + 1].kind == OB_PieceKind.Platform, $"{Where(c, p)}: no firm pad after the sheet");
                    Assert.GreaterOrEqual(Mathf.Max(pieces[at + 1].size.x, pieces[at + 1].size.z), Tune2.recoveryRun - 1e-3f, Where(c, p));
                }
                Assert.IsTrue(segJumps.Any(j => j.speed < Move.walkSpeed * 0.7f), $"seed {c.seed}: no jump off a sheet is sized for the slowed pace");
            }
            Assert.Greater(sheets, 20);
        }

        static OB_HoneyTuning Tune2 => Rules.mechanics.honey;

        [Test] public void RingFlightsLandOnTheNextRingAndTheIslandAndRelaunchOnTheWayDown() {
            int flights = 0;
            foreach (var (c, pieces, stage, segment) in Segments(OB_StageKind.BoostRings, 30)) {
                var rings = pieces.Where(p => p.kind == OB_PieceKind.Ring).ToList();
                var island = pieces.First(p => p.kind == OB_PieceKind.Platform);
                for (int i = 0; i < rings.Count; i++) {
                    var r = rings[i];
                    float flight = r.speed;
                    float carry = Move.Carry(1f, flight);
                    Vector3 v = Quaternion.Inverse(Quaternion.Euler(0f, r.yaw, 0f)) * r.vector;
                    Vector3 local = new(v.x * carry, v.y * flight - 0.5f * Move.gravity * flight * flight, v.z * carry);
                    Vector3 arrive = r.position + Quaternion.Euler(0f, r.yaw, 0f) * local;
                    flights++;
                    Assert.Greater(flight, 0.9f, Where(c, r));
                    Assert.Less(flight, 1.5f, Where(c, r));
                    if (i + 1 < rings.Count) {
                        var next = rings[i + 1];
                        Assert.AreEqual(0f, Vector3.Distance(new Vector3(arrive.x, 0f, arrive.z), new Vector3(next.position.x, 0f, next.position.z)), 0.02f, $"{Where(c, r)}: the flight misses the next ring");
                        // A hands-off flight arrives at the hoop's middle; one holding forward (walk speed on top of the launch) arrives sooner and higher,
                        // inside the trigger's funnel above the hoop.
                        Assert.Less(Mathf.Abs(arrive.y - next.position.y), 0.02f, $"{Where(c, r)}: a hands-off flight misses the next ring");
                        float distance = Move.Carry(r.vector.z, flight), held = flight;
                        for (float tt = 0.05f; tt < flight; tt += 0.01f)
                            if (Move.Carry(r.vector.z, tt) + Move.walkSpeed * Rules.mechanics.rings.airControl * tt >= distance) { held = tt; break; }
                        float heldY = r.position.y + r.vector.y * held - 0.5f * Move.gravity * held * held;
                        Assert.Less(heldY - next.position.y, next.size.y * 0.5f + Rules.mechanics.rings.catchAbove - 0.25f, $"{Where(c, r)}: a flight holding forward arrives above the ring's funnel");
                        // The next ring fires only on the way down (LaunchPad ignores a body rising faster than half its launch).
                        float vyIn = r.vector.y - Move.gravity * flight;
                        Assert.Less(vyIn, rings[i + 1].vector.y * 0.5f, $"{Where(c, r)}: the next ring would not fire");
                        Assert.LessOrEqual(Vector2.Distance(new Vector2(r.position.x, r.position.z), new Vector2(rings[i + 1].position.x, rings[i + 1].position.z)), 40f);
                    } else {
                        Vector3 offset = Quaternion.Inverse(Quaternion.Euler(0f, island.yaw, 0f)) * (arrive - island.position);
                        Assert.Less(Mathf.Abs(offset.x), island.size.x * 0.5f - 1f, $"{Where(c, r)}: lands beside the island");
                        Assert.Less(Mathf.Abs(offset.z), island.size.z * 0.25f, $"{Where(c, r)}: lands off the island's middle");
                        Assert.AreEqual(island.position.y + island.size.y * 0.5f, arrive.y, 0.02f, $"{Where(c, r)}: wrong island height");
                    }
                    Assert.Less(Mathf.Abs(r.vector.y), 40f, "launch speed stays sane");
                }
                Assert.IsTrue(c.jumpChecks.Any(j => j.stage == stage && j.segment == segment && j.airTime > 0f), "steering recorded");
            }
            Assert.Greater(flights, 60);
        }

        [Test] public void CannonIslandsCatchBothAHandsOffAndAHoldForwardFlight() {
            int cannons = 0;
            var t = Rules.mechanics.cannons;
            foreach (var (c, pieces, stage, segment) in Segments(OB_StageKind.Cannons, 30)) {
                foreach (var cannon in pieces.Where(p => p.kind == OB_PieceKind.Cannon)) {
                    var island = pieces.Skip(pieces.IndexOf(cannon)).First(p => p.kind == OB_PieceKind.Platform && p.group == 0);
                    Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, cannon.yaw, 0f)) * (island.position - cannon.position);
                    float carry = Move.Carry(cannon.vector.z, cannon.speed);
                    float held = carry + Move.walkSpeed * t.airControl * cannon.speed * 0.9f;
                    float half = island.size.z * 0.5f - t.landingSlack * 0.5f;
                    cannons++;
                    Assert.Less(Mathf.Abs(carry - local.z), half, $"{Where(c, cannon)}: a hands-off flight misses the island");
                    Assert.Less(Mathf.Abs(held - local.z), half, $"{Where(c, cannon)}: a flight holding forward misses the island");
                }
            }
            Assert.Greater(cannons, 20);
        }

        [Test] public void SwingEndsLieLevelWithTheirLedgesWithinOneHop() {
            int swings = 0;
            foreach (var (c, pieces, stage, segment) in Segments(OB_StageKind.Swings, 25)) {
                foreach (var p in pieces.Where(p => p.kind == OB_PieceKind.Swing)) {
                    swings++;
                    float radians = p.vector.x * Mathf.Deg2Rad;
                    float reach = p.size.y * Mathf.Sin(radians);
                    Assert.Greater(p.vector.x, 20f, Where(c, p));
                    Assert.Less(p.vector.x, 60f, Where(c, p));
                    Assert.Greater(p.period, 2f, Where(c, p));
                    // The platform's top is level with the surface it leaves from at the near end of its swing.
                    float topAtEnds = p.position.y - p.size.y * Mathf.Cos(radians);
                    var from = c.jumpChecks.Where(j => j.stage == stage && j.segment == segment && !j.pro).Take(2 * pieces.Count(q => q.kind == OB_PieceKind.Swing)).ToList();
                    Assert.GreaterOrEqual(from.Count, 2, Where(c, p));
                    Assert.IsTrue(from.All(j => j.gap < 3.6f && Mathf.Abs(j.rise) < 1e-3f), $"{Where(c, p)}: swing hops are short and level");
                    Assert.Greater(reach, 3f);
                    Assert.IsTrue(pieces.Any(q => q.kind == OB_PieceKind.Platform && Mathf.Abs(q.position.y + q.size.y * 0.5f - topAtEnds) < 0.02f),
                        $"{Where(c, p)}: no ledge level with the swing's ends");
                }
            }
            Assert.Greater(swings, 10);
        }

        [Test] public void RopeBridgePlanksHoldLongEnoughToRunAcrossAndGapsStayHops() {
            int planks = 0;
            foreach (var (c, pieces, stage, segment) in Segments(OB_StageKind.RopeBridge, 25)) {
                foreach (var p in pieces.Where(p => p.kind == OB_PieceKind.Plank)) {
                    planks++;
                    Assert.GreaterOrEqual(p.speed, Mathf.Max(p.size.x, p.size.z) / Move.walkSpeed + Rules.crumbleCrossMargin - 1e-3f, $"{Where(c, p)}: falls before it can be crossed");
                    Assert.Greater(p.period, 0f);
                }
                Assert.Greater(pieces.Count(p => p.kind == OB_PieceKind.Plank), 7);
            }
            Assert.Greater(planks, 100);
        }

        [Test] public void RoundTwoPiecesNeverOverlapEachOtherOrTheirNeighbours() {
            int checks = 0;
            var kinds = new[] { OB_StageKind.LiftShaft, OB_StageKind.GhostPath, OB_StageKind.HoneySwamp, OB_StageKind.BoostRings, OB_StageKind.Swings, OB_StageKind.RopeBridge };
            foreach (var kind in kinds)
                foreach (var (c, pieces, stage, segment) in Segments(kind, 12)) {
                    var vols = pieces.Select(p => OB_PieceVolumes.TryGet(p, out var v) ? (ok: true, v) : (ok: false, v)).ToList();
                    for (int a = 0; a < pieces.Count; a++)
                        for (int b = a + 1; b < pieces.Count; b++) {
                            if (!vols[a].ok || !vols[b].ok) continue;
                            // Ghosts and ropes legitimately touch nothing; consecutive hops keep a gap, so any overlap is a bug.
                            checks++;
                            Assert.IsFalse(vols[a].v.Overlaps(vols[b].v, -0.05f), $"{Where(c, pieces[a])} overlaps {pieces[b].kind} (stage {stage} segment {segment})");
                        }
                }
            Assert.Greater(checks, 5000);
        }
    }
}
