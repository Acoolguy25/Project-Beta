using NUnit.Framework;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    /// <summary>
    /// Covers the research rules the server runs and the client's menus preview: what is locked,
    /// and how fast research goes as stations and projects are added.
    /// </summary>
    public sealed class WV_ResearchTests {
        const float Tolerance = 0.0001f;

        [Test]
        public void TechNumbering_IsStable() {
            // The tech byte travels in research requests and keys the replicated ledger.
            Assert.That((byte)WV_Tech.None, Is.EqualTo(0));
            Assert.That((byte)WV_Tech.RotaryAviation, Is.EqualTo(1));
        }

        [Test]
        public void ResearchRefusalNumbering_IsStable() {
            Assert.That((byte)WV_ResearchRefusal.None, Is.EqualTo(0));
            Assert.That((byte)WV_ResearchRefusal.NotEnoughFunds, Is.EqualTo(1));
            Assert.That((byte)WV_ResearchRefusal.AlreadyResearched, Is.EqualTo(2));
            Assert.That((byte)WV_ResearchRefusal.AlreadyResearching, Is.EqualTo(3));
            Assert.That((byte)WV_ResearchRefusal.NoResearchStation, Is.EqualTo(4));
            Assert.That((byte)WV_ResearchRefusal.MissingPrerequisite, Is.EqualTo(5));
            Assert.That((byte)WV_ResearchRefusal.Unavailable, Is.EqualTo(6));
        }

        [Test]
        public void EveryResearchRefusal_HasAMessage() {
            foreach (WV_ResearchRefusal refusal in System.Enum.GetValues(typeof(WV_ResearchRefusal))) {
                if (refusal == WV_ResearchRefusal.None)
                    continue;
                string message = WV_Rules.GetResearchRefusalMessage(refusal, WV_Tech.RotaryAviation);
                Assert.That(message, Is.Not.Null.And.Not.Empty, $"{refusal} has no message.");
            }
        }

        [Test]
        public void HelipadAndChopper_WaitOnRotaryAviation() {
            Assert.That(WV_TechTree.GetRequirement("wv_helipad"), Is.EqualTo(WV_Tech.RotaryAviation));
            Assert.That(WV_TechTree.GetRequirement(WV_ProductionItem.Unit(WV_UnitKind.Chopper)),
                Is.EqualTo(WV_Tech.RotaryAviation));
        }

        [Test]
        public void EverythingElse_IsUnlockedFromTheStart() {
            Assert.That(WV_TechTree.GetRequirement("wv_barracks"), Is.EqualTo(WV_Tech.None));
            Assert.That(WV_TechTree.GetRequirement("wv_airfield"), Is.EqualTo(WV_Tech.None));
            Assert.That(WV_TechTree.GetRequirement("wv_radar"), Is.EqualTo(WV_Tech.None),
                "The research station itself must never be locked behind research.");
            Assert.That(WV_TechTree.GetRequirement(WV_ProductionItem.Unit(WV_UnitKind.Jet)), Is.EqualTo(WV_Tech.None));
            Assert.That(WV_TechTree.GetRequirement(WV_ProductionItem.Troop(WV_TroopKind.Gunner)), Is.EqualTo(WV_Tech.None));
            Assert.That(WV_TechTree.GetRequirement(WV_ProductionItem.Troop(WV_TroopKind.Knife)), Is.EqualTo(WV_Tech.None));
        }

        [Test]
        public void EveryTech_CostsFundsAndTakesTime() {
            foreach (WV_TechDefinition definition in WV_TechTree.All) {
                Assert.That(definition.Cost, Is.GreaterThan(0), $"{definition.DisplayName} is free.");
                Assert.That(definition.ResearchSeconds, Is.GreaterThan(0f), $"{definition.DisplayName} is instant.");
                Assert.That(WV_TechTree.Get(definition.Tech), Is.SameAs(definition));
            }
        }

        [Test]
        public void OneStation_FinishesAProjectInItsAuthoredTime() {
            WV_TechDefinition definition = WV_TechTree.Get(WV_Tech.RotaryAviation);

            float perSecond = WV_TechTree.GetProgressPerSecond(definition, stationRate: 1f, activeProjects: 1);

            Assert.That(1f / perSecond, Is.EqualTo(definition.ResearchSeconds).Within(Tolerance));
        }

        [Test]
        public void ExtraStations_SpeedResearchUpLinearly() {
            WV_TechDefinition definition = WV_TechTree.Get(WV_Tech.RotaryAviation);

            float one = WV_TechTree.GetProgressPerSecond(definition, 1f, 1);
            float two = WV_TechTree.GetProgressPerSecond(definition, 2f, 1);
            float three = WV_TechTree.GetProgressPerSecond(definition, 3f, 1);

            Assert.That(two, Is.EqualTo(one * 2f).Within(Tolerance));
            Assert.That(three, Is.EqualTo(one * 3f).Within(Tolerance));
        }

        [Test]
        public void ParallelProjects_ShareTheStationsEvenly() {
            // Two projects at once each run at half speed, so researching both together finishes no
            // sooner than researching them back to back.
            Assert.That(WV_TechTree.GetProjectRate(2f, 2), Is.EqualTo(1f).Within(Tolerance));
            Assert.That(WV_TechTree.GetProjectRate(1f, 4), Is.EqualTo(0.25f).Within(Tolerance));
            Assert.That(WV_TechTree.GetProjectRate(3f, 1), Is.EqualTo(3f).Within(Tolerance));
        }

        [Test]
        public void NoStationsOrNoProjects_MeansNoProgress() {
            WV_TechDefinition definition = WV_TechTree.Get(WV_Tech.RotaryAviation);
            Assert.That(WV_TechTree.GetProgressPerSecond(definition, 0f, 1), Is.EqualTo(0f));
            Assert.That(WV_TechTree.GetProjectRate(2f, 0), Is.EqualTo(0f));
            Assert.That(WV_TechTree.GetProgressPerSecond(null, 2f, 1), Is.EqualTo(0f));
        }
    }
}
