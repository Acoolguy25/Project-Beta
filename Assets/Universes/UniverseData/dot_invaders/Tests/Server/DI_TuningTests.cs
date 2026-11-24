using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Tests {
    /// <summary>Live tuning (multiplier commands) and map setup that the balance depends on.</summary>
    public class DI_TuningTests {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const string RunnerPrefab = "Assets/Universes/UniverseData/dot_invaders/Server/DI_ServerRunner.prefab";
        GameObject root;
        DI_ServerRunner runner;

        [SetUp]
        public void SetUp() {
            root = UnityEditor.PrefabUtility.LoadPrefabContents(RunnerPrefab);
            runner = root.GetComponent<DI_ServerRunner>();
        }

        [TearDown]
        public void TearDown() => UnityEditor.PrefabUtility.UnloadPrefabContents(root);

        object Get(string name) => typeof(DI_ServerRunner).GetField(name, Members).GetValue(runner);
        void Set(string name, object value) => typeof(DI_ServerRunner).GetField(name, Members).SetValue(runner, value);
        object Call(string method, params object[] args) => typeof(DI_ServerRunner).GetMethod(method, Members).Invoke(runner, args);
        float Property(string name) => (float)typeof(DI_ServerRunner).GetProperty(name, Members).GetValue(runner);
        object GetProperty(string name) => typeof(DI_ServerRunner).GetProperty(name, Members).GetValue(runner);

        [TestCase("SetFactoryMultiplier", "FactoryMultiplier")]
        [TestCase("SetFortressMultiplier", "FortressMultiplier")]
        public void MultiplierCommandsClampToTheRuleRange(string setter, string getter) {
            Call(setter, 3f);
            Assert.That(Property(getter), Is.EqualTo(3f));
            Call(setter, DI_Rules.MaxSpecialBaseMultiplier + 10f);
            Assert.That(Property(getter), Is.EqualTo(DI_Rules.MaxSpecialBaseMultiplier));
            Call(setter, 0f);
            Assert.That(Property(getter), Is.EqualTo(DI_Rules.MinSpecialBaseMultiplier));
        }

        static System.Collections.Generic.IEnumerable<string> BoardIds() {
            foreach (DI_BoardLayout layout in DI_BoardLayouts.All)
                yield return layout.id;
        }

        [TestCaseSource(nameof(BoardIds))]
        public void EveryBoardGivesItsSpecialBasesDistinctBases(string boardId) {
            DI_BoardLayout layout = DI_BoardLayouts.Find(boardId);
            Call("BuildBoard", layout, 17);

            int factories = 0, fortresses = 0, turrets = 0, supers = 0, speeds = 0;
            foreach (object state in (IList)Get("bases")) {
                bool factory = Flag(state, "isFactory"), fortress = Flag(state, "isFortress");
                bool turret = Flag(state, "isTurret"), super = Flag(state, "isSuperProducer"), speed = Flag(state, "isSpeedBase");
                int roles = (factory ? 1 : 0) + (fortress ? 1 : 0) + (turret ? 1 : 0) + (super ? 1 : 0) + (speed ? 1 : 0);
                Assert.That(roles, Is.LessThanOrEqualTo(1), "A base has at most one role.");
                if (factory) factories++;
                if (fortress) fortresses++;
                if (turret) turrets++;
                if (super) supers++;
                if (speed) speeds++;
            }
            Assert.That(((IList)Get("bases")).Count, Is.EqualTo(layout.baseCount));
            Assert.That(factories, Is.EqualTo(layout.factories));
            Assert.That(fortresses, Is.EqualTo(layout.fortresses));
            Assert.That(turrets, Is.EqualTo(layout.turrets));
            Assert.That(supers, Is.EqualTo(layout.superBases));
            Assert.That(speeds, Is.EqualTo(layout.speedBases));
        }

        [Test]
        public void NpcTuningByCommandMakesTheRunningMatchUnrated() {
            Set("matchInProgress", true);
            Call("SetNPCIntelligence", 90f);
            Assert.That(Get("npcTuningOverridden"), Is.True);
            Assert.That(Get("matchUnratedReason"), Is.Not.Null);
            Call("ClearRatingState");
            Assert.That(Get("matchUnratedReason"), Is.Null);
        }

        [Test]
        public void TheNpcTierSetsBotSkillUnlessItWasTunedByHand() {
            DI_NpcTier[] tiers = (DI_NpcTier[])Get("npcTiers");
            Set("forcedNpcTier", tiers.Length - 1);
            Call("ChooseNpcTier");
            Assert.That(Property("NPCIntelligence"), Is.EqualTo(tiers[tiers.Length - 1].intelligence));
            Assert.That(GetProperty("NpcTierName"), Is.EqualTo(tiers[tiers.Length - 1].name));

            Call("SetNPCIntelligence", 3f);
            Set("forcedNpcTier", 0);
            Call("ChooseNpcTier");
            Assert.That(Property("NPCIntelligence"), Is.EqualTo(3f), "A hand-tuned value survives the tier pick.");
        }

        [Test]
        public void NextBoardMovesTheRotationAndRejectsUnknownIds() {
            Assert.That(Call("SetNextBoard", "nope"), Is.False);
            Assert.That(Call("SetNextBoard", "halo"), Is.True);
            Assert.That(Call("TakeNextLayout"), Is.EqualTo(DI_BoardLayouts.IndexOf("halo")));
            // Taken once: the board after it follows the rotation again.
            Set("layoutIndex", DI_BoardLayouts.IndexOf("halo"));
            Assert.That(Call("TakeNextLayout"), Is.EqualTo((DI_BoardLayouts.IndexOf("halo") + 1) % DI_BoardLayouts.All.Count));
        }

        static bool Flag(object state, string field) => (bool)state.GetType().GetField(field, Members).GetValue(state);
    }
}
