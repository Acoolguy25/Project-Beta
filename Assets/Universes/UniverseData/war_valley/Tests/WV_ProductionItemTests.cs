using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>
    /// Covers the one byte a barracks queue, a build request, and a refusal message all read.
    /// <para>
    /// Vehicles and foot soldiers share that byte, and their two enums overlap numerically - Infantry
    /// and Gunner are both 1 - so a mistake here does not fail loudly. It builds the wrong thing,
    /// charges the wrong price, or cancels someone else's queue entry, which is exactly the class of
    /// bug a test is cheaper than.
    /// </para>
    /// </summary>
    public sealed class WV_ProductionItemTests {
        [Test]
        public void Unit_RoundTripsThroughItsEncodedByte() {
            foreach (WV_UnitKind kind in System.Enum.GetValues(typeof(WV_UnitKind))) {
                WV_ProductionItem decoded = WV_ProductionItem.Decode(WV_ProductionItem.Unit(kind).Encoded);

                Assert.That(decoded.IsTroop, Is.False, $"{kind} decoded as a troop.");
                Assert.That(decoded.UnitKind, Is.EqualTo(kind));
            }
        }

        [Test]
        public void Troop_RoundTripsThroughItsEncodedByte() {
            foreach (WV_TroopKind kind in System.Enum.GetValues(typeof(WV_TroopKind))) {
                WV_ProductionItem decoded = WV_ProductionItem.Decode(WV_ProductionItem.Troop(kind).Encoded);

                Assert.That(decoded.IsTroop, Is.True, $"{kind} decoded as a vehicle.");
                Assert.That(decoded.TroopKind, Is.EqualTo(kind));
            }
        }

        /// <summary>
        /// The whole reason the flag exists: these two are both 1 in their own enum, and a queue that
        /// confused them would train a rifleman when the player paid for an infantry squad.
        /// </summary>
        [Test]
        public void GunnerAndInfantry_DoNotShareAnEncoding() {
            byte gunner = WV_ProductionItem.Troop(WV_TroopKind.Gunner).Encoded;
            byte infantry = WV_ProductionItem.Unit(WV_UnitKind.Infantry).Encoded;

            Assert.That(gunner, Is.Not.EqualTo(infantry));
        }

        [Test]
        public void EveryUnitKind_StaysClearOfTheTroopFlag() {
            foreach (WV_UnitKind kind in System.Enum.GetValues(typeof(WV_UnitKind))) {
                Assert.That(
                    (byte)kind & WV_ProductionItem.TroopFlag, Is.Zero,
                    $"{kind} collides with the troop flag, so it would decode as a troop.");
            }
        }

        /// <summary>A modified client naming a troop kind that does not exist must build nothing.</summary>
        [Test]
        public void UnknownTroopKind_DecodesToNothingBuildable() {
            var unknown = (byte)(WV_ProductionItem.TroopFlag | 42);

            WV_ProductionItem decoded = WV_ProductionItem.Decode(unknown);

            Assert.That(decoded.IsNone, Is.True);
        }

        [Test]
        public void NoneItem_IsRecognisedAsNothingBuildable() {
            Assert.That(WV_ProductionItem.Unit(WV_UnitKind.None).IsNone, Is.True);
            Assert.That(WV_ProductionItem.Unit(WV_UnitKind.Tank).IsNone, Is.False);
            // Knife is 0 in its own enum, so the flag is the only thing distinguishing it from None.
            Assert.That(WV_ProductionItem.Troop(WV_TroopKind.Knife).IsNone, Is.False);
        }

        [Test]
        public void DisplayName_NamesTheRightSideOfTheEncoding() {
            Assert.That(
                WV_ProductionItem.Troop(WV_TroopKind.Gunner).DisplayName,
                Is.EqualTo(WV_Rules.GetTroopDisplayName(WV_TroopKind.Gunner)));
            Assert.That(
                WV_ProductionItem.Unit(WV_UnitKind.Tank).DisplayName,
                Is.EqualTo(WV_Rules.GetUnitDisplayName(WV_UnitKind.Tank)));
        }

        /// <summary>
        /// Cancelling matches a queue entry by its encoded byte, so equality has to mean "the same
        /// thing to build" rather than reference identity.
        /// </summary>
        [Test]
        public void Equality_MatchesOnWhatWouldBeBuilt() {
            Assert.That(
                WV_ProductionItem.Troop(WV_TroopKind.Knife),
                Is.EqualTo(WV_ProductionItem.Troop(WV_TroopKind.Knife)));
            Assert.That(
                WV_ProductionItem.Troop(WV_TroopKind.Knife),
                Is.Not.EqualTo(WV_ProductionItem.Troop(WV_TroopKind.Gunner)));
        }

        [Test]
        public void EveryTroopKind_HasACostAndABuildTime() {
            foreach (WV_TroopKind kind in System.Enum.GetValues(typeof(WV_TroopKind))) {
                Assert.That(WV_Rules.GetTroopCost(kind), Is.GreaterThan(0), $"{kind} is free.");
                Assert.That(
                    WV_Rules.GetTroopBuildSeconds(kind), Is.GreaterThan(0f),
                    $"{kind} trains instantly, which the queue cannot represent.");
            }
        }
    }
}
