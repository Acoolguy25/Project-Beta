using System;
using System.Collections.Generic;
using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>Covers the doctrine deck: the catalog, the deal and what picks add up to.</summary>
    public sealed class WV_DoctrineTests {
        /// <summary>WV_CommanderBonuses is static server state: every test starts and ends with none.</summary>
        [SetUp, TearDown]
        public void ResetBonuses() => WV_CommanderBonuses.Clear();

        static readonly Func<string, bool> Everything = _ => true;
        static readonly Func<string, bool> Nothing = _ => false;

        [Test]
        public void Catalog_IsIndexedById_AndCoversEveryId() {
            Assert.That(WV_DoctrineCatalog.All.Count, Is.EqualTo(WV_DoctrineCatalog.Count));
            Assert.That(Enum.GetValues(typeof(WV_DoctrineId)).Length, Is.EqualTo(WV_DoctrineCatalog.Count));
            for (int i = 0; i < WV_DoctrineCatalog.All.Count; i++) {
                WV_DoctrineDefinition doctrine = WV_DoctrineCatalog.All[i];
                Assert.That((int)doctrine.Id, Is.EqualTo(i));
                Assert.That(doctrine.Title, Is.Not.Empty);
                Assert.That(doctrine.Description, Is.Not.Empty);
                Assert.That(doctrine.Icon, Is.Not.Empty);
                Assert.That(doctrine.MaxRank, Is.GreaterThanOrEqualTo(1));
            }
        }

        [Test]
        public void LockedCards_NameARealLevelUnlock() {
            foreach (WV_DoctrineDefinition doctrine in WV_DoctrineCatalog.All) {
                if (doctrine.UnlockId != null)
                    Assert.That(WV_Retention.LevelOf(doctrine.UnlockId), Is.GreaterThan(1), doctrine.Title);
                if (doctrine.Rarity == WV_DoctrineRarity.Elite)
                    Assert.That(doctrine.UnlockId, Is.EqualTo(WV_Retention.EliteDoctrines), doctrine.Title);
            }
        }

        [Test]
        public void Deal_IsDeterministic_AndVariesWithTheSeed() {
            byte[] ranks = WV_DoctrineCatalog.NewRanks();
            List<WV_DoctrineId> first = WV_DoctrineCatalog.Deal(42, ranks, 3, Everything, 0.15f);
            List<WV_DoctrineId> again = WV_DoctrineCatalog.Deal(42, ranks, 3, Everything, 0.15f);
            Assert.That(again, Is.EqualTo(first));

            var hands = new HashSet<string>();
            for (int seed = 0; seed < 20; seed++)
                hands.Add(string.Join(",", WV_DoctrineCatalog.Deal(seed, ranks, 3, Everything, 0.15f)));
            Assert.That(hands.Count, Is.GreaterThan(5), "different deals should give different hands");
        }

        [Test]
        public void Deal_GivesDifferentCards_UpToTheHandSize() {
            byte[] ranks = WV_DoctrineCatalog.NewRanks();
            for (int seed = 0; seed < 50; seed++) {
                List<WV_DoctrineId> hand = WV_DoctrineCatalog.Deal(seed, ranks, 4, Everything, 0.5f);
                Assert.That(hand.Count, Is.EqualTo(4));
                Assert.That(new HashSet<WV_DoctrineId>(hand).Count, Is.EqualTo(hand.Count), $"seed {seed}");
            }
        }

        [Test]
        public void Deal_LeavesOutLockedAndMaxedCards() {
            byte[] ranks = WV_DoctrineCatalog.NewRanks();
            ranks[(int)WV_DoctrineId.WarBonds] = (byte)WV_DoctrineCatalog.Get(WV_DoctrineId.WarBonds).MaxRank;
            for (int seed = 0; seed < 200; seed++) {
                foreach (WV_DoctrineId id in WV_DoctrineCatalog.Deal(seed, ranks, 4, Nothing, 1f)) {
                    WV_DoctrineDefinition doctrine = WV_DoctrineCatalog.Get(id);
                    Assert.That(doctrine.UnlockId, Is.Null, $"{doctrine.Title} is locked");
                    Assert.That(id, Is.Not.EqualTo(WV_DoctrineId.WarBonds), "maxed cards are not dealt again");
                }
            }
        }

        [Test]
        public void EliteCards_ComeOnlyWhenUnlockedAndRolled() {
            byte[] ranks = WV_DoctrineCatalog.NewRanks();
            bool sawElite = false;
            for (int seed = 0; seed < 200; seed++) {
                foreach (WV_DoctrineId id in WV_DoctrineCatalog.Deal(seed, ranks, 3, Everything, 0f))
                    Assert.That(WV_DoctrineCatalog.Get(id).Rarity, Is.EqualTo(WV_DoctrineRarity.Common));
                foreach (WV_DoctrineId id in WV_DoctrineCatalog.Deal(seed, ranks, 3, Everything, 0.3f))
                    sawElite |= WV_DoctrineCatalog.Get(id).Rarity == WV_DoctrineRarity.Elite;
            }
            Assert.That(sawElite, Is.True);
        }

        [Test]
        public void Deal_RunsDryWhenEveryCardIsMaxed() {
            byte[] ranks = WV_DoctrineCatalog.NewRanks();
            foreach (WV_DoctrineDefinition doctrine in WV_DoctrineCatalog.All)
                ranks[(int)doctrine.Id] = (byte)doctrine.MaxRank;
            Assert.That(WV_DoctrineCatalog.Deal(1, ranks, 3, Everything, 0.5f), Is.Empty);
        }

        [Test]
        public void Ranks_AddUpToTheirBonuses() {
            byte[] ranks = WV_DoctrineCatalog.NewRanks();
            Assert.That(WV_DoctrineCatalog.Sum(ranks).BountyMultiplier, Is.EqualTo(1f));
            ranks[(int)WV_DoctrineId.WarBonds] = 2;
            ranks[(int)WV_DoctrineId.DrillSergeants] = 1;
            ranks[(int)WV_DoctrineId.BountyHunters] = 3;
            WV_CommanderModifiers sum = WV_DoctrineCatalog.Sum(ranks);
            Assert.That(sum.BountyMultiplier, Is.EqualTo(1.6f).Within(1e-4f));
            Assert.That(sum.ProductionTimeMultiplier, Is.EqualTo(1f / 1.18f).Within(1e-4f));
            Assert.That(sum.FundsPerKill, Is.EqualTo(18));
            Assert.That(sum.TroopHealthMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void Ranks_AboveTheMaximum_CountOnlyToTheMaximum() {
            byte[] ranks = WV_DoctrineCatalog.NewRanks();
            ranks[(int)WV_DoctrineId.TotalWar] = 9;
            Assert.That(WV_DoctrineCatalog.Sum(ranks).TurretDamageMultiplier, Is.EqualTo(1.25f).Within(1e-4f));
        }

        [Test]
        public void Scale_NeverRoundsARealAmountToNothing() {
            Assert.That(WV_CommanderModifiers.Scale(1, 0.2f), Is.EqualTo(1));
            Assert.That(WV_CommanderModifiers.Scale(0, 3f), Is.EqualTo(0));
            Assert.That(WV_CommanderModifiers.Scale(100, 1.25f), Is.EqualTo(125));
        }

        [Test]
        public void WarChest_PaysMoreLaterInTheRound() {
            WV_DoctrineDefinition chest = WV_DoctrineCatalog.Get(WV_DoctrineId.WarChest);
            Assert.That(chest.GetInstantFunds(8), Is.GreaterThan(chest.GetInstantFunds(2)));
        }

        [Test]
        public void Bonuses_DefaultToNothingForAnUnknownCommander() {
            WV_CommanderBonuses.Clear();
            WV_CommanderModifiers none = WV_CommanderBonuses.For(1234);
            Assert.That(none.IncomeMultiplier, Is.EqualTo(1f));
            Assert.That(none.ProductionTimeMultiplier, Is.EqualTo(1f));
            WV_CommanderBonuses.Set(7, new WV_CommanderModifiers { IncomeBonus = 0.2f });
            Assert.That(WV_CommanderBonuses.For(7).IncomeMultiplier, Is.EqualTo(1.2f).Within(1e-4f));
            WV_CommanderBonuses.Clear();
            Assert.That(WV_CommanderBonuses.For(7).IncomeMultiplier, Is.EqualTo(1f));
        }
    }
}
