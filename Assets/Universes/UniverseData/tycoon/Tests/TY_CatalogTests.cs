using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RyanAssets.Rounds;
using UnityEngine;

namespace Universes.UniverseData.tycoon.Tests {
    public class TY_CatalogTests {
        static readonly TY_PerkLevels NoPerks = default;

        [Test]
        public void EveryItemIsReachableAndLanesAreChains() {
            var random = new System.Random(7);
            // With enough rebirths every landmark is for sale too, so the whole catalog must be reachable.
            int rebirths = TY_Catalog.Items.Max(i => i.rebirths);
            for (int run = 0; run < 200; run++) {
                ulong purchased = TY_Catalog.StarterMask;
                int guard = 0;
                while ((purchased & TY_Catalog.AllButRebirth) != TY_Catalog.AllButRebirth) {
                    var available = TY_Catalog.AvailableItems(purchased, rebirths).Where(i => i != TY_Catalog.RebirthIndex).ToList();
                    Assert.IsNotEmpty(available, "build order dead-ends");
                    // One pad per lane: at most one available item per lane, and it is the lane's next item.
                    foreach (var lane in available.GroupBy(i => TY_Catalog.Items[i].kind)) {
                        Assert.AreEqual(1, lane.Count(), $"two {lane.Key} items available at once");
                        Assert.AreEqual(TY_Catalog.NextInLane(purchased, lane.Key), lane.Single());
                    }
                    purchased |= 1ul << available[random.Next(available.Count)];
                    Assert.Less(++guard, 100);
                }
                Assert.IsTrue(TY_Catalog.Available(purchased, TY_Catalog.RebirthIndex, rebirths), "rebirth unlocks once everything is built");
            }
            Assert.Less(TY_Catalog.Count, 64, "purchases are a 64-bit mask");
            Assert.AreEqual(22, TY_Catalog.BaseCount, "the factory a rebirth needs");
        }

        [Test]
        public void TheRebirthNeedsTheFactoryButNotTheLandmarks() {
            Assert.IsFalse(TY_Catalog.Available(TY_Catalog.BaseMask & ~(1ul << 6), TY_Catalog.RebirthIndex, 0), "a missing dropper blocks the rebirth");
            Assert.IsTrue(TY_Catalog.Available(TY_Catalog.BaseMask, TY_Catalog.RebirthIndex, 0), "landmarks are never required");
            Assert.AreEqual(0ul, TY_Catalog.BaseMask & TY_Catalog.LandmarkMask);
            Assert.AreEqual(0ul, TY_Catalog.BaseMask & (1ul << TY_Catalog.RebirthIndex));
        }

        [Test]
        public void PricesRiseAndIncomeGrows() {
            double income = TY_Catalog.Income(TY_Catalog.StarterMask, 0, NoPerks);
            Assert.Greater(income, 0);
            ulong all = TY_Catalog.BaseMask;
            Assert.Greater(TY_Catalog.Income(all, 0, NoPerks), income * 50);
            Assert.AreEqual(TY_Catalog.Income(all, 0, NoPerks) * 1.5, TY_Catalog.Income(all, 1, NoPerks), 1e-6);
            Assert.AreEqual(3.75, TY_Catalog.UpgradeMultiplier(all), 1e-6);
            var perks = new TY_PerkLevels { income = 2 };
            Assert.AreEqual(TY_Catalog.Income(all, 0, NoPerks) * 1.5, TY_Catalog.Income(all, 0, perks), 1e-6);
            for (int i = 0; i < TY_Catalog.Count; i++) {
                var item = TY_Catalog.Items[i];
                if (item.prereq >= 0 && TY_Catalog.Items[item.prereq].kind == item.kind)
                    Assert.Greater(item.price, TY_Catalog.Items[item.prereq].price, $"{item.name} is cheaper than the item before it");
            }
        }

        [Test]
        public void CollectorCapacityGrowsWithVaultsAndPerks() {
            Assert.AreEqual(TY_Catalog.BaseCapacity, TY_Catalog.Capacity(TY_Catalog.StarterMask, NoPerks));
            ulong all = TY_Catalog.BaseMask;
            Assert.Greater(TY_Catalog.Capacity(all, NoPerks), TY_Catalog.Items[TY_Catalog.RebirthIndex].price, "the last vault holds a rebirth");
            Assert.AreEqual(TY_Catalog.Capacity(all, NoPerks) * 2, TY_Catalog.Capacity(all, new TY_PerkLevels { vault = 2 }));
            Assert.IsFalse(TY_Catalog.AutoCollects(TY_Catalog.StarterMask));
            Assert.IsTrue(TY_Catalog.AutoCollects(all));
        }

        [Test]
        public void FirstRebirthTakesMinutesNotHours() {
            // Greedy player: buys the cheapest available upgrade once it can afford it, collecting in time.
            ulong purchased = TY_Catalog.StarterMask;
            double cash = 0, seconds = 0;
            while (true) {
                int next = TY_Catalog.AvailableItems(purchased, 0).OrderBy(i => TY_Catalog.Price(i, 0)).First();
                double need = TY_Catalog.Price(next, 0) - cash;
                if (need > 0) { double dt = need / TY_Catalog.Income(purchased, 0, NoPerks); seconds += dt; cash += need; }
                cash -= TY_Catalog.Price(next, 0);
                if (next == TY_Catalog.RebirthIndex) break;
                purchased |= 1ul << next;
            }
            Assert.That(seconds, Is.InRange(5 * 60, 20 * 60), $"first rebirth after {seconds / 60:0.0} min");
        }

        [Test]
        public void PerkCostsAndLevels() {
            var levels = new TY_PerkLevels();
            foreach (var perk in TY_Perks.All) {
                Assert.AreEqual(perk.id, TY_Perks.Get(perk.id).id, "perks are indexed by id");
                levels = levels.WithLevel(perk.id, perk.maxLevel);
                Assert.AreEqual(perk.maxLevel, levels.Level(perk.id));
                Assert.Greater(perk.Cost(1), perk.Cost(0));
            }
        }

        [Test]
        public void ZonesFollowPlotYaw() {
            var zone = new ZoneBox(new Vector3(10, 0.5f, 0), new Vector3(2, 1, 4), 90f);
            Assert.IsTrue(zone.ContainsFootprint(new Vector3(11.8f, 0.5f, 0)));
            Assert.IsFalse(zone.ContainsFootprint(new Vector3(10, 0.5f, 1.5f)));
        }

        [Test]
        public void RoundToolkitHelpers() {
            Assert.AreEqual("12.5K", RoundFormat.Money(12500));
            Assert.AreEqual("2nd", RoundFormat.Ordinal(2));
            var track = new PathTrack(new List<Vector3> { Vector3.zero, new(10, 0, 0), new(10, 0, 10) });
            Assert.AreEqual(20f, track.Length, 1e-4f);
            Assert.That(Vector3.Distance(track.Evaluate(15f), new Vector3(10, 0, 5)), Is.LessThan(1e-4f));
            Assert.AreEqual(15f, track.Project(new Vector3(11, 0, 5)), 1e-3f);
            int[] teams = TeamSplit.Balance(Enumerable.Range(0, 7).ToList(), 2, new System.Random(3));
            Assert.LessOrEqual(Mathf.Abs(teams.Count(t => t == 0) - teams.Count(t => t == 1)), 1);
            Assert.AreEqual(1, TeamSplit.MinorityCount(3, 4));
            Assert.AreEqual(2, TeamSplit.MinorityCount(9, 4));
        }
    }
}
