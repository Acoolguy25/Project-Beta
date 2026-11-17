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

        [Test]
        public void FactoriesAndFortressesTakeDistinctBasesAtTheMaximumCount([NUnit.Framework.Range(1, 4)] int count) {
            Set("factoryBaseCount", count);
            Set("fortressBaseCount", count);
            Call("GenerateBases");
            Call("GenerateLinks");
            Call("AssignTurrets");
            Call("AssignSuperBases");
            Call("AssignSpeedBases");
            Call("AssignFactoriesAndFortresses");

            int factories = 0, fortresses = 0;
            foreach (object state in (IList)Get("bases")) {
                bool factory = (bool)state.GetType().GetField("isFactory", Members).GetValue(state);
                bool fortress = (bool)state.GetType().GetField("isFortress", Members).GetValue(state);
                Assert.That(factory && fortress, Is.False, "A base cannot be both a factory and a fortress.");
                if (factory) factories++;
                if (fortress) fortresses++;
            }
            Assert.That(factories, Is.EqualTo(count));
            Assert.That(fortresses, Is.EqualTo(count));
        }
    }
}
