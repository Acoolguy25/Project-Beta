using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Tests {
    /// <summary>The story's data agrees with itself: every line it names exists, and no two cutscene lines overlap.</summary>
    public sealed class CH_StoryTests {
        const string BankPath = "Assets/Universes/UniverseData/classic_horror/Data/VoiceBank.asset";
        static readonly CH_Phase[] Cutscenes = { CH_Phase.Prologue, CH_Phase.Taking, CH_Phase.ChapelOpens, CH_Phase.Departure };

        static CH_VoiceBank Bank() {
            var bank = AssetDatabase.LoadAssetAtPath<CH_VoiceBank>(BankPath);
            Assert.IsNotNull(bank, "VoiceBank.asset is missing; run Ryan > Classic Horror > Rebuild Voice Bank.");
            return bank;
        }

        static string[] NamedLines() {
            var lines = Cutscenes.SelectMany(phase => CH_Story.CutsceneLines(phase).Select(cue => cue.line)).ToList();
            lines.AddRange(CH_Story.Taunts);
            lines.AddRange(CH_Story.LostLines);
            lines.AddRange(CH_Story.CatchLines);
            lines.AddRange(CH_Story.Whispers);
            foreach (CH_ItemKind kind in System.Enum.GetValues(typeof(CH_ItemKind)))
                for (int singer = 0; singer < CH_Story.SingerCount; singer++) {
                    lines.Add(CH_Story.PickUpLine(kind, singer));
                    lines.Add(CH_Story.PlaceLine(kind, singer));
                }
            return lines.Where(line => line != null).Distinct().ToArray();
        }

        [Test] public void EveryLineTheStoryNamesHasAClip() {
            CH_VoiceBank bank = Bank();
            foreach (string id in NamedLines()) {
                Assert.IsTrue(bank.TryGet(id, out CH_VoiceBank.Line line), $"'{id}' is not in the voice bank.");
                Assert.IsNotNull(line.clip, $"'{id}' has no clip.");
                Assert.Greater(line.seconds, 0.3f, $"'{id}' is silent.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(line.text), $"'{id}' has no subtitle.");
            }
        }

        [Test] public void CutsceneLinesNeverOverlapAndFinishInsideTheirScene() {
            CH_VoiceBank bank = Bank();
            foreach (CH_Phase phase in Cutscenes) {
                var cues = CH_Story.CutsceneLines(phase).OrderBy(cue => cue.at).ToArray();
                for (int i = 0; i < cues.Length; i++) {
                    float ends = cues[i].at + bank.Seconds(cues[i].line);
                    if (i + 1 < cues.Length) Assert.LessOrEqual(ends, cues[i + 1].at, $"{phase}: '{cues[i].line}' runs into '{cues[i + 1].line}'.");
                    else Assert.LessOrEqual(ends, CH_Story.CutsceneSeconds(phase) + 0.5f, $"{phase}: '{cues[i].line}' outlasts the scene.");
                }
            }
        }

        [Test] public void ShotAtWalksThroughShotsAndHoldsTheLast() {
            var scene = new CH_CutsceneSet.Scene { shots = new[] { new CH_CutsceneSet.Shot { seconds = 2f }, new CH_CutsceneSet.Shot { seconds = 4f } } };
            Assert.AreEqual(0, CH_CutsceneSet.ShotAt(scene, 1f, out float early));
            Assert.AreEqual(0.5f, early, 1e-4f);
            Assert.AreEqual(1, CH_CutsceneSet.ShotAt(scene, 3f, out float middle));
            Assert.AreEqual(0.25f, middle, 1e-4f);
            Assert.AreEqual(1, CH_CutsceneSet.ShotAt(scene, 60f, out float late));
            Assert.AreEqual(1f, late, 1e-4f);
        }

        [Test] public void WarningsAreRationedPerInvestigator() {
            var ration = new CH_ScareRation();
            Assert.IsTrue(ration.TryTake(0f, 2, 60f));
            Assert.IsFalse(ration.TryTake(30f, 2, 60f), "Inside the cooldown.");
            Assert.IsTrue(ration.TryTake(61f, 2, 60f));
            Assert.IsFalse(ration.TryTake(500f, 2, 60f), "The night's allowance is spent.");
        }

        [Test] public void ThePriestOutrunsAWalkerAndHearsTheBellFarther() {
            var tuning = new CH_MonsterTuning();
            Assert.Less(tuning.ChaseSpeed(1, false, 3), tuning.ChaseSpeed(3, false, 3), "He gets faster each act.");
            Assert.Less(tuning.ChaseSpeed(1, false, 1), tuning.ChaseSpeed(1, false, 3), "A lone survivor gets a little slack.");
            Assert.Greater(tuning.ChaseSpeed(1, true, 3), tuning.ChaseSpeed(3, false, 3), "Whoever carries the bell is chased hardest.");
            Assert.Greater(tuning.ChaseSpeed(1, false, 1), CH_MonsterTuning.InvestigatorWalkSpeed, "Walking never escapes a chase.");
            Assert.Less(tuning.ChaseSpeed(3, true, 3), CH_MonsterTuning.InvestigatorSprintSpeed, "A sprint always opens a gap, even with the bell.");
            Assert.Greater(tuning.HearingRange(0f, true), tuning.HearingRange(99f, false));
            Assert.AreEqual(0f, tuning.HearingRange(0f, false), "Standing still is silent.");
            Assert.Greater(tuning.SightRange(true, false), tuning.SightRange(false, false), "A lit flashlight carries.");
            Assert.IsTrue(tuning.InSightCone(Vector3.forward, Vector3.forward));
            Assert.IsFalse(tuning.InSightCone(Vector3.forward, Vector3.back));
        }
    }
}
