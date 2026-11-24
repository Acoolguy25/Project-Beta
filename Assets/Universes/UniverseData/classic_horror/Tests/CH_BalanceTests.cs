using NUnit.Framework;

namespace Universes.UniverseData.classic_horror.Tests {
    /// <summary>
    /// Father Crane's numbers against <see cref="CH_ChaseModel"/>: a good player can always get away, but only by running
    /// well, putting down what slows them, and using the moments he stands and stares.
    /// </summary>
    public sealed class CH_BalanceTests {
        /// <summary>The walk from the chapel altar to the boarding point (the NavMesh path, measured in the editor, rounded up).</summary>
        const float AltarToBoat = 185f;
        /// <summary>How long a player takes to react to a scare before they start running.</summary>
        const float Reaction = 0.4f;
        static readonly CH_MonsterTuning Tuning = new();

        [Test] public void ASprinterEscapesEveryActsChase() {
            for (int act = 1; act <= 3; act++)
                foreach (int team in new[] { 1, 3 }) {
                    var run = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(act, false, team), 0f, 120f, 8f);
                    Assert.IsTrue(run.escaped, $"Act {act}, {team} alive: a sprint from 8 m away should get clear.");
                }
        }

        [Test] public void AWalkerIsCaught() {
            Assert.Greater(Tuning.ChaseSpeed(1, false, 1), CH_MonsterTuning.InvestigatorWalkSpeed);
            var run = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(1, false, 1), 0f, 120f, 8f, carrySpeed: CH_MonsterTuning.InvestigatorWalkSpeed);
            Assert.IsFalse(run.escaped, "Nobody walks away from him.");
        }

        [Test] public void TheBellRunIsWinnableAloneAndWithATeam() {
            foreach (int team in new[] { 1, 3 }) {
                var run = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(3, true, team), Tuning.bellGrabHoldSeconds - Reaction, AltarToBoat, 1.2f);
                Assert.IsTrue(run.escaped, $"{team} alive: the bell can be carried from the altar to the boat.");
                Assert.Greater(run.finalGap, 10f, $"{team} alive: with room for a wrong turn ({run.finalGap:0.0} m to spare).");
            }
        }

        [Test] public void WithoutHisStareTheBellCannotBeStolen() {
            var run = CH_ChaseModel.Run(Tuning, Tuning.ChaseSpeed(3, true, 3), 0f, AltarToBoat, 1.2f);
            Assert.IsFalse(run.escaped, "The stare after the bell leaves the altar is what makes the run possible.");
        }

        [Test] public void AChasedFuelCarrierHasToPutTheCanDown() {
            float pace = Tuning.ChaseSpeed(1, false, 1);
            Assert.IsFalse(CH_ChaseModel.Run(Tuning, pace, 0f, 60f, 12f, carrySpeed: CH_MonsterTuning.HeavyCarrySpeed).escaped, "Nobody outruns him with the fuel can.");
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
