using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>Covers what dying and selling cost a commander, and the per-player limits and alliances.</summary>
    public sealed class WV_PenaltyTests {
        [Test]
        public void DeathPenalty_IsAShareOfALargeBalance() {
            Assert.That(WV_Rules.GetDeathPenalty(2000), Is.EqualTo(300));
        }

        [Test]
        public void DeathPenalty_HasAFloor() {
            Assert.That(WV_Rules.GetDeathPenalty(200), Is.EqualTo(WV_Rules.DeathPenaltyMinimum));
        }

        [Test]
        public void DeathPenalty_NeverExceedsTheBalance() {
            Assert.That(WV_Rules.GetDeathPenalty(20), Is.EqualTo(20));
            Assert.That(WV_Rules.GetDeathPenalty(0), Is.EqualTo(0));
            Assert.That(WV_Rules.GetDeathPenalty(-5), Is.EqualTo(0));
        }

        [Test]
        public void Selling_AtFullHealthRefundsAFixedShareOfTheCost() {
            Assert.That(WV_Rules.GetSellRefund(1000L, 1f), Is.EqualTo(500));
            Assert.That(WV_Rules.GetSellRefund(1000UL, 1f), Is.EqualTo(500));
        }

        [Test]
        public void Selling_RefundFallsLinearlyWithDamageToAFloor() {
            Assert.That(WV_Rules.GetSellRefund(1000L, 0.5f), Is.EqualTo(375));
            Assert.That(WV_Rules.GetSellRefund(1000L, 0.1f), Is.EqualTo(275));
            // A building on its last hit point still works, so it keeps half its sale value.
            Assert.That(WV_Rules.GetSellRefund(1000L, 0f), Is.EqualTo(250));
        }

        [Test]
        public void Selling_ClampsOutOfRangeConditionAndIgnoresFreeItems() {
            Assert.That(WV_Rules.GetSellRefund(1000L, 2f), Is.EqualTo(500));
            Assert.That(WV_Rules.GetSellRefund(1000L, -1f), Is.EqualTo(250));
            Assert.That(WV_Rules.GetSellRefund(0L, 1f), Is.EqualTo(0));
        }

        [Test]
        public void Limits_MatchThePerPlayerCaps() {
            Assert.That(WV_Limits.GetLimit(WV_ForceCategory.Soldier), Is.EqualTo(20));
            Assert.That(WV_Limits.GetLimit(WV_ForceCategory.Aircraft), Is.EqualTo(10));
            Assert.That(WV_Limits.GetLimit(WV_ForceCategory.Building), Is.EqualTo(50));
        }

        [Test]
        public void Limits_CountTroopsAndGroundVehiclesAsSoldiersAndAircraftApart() {
            Assert.That(WV_Limits.GetCategory(WV_ProductionItem.Troop(WV_TroopKind.Gunner)), Is.EqualTo(WV_ForceCategory.Soldier));
            Assert.That(WV_Limits.GetCategory(WV_ProductionItem.Unit(WV_UnitKind.Tank)), Is.EqualTo(WV_ForceCategory.Soldier));
            Assert.That(WV_Limits.GetCategory(WV_ProductionItem.Unit(WV_UnitKind.Chopper)), Is.EqualTo(WV_ForceCategory.Aircraft));
            Assert.That(WV_Limits.GetCategory(WV_ProductionItem.Unit(WV_UnitKind.Jet)), Is.EqualTo(WV_ForceCategory.Aircraft));
        }

        [Test]
        public void Alliances_SurvivalPutsCommandersAgainstTheWaves() {
            Assert.That(WV_Alliances.Mode, Is.EqualTo(WV_GameMode.Survival));
            Assert.That(WV_Alliances.GetCommanderSide(0), Is.EqualTo(WV_Alliances.GetCommanderSide(7)));
            var enemies = WV_Alliances.BuildEnemyTable();
            Assert.That(enemies[WV_Alliances.Invaders], Does.Contain(WV_Alliances.Defenders));
            Assert.That(enemies[WV_Alliances.Defenders], Does.Contain(WV_Alliances.Invaders));
            Assert.That(enemies[WV_Alliances.Defenders], Does.Not.Contain(WV_Alliances.Defenders));
        }
    }
}
