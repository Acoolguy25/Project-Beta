using NUnit.Framework;

namespace Universes.UniverseData.tycoon.Tests {
    // Brick Buddies: crate odds, the pity guarantee, levels, the income they add and how they are saved.
    public class TY_PetTests {
        [Test]
        public void EveryRarityHasAPetAndNamesAreUnique() {
            foreach (TY_Rarity rarity in System.Enum.GetValues(typeof(TY_Rarity)))
                Assert.AreEqual(rarity, TY_Pets.All[TY_PetCrate.PickPet(rarity, 0.5)].rarity, $"no {rarity} pet");
            var names = new System.Collections.Generic.HashSet<string>();
            foreach (TY_Pet pet in TY_Pets.All) Assert.IsTrue(names.Add(pet.name), pet.name);
        }

        [Test]
        public void RollsFollowTheWeights() {
            var crate = new TY_PetCrate();
            crate.Validate();
            var counts = new int[5];
            const int n = 10_000;
            for (int i = 0; i < n; i++) counts[(int)crate.RollRarity((i + 0.5) / n, 0)]++;
            int total = 0;
            foreach (int w in crate.rarityWeights) total += w;
            for (int r = 0; r < counts.Length; r++)
                Assert.AreEqual((double)crate.rarityWeights[r] / total, counts[r] / (double)n, 0.002, $"{(TY_Rarity)r} odds");
        }

        [Test]
        public void PityGuaranteesAnEpicAfterADryRun() {
            var crate = new TY_PetCrate { pityCrates = 15 };
            crate.Validate();
            Assert.AreEqual(TY_Rarity.Common, crate.RollRarity(0.0, 13));
            // The 15th crate in a row without an Epic is at least Epic, even on the worst roll.
            Assert.GreaterOrEqual(crate.RollRarity(0.0, 14), TY_Rarity.Epic);
            Assert.AreEqual(1, crate.UntilPity(14));
            Assert.AreEqual(15, crate.UntilPity(0));
            Assert.AreEqual(0, TY_PetCrate.NextSinceEpic(9, TY_Rarity.Legendary));
            Assert.AreEqual(10, TY_PetCrate.NextSinceEpic(9, TY_Rarity.Rare));
        }

        [Test]
        public void HatchesLevelUpToTheMaxThenRefund() {
            byte[] levels = TY_Pets.Repair(null);
            TY_Hatch first = TY_PetCrate.Apply(levels, 4);
            Assert.IsTrue(first.isNew);
            Assert.AreEqual(1, first.level);
            for (int i = 2; i <= TY_Pets.MaxLevel; i++) Assert.AreEqual(i, TY_PetCrate.Apply(levels, 4).level);
            TY_Hatch extra = TY_PetCrate.Apply(levels, 4);
            Assert.IsTrue(extra.maxed);
            Assert.AreEqual(TY_Pets.MaxLevel, levels[4]);
            Assert.IsTrue(TY_Hatch.MaxedFlag(extra.Flags));
            Assert.IsFalse(TY_Hatch.IsNewFlag(extra.Flags));
        }

        [Test]
        public void TheBestPetAndTheCollectionSetTheIncome() {
            byte[] levels = TY_Pets.Repair(null);
            Assert.AreEqual(-1, TY_Pets.Best(levels));
            Assert.AreEqual(1d, TY_Pets.Multiplier(levels), 1e-9);
            levels[0] = 1;  // Brick Pup, +5%
            levels[4] = 3;  // Kiln Fox Lv 3: +20% x 1.4 = +28%
            Assert.AreEqual(4, TY_Pets.Best(levels));
            Assert.AreEqual(1.28 * 1.04, TY_Pets.Multiplier(levels), 1e-9);
            // A whole maxed set: Legendary x1.8 and +2% for each of the ten.
            for (int i = 0; i < levels.Length; i++) levels[i] = TY_Pets.MaxLevel;
            Assert.AreEqual((1 + 0.6 * 1.8) * (1 + 0.02 * TY_Pets.Count), TY_Pets.Multiplier(levels), 1e-9);
        }

        [Test]
        public void CratePriceIsSecondsOfIncomeWithAFloor() {
            var crate = new TY_PetCrate();
            crate.Validate();
            Assert.AreEqual(crate.minimumPrice, crate.Price(0));
            Assert.AreEqual(crate.minimumPrice, crate.Price(double.NaN));
            Assert.AreEqual(75_000, crate.Price(1000));
        }

        [Test]
        public void ACollectionFromAnOldSaveIsRepaired() {
            byte[] repaired = TY_Pets.Repair(new byte[] { 2, 0, 9 });
            Assert.AreEqual(TY_Pets.Count, repaired.Length);
            Assert.AreEqual(2, repaired[0]);
            Assert.AreEqual(TY_Pets.MaxLevel, repaired[2]);
            Assert.AreEqual(0, repaired[TY_Pets.Count - 1]);
            Assert.AreEqual(TY_Pets.Count, TY_Pets.Repair(new byte[TY_Pets.Count + 4]).Length);
        }
    }
}
