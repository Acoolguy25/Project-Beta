using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Universes.UniverseData.war_valley.Server;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests.Server {
    /// <summary>
    /// Covers the campaign fields of a commander's save: stars per tier, the Endless record and the
    /// retention block, and the repairs applied on load. Server-only, like <see cref="WV_SaveDataTests"/>.
    /// </summary>
    public sealed class WV_CampaignSaveTests {
        const string RunnerPrefab = "Assets/Universes/UniverseData/war_valley/Server/WV_ServerRunner.prefab";

        /// <summary>
        /// The level cap lives in two places, WV_Retention.MaxLevel (the default tuning, the unlock table)
        /// and the runner prefab's serialized RetentionTuning: they must agree, or the track ends early or late.
        /// Read from the prefab's text, so it needs no Editor API; skipped where the project is not the cwd.
        /// </summary>
        [Test]
        public void RunnerPrefab_MaxLevel_MatchesWV_Retention() {
            string path = Path.Combine(Directory.GetCurrentDirectory(), RunnerPrefab);
            if (!File.Exists(path))
                Assert.Ignore($"{RunnerPrefab} is not under the working directory");
            Match block = Regex.Match(File.ReadAllText(path), @"RetentionTuning:\s*\n\s*levels:\s*\n\s*maxLevel:\s*(\d+)");
            Assert.That(block.Success, Is.True, "the prefab has no RetentionTuning.levels.maxLevel");
            Assert.That(int.Parse(block.Groups[1].Value), Is.EqualTo(WV_Retention.MaxLevel));
            foreach (RyanAssets.Retention.UnlockDefinition unlock in WV_Retention.Unlocks)
                Assert.That(unlock.level, Is.LessThanOrEqualTo(WV_Retention.MaxLevel), unlock.id);
        }

        [Test]
        public void RecordStars_KeepsTheBest_AndReportsTheFirstWin() {
            var save = new WV_SaveData();
            Assert.That(save.RecordStars(WV_DifficultyTier.Veteran, 2), Is.True);
            Assert.That(save.RecordStars(WV_DifficultyTier.Veteran, 1), Is.False);
            Assert.That(save.StarsOn(WV_DifficultyTier.Veteran), Is.EqualTo(2));
            Assert.That(save.RecordStars(WV_DifficultyTier.Veteran, 3), Is.False);
            Assert.That(save.StarsOn(WV_DifficultyTier.Veteran), Is.EqualTo(3));
            Assert.That(save.StarsOn(WV_DifficultyTier.Recruit), Is.Zero);
        }

        [Test]
        public void RecordStars_IgnoresLossesAndEndless() {
            var save = new WV_SaveData();
            Assert.That(save.RecordStars(WV_DifficultyTier.Recruit, 0), Is.False);
            Assert.That(save.RecordStars(WV_DifficultyTier.Endless, 3), Is.False);
            Assert.That(save.tierStars, Is.Empty);
        }

        [Test]
        public void RecordEndlessWave_KeepsTheHighest() {
            var save = new WV_SaveData();
            Assert.That(save.RecordEndlessWave(24), Is.True);
            Assert.That(save.RecordEndlessWave(22), Is.False);
            Assert.That(save.bestEndlessWave, Is.EqualTo(24));
        }

        [Test]
        public void Validate_RepairsAnOldOrDamagedSave() {
            var save = new WV_SaveData {
                retention = null,
                tierStars = new List<int> { 9, -2, 1, 0, 0, 3, 3 },
                bestEndlessWave = -5,
                doctrinesPicked = -1
            };
            save.Validate();
            Assert.That(save.retention, Is.Not.Null);
            Assert.That(save.tierStars, Is.EqualTo(new List<int> { 3, 0, 1, 0, 0 }));
            Assert.That(save.bestEndlessWave, Is.Zero);
            Assert.That(save.doctrinesPicked, Is.Zero);

            var old = new WV_SaveData { tierStars = null };
            old.Validate();
            Assert.That(old.tierStars, Is.Not.Null);
        }

        [Test]
        public void WinningRecruit_OpensVeteranInTheSave() {
            var save = new WV_SaveData();
            save.RecordStars(WV_DifficultyTier.Recruit, 1);
            Assert.That(WV_Campaign.IsUnlocked(save.tierStars, WV_DifficultyTier.Veteran), Is.True);
        }
    }
}
