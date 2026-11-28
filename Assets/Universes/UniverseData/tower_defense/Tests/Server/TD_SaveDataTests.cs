using NUnit.Framework;

namespace Universes.UniverseData.tower_defense.Server.Tests {
    /// <summary>
    /// The saved record (<see cref="TD_SaveData"/>): the version 1 to 2 migration, per-mode wins and stars,
    /// bests, and the repairs Validate makes to a damaged save. A server assembly (UNITY_SERVER), like the
    /// save itself: run it from the server Editor or a Linux Server build profile.
    /// </summary>
    public sealed class TD_SaveDataTests {
        [Test]
        public void AVersionOneSaveTurnsItsWinsIntoNormalWinsAndOpensHard() {
            var save = new TD_SaveData { gamesWon = 3, gamesPlayed = 5 };
            Assert.IsFalse(save.HardUnlocked);
            save.Migrate(1, null);
            Assert.AreEqual(3, save.normalWins, "version 1 only had Normal");
            Assert.AreEqual(0, save.hardWins);
            Assert.IsTrue(save.HardUnlocked, "an old Normal win opens Hard");
        }

        [Test]
        public void AVersionTwoSaveIsLeftAlone() {
            var save = new TD_SaveData { gamesWon = 4, normalWins = 1, hardWins = 3 };
            save.Migrate(2, null);
            Assert.AreEqual(1, save.normalWins);
            Assert.AreEqual(3, save.hardWins);
            Assert.AreEqual(2, save.LatestVersion);
        }

        [Test]
        public void AVersionOneSaveWithNoWinsStaysLocked() {
            var save = new TD_SaveData { gamesPlayed = 2 };
            save.Migrate(1, null);
            Assert.AreEqual(0, save.normalWins);
            Assert.IsFalse(save.HardUnlocked);
        }

        [Test]
        public void WinsCountPerModeAndKeepTheBestStars() {
            var save = new TD_SaveData();
            Assert.IsTrue(save.RecordWin(TD_Difficulty.Normal, 2), "first stars on Normal");
            Assert.IsFalse(save.RecordWin(TD_Difficulty.Normal, 1), "fewer stars are not a best");
            Assert.IsTrue(save.RecordWin(TD_Difficulty.Normal, 3));
            Assert.AreEqual(3, save.BestStars(TD_Difficulty.Normal));
            Assert.AreEqual(3, save.normalWins);
            Assert.IsTrue(save.HardUnlocked);

            Assert.IsTrue(save.RecordWin(TD_Difficulty.Hard, 9), "stars are clamped, not refused");
            Assert.AreEqual(TD_Rules.MaxStars, save.BestStars(TD_Difficulty.Hard));
            Assert.AreEqual(1, save.hardWins);

            Assert.IsFalse(save.RecordWin(TD_Difficulty.Endless, 3), "Endless has no stars");
            Assert.AreEqual(0, save.BestStars(TD_Difficulty.Endless));
            Assert.AreEqual(5, save.gamesWon, "every win counts in the total");
        }

        [Test]
        public void BestWavesOnlyGoUp() {
            var save = new TD_SaveData();
            Assert.IsTrue(save.RecordWave(7));
            Assert.IsFalse(save.RecordWave(7));
            Assert.IsFalse(save.RecordWave(3));
            Assert.AreEqual(7, save.bestWave);
            Assert.IsTrue(save.RecordEndlessWave(22));
            Assert.IsFalse(save.RecordEndlessWave(21));
            Assert.AreEqual(22, save.bestEndlessWave);
        }

        [Test]
        public void ValidateRepairsADamagedSave() {
            var save = new TD_SaveData {
                bestWave = -4, totalKills = -10, normalWins = 2, hardWins = -1, gamesWon = 0, gamesPlayed = 1,
                towersBuilt = -1, towersUpgraded = -2, bossesDestroyed = -3, bestStarsNormal = 7, bestStarsHard = -2,
                bestEndlessWave = -5, shieldsBroken = -6, abilitiesUsed = -7, elitesMade = -8, retention = null,
            };
            save.Validate();
            Assert.AreEqual(0, save.bestWave);
            Assert.AreEqual(0, save.totalKills);
            Assert.AreEqual(0, save.hardWins);
            Assert.AreEqual(2, save.gamesWon, "wins cover the per-mode wins");
            Assert.AreEqual(2, save.gamesPlayed, "every win was a game played");
            Assert.AreEqual(0, save.towersBuilt);
            Assert.AreEqual(0, save.towersUpgraded);
            Assert.AreEqual(0, save.bossesDestroyed);
            Assert.AreEqual(0, save.elitesMade);
            Assert.AreEqual(TD_Rules.MaxStars, save.bestStarsNormal);
            Assert.AreEqual(0, save.bestStarsHard);
            Assert.AreEqual(0, save.bestEndlessWave);
            Assert.AreEqual(0, save.shieldsBroken);
            Assert.AreEqual(0, save.abilitiesUsed);
            Assert.IsNotNull(save.retention, "a missing retention block is rebuilt");
            Assert.IsTrue(save.HardUnlocked);
        }
    }
}
