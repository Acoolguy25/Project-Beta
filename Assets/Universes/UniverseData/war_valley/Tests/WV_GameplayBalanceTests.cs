using NUnit.Framework;
using RyanAssets.Shared.Declarations;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Tests {
    public sealed class WV_GameplayBalanceTests {
        const string Root = "Assets/Universes/UniverseData/war_valley/";
        static GameObject Structure(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Structures/" + name + ".prefab");

        [Test]
        public void StarterBaseAndSquad_AreAffordableAndReadyBeforeWaveOne() {
            StructureComponent mine = Structure("Mineshaft").GetComponent<StructureComponent>();
            StructureComponent barracks = Structure("Barracks").GetComponent<StructureComponent>();
            StructureComponent tower = Structure("Watchtower").GetComponent<StructureComponent>();
            long cost = (long)(mine.Cost + barracks.Cost + tower.Cost) + 4 * WV_Rules.GetTroopCost(WV_TroopKind.Knife);
            Assert.That(cost, Is.LessThanOrEqualTo(WV_Rules.StartingFunds));
            float ready = barracks.Duration + 4 * WV_Rules.GetTroopBuildSeconds(WV_TroopKind.Knife);
            Assert.That(ready, Is.LessThan(new WV_WaveTuning().PreparationSeconds));
        }

        [TestCase("Watchtower", 6f)]
        [TestCase("Airfield", 8f)]
        [TestCase("ShieldGenerator", 6f)]
        [TestCase("GuardPost", 4f)]
        public void Buildings_HaveCompactHeightsAndCollisionFollowingTheirArt(string name, float heightLimit) {
            GameObject prefab = Structure(name);
            BoxCollider footprint = prefab.GetComponent<BoxCollider>();
            Assert.That(footprint.isTrigger, Is.True, "The placement volume must not block movement.");
            Assert.That(footprint.size.y, Is.LessThanOrEqualTo(heightLimit + 0.01f));
            MeshCollider[] colliders = prefab.GetComponentsInChildren<MeshCollider>(true);
            Assert.That(colliders.Length, Is.GreaterThan(0), "Visible art still needs physical collision.");
            foreach (MeshCollider collider in colliders)
                Assert.That(collider.sharedMesh, Is.SameAs(collider.GetComponent<MeshFilter>().sharedMesh));
            NavMeshObstacle obstacle = prefab.GetComponent<NavMeshObstacle>();
            Assert.That(obstacle.size, Is.EqualTo(footprint.size));
            Assert.That(obstacle.center, Is.EqualTo(footprint.center));
        }

        [Test]
        public void Gunners_CannotOutrangeAnyRangedVehicleOrGroundDefense() {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root + "Units", Root + "Structures" })) {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                WV_Unit unit = prefab.GetComponent<WV_Unit>();
                WV_DefenseTurret turret = prefab.GetComponent<WV_DefenseTurret>();
                if (unit != null)
                    Assert.That(unit.AttackRange, Is.GreaterThan(WV_Rules.GunnerEngageRange), prefab.name);
                if (turret != null && !turret.IsAntiAir)
                    Assert.That(turret.Range, Is.GreaterThan(WV_Rules.GunnerEngageRange), prefab.name);
                if (unit != null || turret != null)
                    Assert.That(prefab.GetComponentInChildren<WV_RangeIndicator>(true), Is.Not.Null, prefab.name);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void SmallEnemyCaps_StillBoundWavesWithEveryTroopKindUnlocked(int cap) {
            var tuning = new WV_WaveTuning { MaxEnemiesPerWave = cap };
            Assert.That(WV_WavePlan.Build(80, tuning).TotalCount, Is.LessThanOrEqualTo(cap));
        }

        [Test]
        public void Aircraft_RemainWithinTheLowerFlightBand() {
            foreach (WV_UnitKind kind in new[] { WV_UnitKind.Chopper, WV_UnitKind.Jet, WV_UnitKind.Bomber, WV_UnitKind.UAV })
                Assert.That(WV_Rules.GetCruiseAltitude(kind), Is.InRange(5f, 10f), kind.ToString());
        }

        [Test]
        public void Freeze_AppendsToTheProtocolWithoutChangingExistingOrders() {
            Assert.That((byte)WV_OrderType.Stop, Is.EqualTo(0));
            Assert.That((byte)WV_OrderType.HoldPosition, Is.EqualTo(4));
            Assert.That((byte)WV_OrderType.Freeze, Is.EqualTo(5));
        }
    }
}
