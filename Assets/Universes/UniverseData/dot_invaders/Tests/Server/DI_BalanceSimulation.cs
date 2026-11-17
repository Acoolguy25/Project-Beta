using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Tests {
    /// <summary>
    /// Balance harness, run on demand: plays whole NPC-only matches on freshly generated boards with
    /// the real map generator, NPC planner and simulation (no networking), and logs who held each
    /// factory and fortress over time, who won and how long the match ran. Compare multiplier 1
    /// (specials act as plain bases) with the shipped default:
    /// <c>-testFilter Universes.UniverseData.dot_invaders.Tests.DI_BalanceSimulation</c>.
    /// </summary>
    [Explicit("Balance harness: plays full simulated matches; run on demand.")]
    public class DI_BalanceSimulation {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        // The live server scene runs 20-minute matches (dot_invaders_server.unity overrides the prefab).
        const int MatchSeconds = 1200;
        const int MatchesPerCase = 5;
        const int SampleEverySeconds = 60;

        GameObject root;
        DI_ServerRunner runner;

        [SetUp]
        public void SetUp() {
            root = UnityEditor.PrefabUtility.LoadPrefabContents(
                "Assets/Universes/UniverseData/dot_invaders/Server/DI_ServerRunner.prefab");
            runner = root.GetComponent<DI_ServerRunner>();
        }

        [TearDown]
        public void TearDown() => UnityEditor.PrefabUtility.UnloadPrefabContents(root);

        [TestCase(1f)]
        [TestCase(DI_Rules.DefaultFactoryMultiplier)]
        public void NpcOnlyMatches(float multiplier) {
            Set("factoryMultiplier", multiplier);
            Set("fortressMultiplier", multiplier);
            float step = (float)typeof(DI_ServerRunner).GetField("SimulationStep", Members).GetValue(null);
            int stepsPerSample = Mathf.RoundToInt(SampleEverySeconds / step);
            var report = new StringBuilder($"Factory/Fortress multiplier {multiplier}x, {MatchesPerCase} NPC-only matches\n");
            int factoryHolderWon = 0, decided = 0;

            for (int match = 1; match <= MatchesPerCase; match++) {
                StartBoard();
                IList bases = (IList)Get("bases");
                List<int> factories = Indexes(bases, "isFactory"), fortresses = Indexes(bases, "isFortress");
                var timeline = new StringBuilder();
                int elapsedSteps = 0, survivor = -2, maxSteps = Mathf.RoundToInt(MatchSeconds / step);
                while (elapsedSteps < maxSteps && survivor == -2) {
                    Call("RefreshTeamSpeedBases");
                    Call("UpdateNpcTeams", step);
                    Call("UpdateBases", step);
                    Call("UpdateDots", step);
                    elapsedSteps++;
                    survivor = (int)Call("FindSoleSurvivingTeam");
                    if (elapsedSteps % stepsPerSample == 0)
                        timeline.Append($"  {elapsedSteps * step / 60f:0}m F[{Holders(bases, factories)}] W[{Holders(bases, fortresses)}]\n");
                }
                bool eliminated = survivor != -2;
                int winner = eliminated ? survivor : (int)Call("DetermineLeadingTeam");
                // Who held the factories at the 5-minute mark, against the eventual winner.
                string earlyHolders = HoldersAt(timeline, 5);
                if (winner >= 0) {
                    decided++;
                    if (earlyHolders.Contains(winner.ToString()))
                        factoryHolderWon++;
                }
                report.Append($"Match {match}: winner team {winner} by {(eliminated ? "elimination" : "time")} after " +
                              $"{elapsedSteps * step / 60f:0.0} min; factories {Join(factories)}, fortresses {Join(fortresses)}\n")
                      .Append(timeline);
            }
            report.Append($"Winner held a factory at 5 min in {factoryHolderWon} of {decided} decided matches.\n");
            TestContext.WriteLine(report.ToString());
            Debug.Log(report.ToString());
            Assert.That(decided, Is.GreaterThan(0), "No match produced a winner.");
        }

        void StartBoard() {
            Set("matchDurationSeconds", MatchSeconds);
            foreach (string list in new[] { "bases", "links", "dots", "npcTeams", "dotInvadersTeams", "clashPositions" })
                Get(list).GetType().GetMethod("Clear").Invoke(Get(list), null);
            Set("nextTeamId", 0);
            Set("winningTeamId", -1);
            Call("GenerateBases");
            Call("GenerateLinks");
            Call("AssignTurrets");
            Call("AssignSuperBases");
            Call("AssignSpeedBases");
            Call("AssignFactoriesAndFortresses");
            Call("SpawnNpcTeams");
            Set("initialized", true);
            Set("matchInProgress", true);
        }

        static List<int> Indexes(IList bases, string flag) {
            var found = new List<int>();
            for (int i = 0; i < bases.Count; i++)
                if ((bool)bases[i].GetType().GetField(flag, Members).GetValue(bases[i]))
                    found.Add(i);
            return found;
        }

        static string Holders(IList bases, List<int> ids) {
            var text = new StringBuilder();
            foreach (int id in ids) {
                if (text.Length > 0) text.Append(',');
                text.Append((int)bases[id].GetType().GetField("teamId", Members).GetValue(bases[id]));
            }
            return text.ToString();
        }

        static string HoldersAt(StringBuilder timeline, int minute) {
            foreach (string line in timeline.ToString().Split('\n'))
                if (line.TrimStart().StartsWith($"{minute}m "))
                    return line.Substring(line.IndexOf("F[") + 2, line.IndexOf(']') - line.IndexOf("F[") - 2);
            return string.Empty;
        }

        static string Join(List<int> ids) => ids.Count == 0 ? "none" : string.Join(",", ids);

        object Get(string name) => typeof(DI_ServerRunner).GetField(name, Members).GetValue(runner);
        void Set(string name, object value) => typeof(DI_ServerRunner).GetField(name, Members).SetValue(runner, value);
        object Call(string method, params object[] args) => typeof(DI_ServerRunner).GetMethod(method, Members).Invoke(runner, args);
    }
}
