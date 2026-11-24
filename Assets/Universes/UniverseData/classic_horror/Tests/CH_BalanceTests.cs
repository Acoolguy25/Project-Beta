using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Tests {
    /// <summary>
    /// Father Crane's numbers against <see cref="CH_ChaseModel"/>: a good player can always get away, but only by running
    /// well to cover, putting down what slows them, and using the moments he stands and stares. In the open he stays on them.
    /// </summary>
    public sealed class CH_BalanceTests {
        /// <summary>The walk from the chapel altar to the boarding point (the NavMesh path, measured in the editor, rounded up).</summary>
        const float AltarToBoat = 185f;
        /// <summary>How long a player takes to react to a scare before they start running.</summary>
        const float Reaction = 0.4f;
        /// <summary>A long run with nothing to break his sight: no corner, no door, no hiding place.</summary>
        const float OpenGround = 300f;
        /// <summary>A run this long in his sight means the player never tried to lose him.</summary>
        const float EndlessOpenGround = 600f;
        /// <summary>The shared client character, whose CharacterMovement holds the sprint cost.</summary>
        const string CharacterPrefab = "Packages/com.ryan.networking/Runtime/Client/ClientCore/Client.prefab";
        static readonly CH_MonsterTuning Tuning = new();

        [Test] public void TheModelUsesTheSharedCharactersSprintCost() {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefab);
            Assert.IsNotNull(prefab, $"{CharacterPrefab} moved: point this test at the shared character.");
            SerializedProperty cost = null;
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true)) {
                if (component == null) continue;
                cost = new SerializedObject(component).FindProperty("sprintStaminaPerSecond");
                if (cost != null) break;
            }
            Assert.IsNotNull(cost, "The shared character no longer has sprintStaminaPerSecond.");
            Assert.AreEqual(CH_MonsterTuning.SprintStaminaPerSecond, cost.floatValue, 1e-4f,
                "The shared sprint cost changed: update CH_MonsterTuning.SprintStaminaPerSecond and re-check these tests.");
        }

        [Test] public void InTheOpenHeIsStillOnYouAfterALongRun() {
            for (int act = 1; act <= 3; act++)
                foreach (int team in new[] { 1, 3 }) {
                    var run = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(act, false, team), 0f, OpenGround, 8f);
                    Assert.Less(run.finalGap, Tuning.chaseSightRange,
                        $"Act {act}, {team} alive: {OpenGround} m of open sprinting left a {run.finalGap:0.0} m gap; only cover should lose him.");
                }
        }

        /// <summary>
        /// A real chase through the village cost him about a fifth of his pace to corners and repaths (try 7's live trace:
        /// 85 m at 5.15 m/s against a set 6.35). Closing in is what keeps such a chase alive.
        /// </summary>
        [Test] public void WhenCornersCostHimAFifthHeStillKeepsYouInSight() {
            const float CornerLoss = 0.82f;
            for (int act = 1; act <= 3; act++)
                foreach (int team in new[] { 1, 3 }) {
                    var run = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(act, false, team) * CornerLoss, 0f, OpenGround, 8f);
                    if (run.escaped)
                        Assert.Less(run.finalGap, Tuning.chaseSightRange, $"Act {act}, {team} alive: {run.finalGap:0.0} m ahead after {OpenGround} m.");
                }
            var drifting = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(1, false, 1) * CornerLoss, 0f, OpenGround, 8f, closesIn: false);
            Assert.Greater(drifting.finalGap, Tuning.chaseSightRange, "Without closing in, that chase would be lost.");
        }

        [Test] public void ClosingInNeverOutrunsASprinter() {
            for (int act = 1; act <= 3; act++)
                Assert.LessOrEqual(Tuning.ClosingPace(Tuning.ChaseSpeed(act, false, 3), 1000f), CH_MonsterTuning.InvestigatorSprintSpeed);
            Assert.AreEqual(Tuning.ChaseSpeed(2, false, 3), Tuning.ClosingPace(Tuning.ChaseSpeed(2, false, 3), Tuning.closeInFrom));
        }

        [Test] public void FromActTwoHeRunsDownATeamThatStaysInTheOpen() {
            for (int act = 2; act <= 3; act++) {
                var run = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(act, false, 3), 0f, EndlessOpenGround, 8f);
                Assert.IsFalse(run.escaped, $"Act {act}: a team that never breaks his sight is caught (got {run.distanceCovered:0} m).");
            }
            Assert.IsFalse(CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(3, false, 1), 0f, EndlessOpenGround, 8f).escaped,
                "Act III: even alone, nobody outlasts him in the open.");
        }

        [Test] public void ASprinterEscapesEveryActsChase() {
            for (int act = 1; act <= 3; act++)
                foreach (int team in new[] { 1, 3 }) {
                    var run = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(act, false, team), 0f, 120f, 8f);
                    Assert.IsTrue(run.escaped, $"Act {act}, {team} alive: a sprint from 8 m away should get clear.");
                }
        }

        [Test] public void AWalkerIsCaught() {
            Assert.Greater(Tuning.ChaseSpeed(1, false, 1), CH_MonsterTuning.InvestigatorWalkSpeed);
            var run = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(1, false, 1), 0f, 120f, 8f, fixedSpeed: CH_MonsterTuning.InvestigatorWalkSpeed);
            Assert.IsFalse(run.escaped, "Nobody walks away from him.");
        }

        [Test] public void TheBellRunIsWinnableAloneAndWithATeam() {
            foreach (int team in new[] { 1, 3 }) {
                var run = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(3, true, team), Tuning.bellGrabHoldSeconds - Reaction, AltarToBoat, 1.2f, closesIn: false);
                Assert.IsTrue(run.escaped, $"{team} alive: the bell can be carried from the altar to the boat.");
                Assert.Greater(run.finalGap, 10f, $"{team} alive: with room for a wrong turn ({run.finalGap:0.0} m to spare).");
            }
        }

        [Test] public void WithoutHisStareTheBellCannotBeStolen() {
            var run = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(3, true, 3), 0f, AltarToBoat, 1.2f, closesIn: false);
            Assert.IsFalse(run.escaped, "The stare after the bell leaves the altar is what makes the run possible.");
        }

        [Test] public void AChasedFuelCarrierHasToPutTheCanDown() {
            float pace = Tuning.ChaseSpeed(1, false, 1);
            Assert.IsFalse(CH_ChaseModel.Run(Tuning, pace, 0f, 60f, 12f, fixedSpeed: CH_MonsterTuning.HeavyCarrySpeed).escaped, "Nobody outruns him with the fuel can.");
            Assert.IsTrue(CH_ChaseModel.Run(Tuning, pace, 0f, 60f, 12f).escaped, "Put it down and run, and they get away.");
        }

        [Test] public void BeingDraggedOutOfAWardrobeLeavesAChanceToRun() {
            float pace = Tuning.ChaseSpeed(2, false, 1);
            var run = CH_ChaseModel.Run(Tuning, pace, Tuning.dragOutHoldSeconds - Reaction, 60f, 1.6f);
            Assert.IsTrue(run.escaped, "His stare gives a head start that a sprint can turn into an escape.");
            Assert.IsFalse(CH_ChaseModel.Run(Tuning, pace, 0f, 60f, 1.6f).escaped, "Without it, a drag-out would be certain death.");
        }
    }
}
