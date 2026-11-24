using NUnit.Framework;
using Universes.UniverseData.obby.Server;

namespace Universes.UniverseData.obby.Tests {
    /// <summary>Sky Obby's saved progress: finishes, medals, places and old saves. Server-only (runs in the Linux Server profile).</summary>
    public sealed class OB_SaveDataTests {
        [Test] public void FinishesKeepTheBestTimesAndCountMedals() {
            var save = new OB_SaveData();
            Assert.IsTrue(save.RecordFinish(300f, 290f, OB_Medal.Silver, 30), "the first finish is a best");
            Assert.IsFalse(save.RecordFinish(310f, 295f, OB_Medal.Silver, 30));
            Assert.IsTrue(save.RecordFinish(305f, 280f, OB_Medal.Gold, 30), "the scored time decides a best, not the clock");
            Assert.AreEqual(300f, save.bestFinishTime);
            Assert.AreEqual(280f, save.bestScoredTime);
            Assert.AreEqual((1, 2, 0), (save.goldMedals, save.silverMedals, save.bronzeMedals));
            Assert.AreEqual((int)OB_Medal.Gold, save.bestMedal);
            Assert.AreEqual(3, save.coursesFinished);
            Assert.AreEqual(30, save.bestStage);
        }

        [Test] public void OnlyTheTopThreeArePodiumsAndOnlyFirstIsAWin() {
            var save = new OB_SaveData();
            foreach (int place in new[] { 1, 2, 3, 4, 0 }) save.RecordPlace(place);
            Assert.AreEqual(1, save.wins);
            Assert.AreEqual(3, save.podiums);
        }

        [Test] public void OldSavesGetTheirFinishAsTheBestToBeat() {
            var old = new OB_SaveData { bestFinishTime = 400f, coursesFinished = 2 };
            old.Validate();
            Assert.AreEqual(400f, old.bestScoredTime);
            Assert.IsFalse(old.RecordFinish(450f, 445f, OB_Medal.None, 30), "a slower run is no NEW BEST");
            Assert.IsTrue(old.RecordFinish(390f, 385f, OB_Medal.None, 30));
        }

        [Test] public void ValidateRepairsBrokenValues() {
            var save = new OB_SaveData { bestFinishTime = float.NaN, bestScoredTime = -3f, bestMedal = 9, wins = -1, totalCoins = -5 };
            save.Validate();
            Assert.AreEqual((0f, 0f), (save.bestFinishTime, save.bestScoredTime));
            Assert.AreEqual((int)OB_Medal.Gold, save.bestMedal);
            Assert.AreEqual(0, save.wins);
            Assert.AreEqual(0, save.totalCoins);
        }

        [Test] public void VersionOneSavesGainEmptyStarsTiersAndRetention() {
            var old = new OB_SaveData { retention = null, tierBestTimes = null, tierBestStars = new[] { 2, 1 }, totalStars = -4 };
            old.Validate();
            Assert.IsNotNull(old.retention);
            Assert.AreEqual(OB_Tiers.Count, old.tierBestTimes.Length);
            Assert.AreEqual(OB_Tiers.Count, old.tierBestStars.Length);
            Assert.AreEqual((2, 1, 0), (old.tierBestStars[0], old.tierBestStars[1], old.tierBestStars[2]), "existing entries are kept");
            Assert.AreEqual(0, old.totalStars);
            Assert.AreEqual(2, new OB_SaveData().LatestVersion);
        }

        [Test] public void TierRecordsKeepTheFastestAndReportThePreviousBest() {
            var save = new OB_SaveData();
            Assert.AreEqual(0f, save.RecordTier(OB_Tier.Hard, 80f, 1, clean: true, perfect: false), "no best before the first clear");
            Assert.AreEqual(80f, save.RecordTier(OB_Tier.Hard, 90f, 3, clean: false, perfect: false));
            Assert.AreEqual(80f, save.RecordTier(OB_Tier.Hard, 70f, 0, clean: true, perfect: true));
            Assert.AreEqual(70f, save.TierBest(OB_Tier.Hard));
            Assert.AreEqual(3, save.tierBestStars[(int)OB_Tier.Hard]);
            Assert.AreEqual((2, 1), (save.cleanTiers, save.perfectTiers));
            Assert.AreEqual(0f, save.TierBest(OB_Tier.Basic));
        }

        [Test] public void StarsAddUpAndTheBestRunIsKept() {
            var save = new OB_SaveData();
            save.RecordStar(1);
            save.RecordStar(2);
            save.RecordStar(1);
            Assert.AreEqual(3, save.totalStars);
            Assert.AreEqual(2, save.bestRunStars);
        }
    }
}
