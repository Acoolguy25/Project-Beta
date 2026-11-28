using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.obby.Tests {
    /// <summary>Round 4: "?" blocks over checkpoint pads and the power-ups they give.</summary>
    public sealed class OB_PowerUpTests {
        static readonly Vector3 Start = new(0f, 100f, 9f), Lobby = new(0f, 100f, -3f);
        static readonly OB_Movement Move = OB_Movement.Default;
        static readonly OB_CourseRules Rules = OB_CourseRules.Default;
        static OB_PowerUpRules Power => Rules.powerUps;

        static IEnumerable<OB_CourseLayout> Courses(int seeds) {
            foreach (int n in new[] { 13, OB_CourseGenerator.DefaultStageCount, 30 })
                for (int seed = 1; seed <= seeds; seed++)
                    yield return OB_CourseGenerator.Generate(seed * 7919 + n, n, Move, Start, 0f, Lobby, Rules);
        }

        static IEnumerable<OB_Piece> Blocks(OB_CourseLayout c) => c.pieces.Where(p => p.kind == OB_PieceKind.PowerBlock);

        [Test] public void EveryCourseHasBlocksSpacedAlongIt() {
            foreach (var c in Courses(20)) {
                var stages = Blocks(c).Select(p => p.stage).ToList();
                Assert.GreaterOrEqual(stages.Count, c.StageCount / (Power.spacing.y + 1), $"seed {c.seed}: too few blocks");
                Assert.AreEqual(stages.Count, stages.Distinct().Count(), $"seed {c.seed}: two blocks on one stage");
                Assert.IsTrue(stages.All(s => s >= Power.firstStage && s < c.StageCount), $"seed {c.seed}: a block outside the course");
                for (int i = 1; i < stages.Count; i++)
                    Assert.LessOrEqual(stages[i] - stages[i - 1], Power.spacing.y, $"seed {c.seed}: blocks too far apart");
            }
        }

        [Test] public void BlocksFloatOverTheirPadWhereOnlyAJumpReaches() {
            foreach (var c in Courses(20))
                foreach (var block in Blocks(c)) {
                    var pad = c.pieces.First(p => p.kind == OB_PieceKind.CheckpointPad && p.stage == block.stage);
                    float padTop = pad.position.y + pad.size.y * 0.5f;
                    float bottom = block.position.y - block.size.y * 0.5f - padTop;
                    Assert.Greater(bottom, Move.bodyHeight + 0.5f, $"seed {c.seed} stage {block.stage}: a standing robot hits the block");
                    Assert.Less(bottom, Move.bodyHeight + Move.jumpHeight - 0.3f, $"seed {c.seed} stage {block.stage}: a jump cannot bump the block");
                    // Over the pad, beside its centre (the respawn point), and clear of the stage's next jump off the far edge.
                    Vector3 local = Quaternion.Euler(0f, -pad.yaw, 0f) * (block.position - pad.position);
                    Assert.LessOrEqual(Mathf.Abs(local.x) + block.size.x * 0.5f, pad.size.x * 0.5f + 0.01f, $"seed {c.seed}: block overhangs its pad");
                    Assert.LessOrEqual(Mathf.Abs(local.z) + block.size.z * 0.5f, pad.size.z * 0.5f + 0.01f, $"seed {c.seed}: block overhangs its pad");
                    Assert.Greater(Mathf.Abs(local.x), 0.5f, $"seed {c.seed}: block right over the respawn point");
                    Assert.IsTrue(c.stages[block.stage].region.Contains(block.position), $"seed {c.seed}: block outside its stage region");
                }
        }

        [Test] public void BlocksDoNotChangeTheStages() {
            var off = OB_CourseRules.Default;
            off.powerUps.enabled = false;
            foreach (int seed in new[] { 5, 77, 1234 }) {
                var with = OB_CourseGenerator.Generate(seed, 24, Move, Start, 0f, Lobby, Rules);
                var without = OB_CourseGenerator.Generate(seed, 24, Move, Start, 0f, Lobby, off);
                var kept = with.pieces.Where(p => p.kind != OB_PieceKind.PowerBlock).ToList();
                Assert.AreEqual(without.pieces.Count, kept.Count, $"seed {seed}");
                for (int i = 0; i < kept.Count; i++) {
                    Assert.AreEqual(without.pieces[i].kind, kept[i].kind, $"seed {seed} piece {i}");
                    Assert.AreEqual(without.pieces[i].position, kept[i].position, $"seed {seed} piece {i}");
                }
                Assert.AreEqual(Blocks(with).Count(), with.pieces.Count - kept.Count);
                Assert.AreNotEqual(Rules.Hash(), off.Hash(), "power-up rules are part of the rules hash");
            }
        }

        [Test] public void RushOnlyFromItsTierAndEveryPowerAppears() {
            var seen = new HashSet<OB_PowerUp>();
            foreach (var c in Courses(30))
                foreach (var block in Blocks(c)) {
                    var power = (OB_PowerUp)block.group;
                    seen.Add(power);
                    if (power == OB_PowerUp.Rush) Assert.GreaterOrEqual(c.stages[block.stage].tier, Power.rushFromTier, $"seed {c.seed}: rush too early");
                }
            CollectionAssert.IsSupersetOf(seen, new[] { OB_PowerUp.Shield, OB_PowerUp.Boots, OB_PowerUp.Rush });
            Assert.AreEqual(OB_PowerUp.Boots, Power.Pick(OB_Tier.Basic, 0.99f), "no rush in Basic: the top of the roll falls to boots");
            Assert.AreEqual(OB_PowerUp.Rush, Power.Pick(OB_Tier.Extreme, 0.99f));
            Assert.AreEqual(OB_PowerUp.Shield, Power.Pick(OB_Tier.Extreme, 0f));
        }

        [Test] public void BumpNeedsAHeadJustUnderTheBlock() {
            var rules = new OB_PowerUpRules();
            Vector3 centre = new(0f, 10f, 0f);
            float bottom = centre.y - rules.blockSize * 0.5f;
            Bounds Body(float headY, float x = 0f) => new(new Vector3(x, headY - Move.bodyHeight * 0.5f, 0f), new Vector3(2f, Move.bodyHeight, 2f));
            Assert.IsTrue(rules.IsBump(Body(bottom - 0.1f), centre, 0f), "head at the underside");
            Assert.IsFalse(rules.IsBump(Body(bottom - rules.bumpDepth - 0.2f), centre, 0f), "head well below");
            Assert.IsFalse(rules.IsBump(Body(bottom - 0.1f, rules.blockSize * 2f), centre, 0f), "beside the block");
            Assert.IsFalse(rules.IsBump(Body(centre.y + 4f), centre, 0f), "on top of the block");
        }

        [Test] public void PowerStateRules() {
            var rules = new OB_PowerUpRules();
            var state = new OB_PowerState();
            Assert.IsTrue(state.TryUse(3, OB_PowerUp.Shield, 0f, rules));
            Assert.IsFalse(state.TryUse(3, OB_PowerUp.Shield, 1f, rules), "a block works once");
            Assert.IsFalse(state.Protects(1f));
            Assert.IsTrue(state.TryAbsorb(2f, rules), "the shield takes the hit");
            Assert.IsTrue(state.Protects(2f + rules.shieldGraceSeconds * 0.5f), "grace after the pop");
            Assert.IsFalse(state.Protects(2f + rules.shieldGraceSeconds + 0.01f));
            Assert.IsFalse(state.TryAbsorb(5f, rules), "one hit only");

            Assert.IsTrue(state.TryUse(5, OB_PowerUp.Rush, 10f, rules));
            Assert.IsTrue(state.Protects(10f + rules.rushSeconds - 0.1f));
            Assert.IsFalse(state.Protects(10f + rules.rushSeconds + 0.1f));
            Assert.IsTrue(state.TryUse(6, OB_PowerUp.Boots, 20f, rules));
            Assert.AreEqual(rules.bootsSeconds, state.BootsLeft(20f), 1e-4f);
            Assert.AreEqual(0f, state.BootsLeft(20f + rules.bootsSeconds + 1f));

            Assert.IsFalse(state.HeadRose(10f, rules.bumpMaxRise), "the first sample is no rise");
            Assert.IsTrue(state.HeadRose(10.15f, rules.bumpMaxRise), "a jump rises a little each sample");
            Assert.IsFalse(state.HeadRose(10.1f, rules.bumpMaxRise), "falling is no bump");
            Assert.IsFalse(state.HeadRose(15f, rules.bumpMaxRise), "a teleport up is no bump");
            state.Clear();
            Assert.IsFalse(state.HasUsed(3) || state.shield || state.Protects(10f), "a new race starts clean");
        }
    }
}
