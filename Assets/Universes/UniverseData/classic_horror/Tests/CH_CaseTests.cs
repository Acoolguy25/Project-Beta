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
                Assert.AreEqual(9 + CH_Case.DiaryPageCount, c.LocationIndices.Distinct().Count());
                Assert.AreEqual(c.LocationIndices.Length, c.LocationIndices.Distinct().Count());
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

        [Test] public void PriestDiaryIsOptionalNamedAndConsistent() {
            var c = library.Generate(211, 16, 3);
            Assert.AreEqual(CH_Case.DiaryPageCount, c.Diary.Length);
            Assert.IsFalse(string.IsNullOrEmpty(c.Priest));
            foreach (string page in c.Diary) {
                StringAssert.Contains(c.Priest, page);
                StringAssert.DoesNotContain("{", page);
            }
            foreach (string line in c.EvidenceReplies) StringAssert.DoesNotContain("{witness}", line);
            StringAssert.Contains(c.Priest, c.ChapterTwoLine);
            Assert.IsTrue(c.CollectDiary(0)); Assert.IsFalse(c.CollectDiary(0)); Assert.IsFalse(c.CollectDiary(3));
            Assert.IsFalse(c.DiaryComplete);
            Assert.IsTrue(c.CollectDiary(1)); Assert.IsTrue(c.CollectDiary(2));
            Assert.IsTrue(c.DiaryComplete);
            Assert.AreEqual(CH_Phase.Investigation, c.Phase, "Diary pages never advance the case.");
        }

        [Test] public void SmallMapsSkipDiaryPages() {
            var c = library.Generate(5, 9, 3);
            Assert.AreEqual(9, c.LocationIndices.Length);
            Assert.AreEqual(0, c.Diary.Length);
            Assert.IsFalse(c.CollectDiary(0));
            Assert.IsFalse(c.DiaryComplete);
        }

        [Test] public void FailedCasesRejectProgressAndInvalidIds() {
            var c = library.Generate(103, 16, 3);
            Assert.IsFalse(c.Collect(-1)); Assert.IsFalse(c.Collect(9));
            c.Fail();
            Assert.IsFalse(c.Collect(0)); Assert.IsFalse(c.Offer(0)); Assert.IsFalse(c.Extract());
            Assert.AreEqual(CH_Phase.Failed, c.Phase);
        }

        [Test] public void EveryCaseTextIsFullyExpanded() {
            for (int seed = 1; seed <= 256; seed++) {
                var c = library.Generate(unchecked(seed * 104729), 16, 3);
                c.BindSource("the drowned chapel");
                foreach (string text in AllTexts(c))
                    Assert.IsFalse(text.Contains("{"), $"Seed {seed} left a token in: {text}");
                Assert.AreEqual(CH_Case.StoryBeatCount, c.StoryBeats.Length);
                Assert.AreEqual(CH_Case.AmbientScareCount, c.AmbientLines.Length);
                Assert.AreEqual(CH_Case.Offerings.Length, c.OfferingLines.Length);
            }
        }

        [Test] public void SourceIsBoundIntoTheStory() {
            var c = library.Generate(77, 16, 3);
            c.BindSource("the flooded mill");
            StringAssert.Contains("the flooded mill", c.Evidence[3]);
            StringAssert.Contains("the flooded mill", c.StoryBeats[2]);
        }

        [Test] public void EditingStoryTextNeverMovesTheLayout() {
            var a = library.Generate(9001, 16, 3);
            var edited = ScriptableObject.CreateInstance<CH_StoryLibrary>();
            try {
                edited.witnesses = new[] { "Only Witness" };
                edited.dispatch = new[] { "DISPATCH / One line." };
                edited.memories = new[] { "m1", "m2", "m3" };
                var b = edited.Generate(9001, 16, 3);
                Assert.AreEqual(a.Temperament, b.Temperament);
                Assert.AreEqual(a.SourceIndex, b.SourceIndex);
                CollectionAssert.AreEqual(a.Order, b.Order);
                CollectionAssert.AreEqual(a.LocationIndices, b.LocationIndices);
            } finally { UnityEngine.Object.DestroyImmediate(edited); }
        }

        [Test] public void EndingsFollowMemoriesAndDiary() {
            CH_Case Solved(bool memories, bool diary) {
                var c = library.Generate(4242, 16, 3);
                if (memories) { c.Collect(4); c.Collect(5); }
                if (diary) for (int page = 0; page < c.Diary.Length; page++) c.CollectDiary(page);
                for (int id = 0; id < 4; id++) c.Collect(id);
                for (int id = 6; id <= 8; id++) c.Collect(id);
                foreach (int offering in c.Order) c.Offer(offering);
                Assert.IsTrue(c.Extract());
                return c;
            }
            Assert.AreEqual(CH_Case.EndingLastRites, Solved(true, true).EndingIndex());
            Assert.AreEqual(CH_Case.EndingWaterRemembers, Solved(true, false).EndingIndex());
            Assert.AreEqual(CH_Case.EndingNameForName, Solved(false, true).EndingIndex());
            Assert.AreEqual(CH_Case.EndingBorrowedSilence, Solved(false, false).EndingIndex());
            var failed = library.Generate(4242, 16, 3);
            failed.Fail();
            Assert.AreEqual(CH_Case.EndingStillMissing, failed.EndingIndex());
            StringAssert.StartsWith("STILL MISSING\n", failed.Ending());
            StringAssert.Contains(failed.Witness, failed.Ending());
        }

        [Test] public void StoryLinesSplitIntoSpeakerAndBody() {
            CH_StoryText.Split("DISPATCH / Find four records.", out string speaker, out string body);
            Assert.AreEqual("DISPATCH", speaker);
            Assert.AreEqual("Find four records.", body);
            CH_StoryText.Split("No speaker here.", out speaker, out body);
            Assert.AreEqual("", speaker);
            Assert.AreEqual("No speaker here.", body);
        }

        static IEnumerable<string> AllTexts(CH_Case c) {
            yield return c.Title;
            yield return c.Introduction;
            yield return c.ChapterTwoLine;
            foreach (var group in new[] { c.Evidence, c.Warnings, c.Diary, c.EvidenceReplies, c.StoryBeats, c.OfferingLines, c.MistakeLines, c.AmbientLines, c.Endings })
                foreach (string text in group) yield return text;
        }

        [Test] public void IncompleteMapsFailExplicitly() {
            Assert.Throws<ArgumentException>(() => library.Generate(1, 8, 3));
            Assert.Throws<ArgumentException>(() => library.Generate(1, 16, 0));
        }
    }
}
