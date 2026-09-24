using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>
    /// Covers the answer a commander gets back when a build order is turned down.
    /// <para>
    /// Every unit in the mode - a tank, a jet, a foot soldier - is raised the same way: queued at a
    /// building that can make it, paid for up front, and waited on. There is no longer a second
    /// route that skips the queue, so a refusal is the only thing standing between a click and a
    /// unit, and it has to say which reason it was.
    /// </para>
    /// </summary>
    public sealed class WV_ProductionRefusalTests {
        /// <summary>
        /// The refusal byte is on the wire, so the numbering is a contract rather than an
        /// implementation detail: a value may be appended, but none of these may shift.
        /// </summary>
        [Test]
        public void RefusalNumbering_IsStable() {
            Assert.That((byte)WV_TroopRefusal.None, Is.EqualTo(0));
            Assert.That((byte)WV_TroopRefusal.NotEnoughFunds, Is.EqualTo(1));
            Assert.That((byte)WV_TroopRefusal.SquadFull, Is.EqualTo(2));
            Assert.That((byte)WV_TroopRefusal.QueueFull, Is.EqualTo(3));
            Assert.That((byte)WV_TroopRefusal.NotOperational, Is.EqualTo(4));
            Assert.That((byte)WV_TroopRefusal.Unavailable, Is.EqualTo(5));
        }

        /// <summary>Every refusal has to say something, or a refused click reads as a broken button.</summary>
        [Test]
        public void EveryRefusal_HasAMessage() {
            WV_ProductionItem item = WV_ProductionItem.Troop(WV_TroopKind.Gunner);
            foreach (WV_TroopRefusal refusal in System.Enum.GetValues(typeof(WV_TroopRefusal))) {
                if (refusal == WV_TroopRefusal.None)
                    continue;

                string message = WV_Rules.GetProductionRefusalMessage(refusal, item);

                Assert.That(message, Is.Not.Null.And.Not.Empty, $"{refusal} has no message.");
            }
        }

        /// <summary>
        /// A troop costs funds and occupies its building's queue for a real span of time. Those two
        /// numbers are the whole of what makes a barracks a gate rather than a button, so a zero in
        /// either one would quietly restore instant free troops.
        /// </summary>
        [Test]
        public void EveryTroop_CostsFundsAndTakesTime() {
            foreach (WV_TroopKind kind in new[] { WV_TroopKind.Knife, WV_TroopKind.Gunner }) {
                Assert.That(WV_Rules.GetTroopCost(kind), Is.GreaterThan(0), $"{kind} is free.");
                Assert.That(WV_Rules.GetTroopBuildSeconds(kind), Is.GreaterThan(0f), $"{kind} is instant.");
            }
        }
    }
}
