using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Tests {
    public sealed class CH_CaseTests {
        CH_StoryLibrary library;
        [SetUp] public void SetUp() => library = ScriptableObject.CreateInstance<CH_StoryLibrary>();
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(library);

        [Test] public void SameSeedReproducesStoryLayoutAndSolution() {
            var a = library.Generate(51793, 16, 3);
            var b = library.Generate(51793, 16, 3);
            Assert.AreEqual(a.Title, b.Title);
            Assert.AreEqual(a.Introduction, b.Introduction);
            CollectionAssert.AreEqual(a.Evidence, b.Evidence);
            CollectionAssert.AreEqual(a.Order, b.Order);
            CollectionAssert.AreEqual(a.LocationIndices, b.LocationIndices);
        }

        [Test] public void EverySampledCaseHasUniquePlacementsAndSolvableTwoChapterProgression() {
            var stories = new HashSet<string>();
            var rules = new HashSet<CH_Temperament>();
            var sources = new HashSet<int>();
            var orders = new HashSet<string>();
            for (int seed = 1; seed <= 512; seed++) {
                var c = library.Generate(unchecked(seed * 7919), 16, 3);
                Assert.AreEqual(9, c.LocationIndices.Distinct().Count());
                Assert.IsTrue(c.LocationIndices.All(i => i >= 0 && i < 16));
                CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, c.Order);
                Assert.IsFalse(c.Collect(6), "Chapter II items must not unlock early.");
                Assert.IsFalse(c.Offer(c.Order[0]));
                Assert.IsFalse(c.Extract());
                Assert.IsTrue(c.Collect(0)); Assert.IsFalse(c.Collect(0), "Duplicate requests must be idempotent.");
                Assert.IsTrue(c.Collect(1)); Assert.IsTrue(c.Collect(2)); Assert.IsTrue(c.Collect(3));
                Assert.AreEqual(CH_Phase.Descent, c.Phase);
                for (int id = 6; id <= 8; id++) Assert.IsTrue(c.Collect(id));
                int wrong = (c.Order[0] + 1) % 3;
                Assert.IsFalse(c.Offer(wrong)); Assert.AreEqual(0, c.RitualStep); Assert.AreEqual(1, c.Mistakes);
                foreach (int offering in c.Order) Assert.IsTrue(c.Offer(offering));
                Assert.AreEqual(CH_Phase.Escape, c.Phase);
                Assert.IsTrue(c.Extract()); Assert.AreEqual(CH_Phase.Complete, c.Phase);
                Assert.IsFalse(c.Extract()); Assert.IsFalse(c.Collect(4));
                stories.Add(c.Title + c.Introduction + string.Join("|", c.LocationIndices));
                rules.Add(c.Temperament); sources.Add(c.SourceIndex); orders.Add(string.Join(",", c.Order));
            }
            Assert.Greater(stories.Count, 500);
            Assert.AreEqual(3, rules.Count); Assert.AreEqual(3, sources.Count); Assert.AreEqual(6, orders.Count);
        }

        [Test] public void OptionalMemoriesDoNotSkipRequiredEvidence() {
            var c = library.Generate(101, 16, 3);
            Assert.IsTrue(c.Collect(4)); Assert.IsTrue(c.Collect(5));
            Assert.AreEqual(2, c.MemoryCount); Assert.AreEqual(0, c.EvidenceCount);
            Assert.AreEqual(CH_Phase.Investigation, c.Phase);
        }

        [Test] public void FailedCasesRejectProgressAndInvalidIds() {
            var c = library.Generate(103, 16, 3);
            Assert.IsFalse(c.Collect(-1)); Assert.IsFalse(c.Collect(9));
            c.Fail();
            Assert.IsFalse(c.Collect(0)); Assert.IsFalse(c.Offer(0)); Assert.IsFalse(c.Extract());
            Assert.AreEqual(CH_Phase.Failed, c.Phase);
        }

        [Test] public void IncompleteMapsFailExplicitly() {
            Assert.Throws<ArgumentException>(() => library.Generate(1, 8, 3));
            Assert.Throws<ArgumentException>(() => library.Generate(1, 16, 0));
        }
    }
}
