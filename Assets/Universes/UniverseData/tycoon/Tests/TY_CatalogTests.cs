using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RyanAssets.Rounds;
using UnityEngine;

namespace Universes.UniverseData.tycoon.Tests {
    public class TY_CatalogTests {
        [Test]
        public void EveryItemIsReachableAndButtonsNeverOverlap() {
            var random = new System.Random(7);
            for (int run = 0; run < 200; run++) {
                uint purchased = TY_Catalog.StarterMask;
                int guard = 0;
                while ((purchased & TY_Catalog.AllButRebirth) != TY_Catalog.AllButRebirth) {
                    var available = TY_Catalog.AvailableItems(purchased).Where(i => i != TY_Catalog.RebirthIndex).ToList();
                    Assert.IsNotEmpty(available, "build order dead-ends");
                    var spots = available.Select(i => TY_Catalog.Items[i].buttonPosition).ToList();
                    Assert.AreEqual(spots.Count, spots.Distinct().Count(), "two visible buttons share a spot");
                    purchased |= 1u << available[random.Next(available.Count)];
                    Assert.Less(++guard, 100);
                }
                Assert.IsTrue(TY_Catalog.Available(purchased, TY_Catalog.RebirthIndex), "rebirth unlocks once everything is built");
            }
        }

        [Test]
        public void PricesRiseAndIncomeGrows() {
            double income = TY_Catalog.Income(TY_Catalog.StarterMask, 0);
            Assert.Greater(income, 0);
            uint all = TY_Catalog.AllButRebirth;
            Assert.Greater(TY_Catalog.Income(all, 0), income * 50);
            Assert.AreEqual(TY_Catalog.Income(all, 0) * 1.5, TY_Catalog.Income(all, 1), 1e-6);
            Assert.AreEqual(3.0, TY_Catalog.UpgradeMultiplier(all), 1e-6);
        }

        [Test]
        public void FirstRebirthTakesMinutesNotHours() {
            // Greedy player: always buys the cheapest available upgrade as soon as it can afford it.
            uint purchased = TY_Catalog.StarterMask;
            double cash = 0, seconds = 0;
            while (true) {
                int next = TY_Catalog.AvailableItems(purchased).OrderBy(i => TY_Catalog.Items[i].price).First();
                double need = TY_Catalog.Items[next].price - cash;
                if (need > 0) { double dt = need / TY_Catalog.Income(purchased, 0); seconds += dt; cash += need; }
                cash -= TY_Catalog.Items[next].price;
                if (next == TY_Catalog.RebirthIndex) break;
                purchased |= 1u << next;
            }
            Assert.That(seconds, Is.InRange(5 * 60, 20 * 60), $"first rebirth after {seconds / 60:0.0} min");
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
