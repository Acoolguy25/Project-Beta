using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Universes.UniverseData.classic_horror.Tests {
    /// <summary>The record kept between sessions: old saves carry over, endings count once, and nothing goes out of range.</summary>
    public sealed class CH_SaveDataTests {
        [Test] public void TheInvestigationGamesSavesCarryOver() {
            var save = new CH_SaveData();
            save.Migrate(1, new JObject { ["casesCompleted"] = 4, ["fastestCaseSeconds"] = 512, ["evidenceFound"] = 19 });
            Assert.AreEqual(4, save.escapes, "Closed cases count as escapes.");
            Assert.AreEqual(4, save.nightsPlayed, "Every escape was a night played.");
            Assert.AreEqual(512, save.fastestEscapeSeconds);
            Assert.AreEqual(19, save.itemsDelivered);
        }

        [Test] public void ACurrentSaveIsLeftAlone() {
            var save = new CH_SaveData { escapes = 2, nightsPlayed = 5 };
            save.Migrate(2, new JObject { ["casesCompleted"] = 9 });
            Assert.AreEqual(2, save.escapes);
            Assert.AreEqual(5, save.nightsPlayed);
        }

        [Test] public void EndingsAreRecordedWithTheBestTime() {
            var save = new CH_SaveData();
            save.RecordEnding(CH_Story.EndingEscaped, 600);
            save.RecordEnding(CH_Story.EndingTrue, 458);
            save.RecordEnding(CH_Story.EndingDrowned, 120);
            Assert.AreEqual(3, save.nightsPlayed);
            Assert.AreEqual(2, save.escapes, "Drowning is not an escape.");
            Assert.AreEqual(1, save.trueEndings);
            Assert.AreEqual(458, save.fastestEscapeSeconds, "Only escapes set the record, and only a quicker one.");
            Assert.AreEqual((1 << CH_Story.EndingEscaped) | (1 << CH_Story.EndingTrue) | (1 << CH_Story.EndingDrowned), save.endingsSeen);
        }

        [Test] public void ValidateKeepsEveryCountInRange() {
            var save = new CH_SaveData { nightsPlayed = 3, escapes = 7, trueEndings = 9, endingsSeen = -1, singersMet = -1, timesCaught = -4, fastestEscapeSeconds = -1 };
            save.Validate();
            Assert.AreEqual(3, save.escapes, "No more escapes than nights.");
            Assert.AreEqual(3, save.trueEndings, "No more true endings than escapes.");
            Assert.AreEqual((1 << CH_Story.EndingCount) - 1, save.endingsSeen);
            Assert.AreEqual((1 << CH_Story.SingerCount) - 1, save.singersMet);
            Assert.AreEqual(0, save.timesCaught);
            Assert.AreEqual(0, save.fastestEscapeSeconds);
        }
    }
}
