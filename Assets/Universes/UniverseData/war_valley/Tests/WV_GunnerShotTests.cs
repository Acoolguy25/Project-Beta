using NUnit.Framework;
using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Combat;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Shared;
using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Tests {
    public sealed class WV_GunnerShotTests {
        GameObject root;
        GameCharacter shooter;
        GameCharacter ally;
        GameCharacter enemy;
        ToolGunShared gun;
        Vector3 origin;

        [SetUp]
        public void SetUp() {
            root = new GameObject("Gunner shot test");
            origin = new Vector3(10000f, 1001f, 10000f);
            shooter = Character("Shooter", origin - Vector3.up, TeamColor.Blue, TeamColor.Blue);
            ally = Character("Ally", origin + Vector3.forward * 4f - Vector3.up, TeamColor.Blue, TeamColor.Green);
            enemy = Character("Enemy", origin + Vector3.forward * 12f - Vector3.up, TeamColor.Red, TeamColor.Blue);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RyanAssets/Tools/Implemented/FN_Five_Seven.prefab");
            gun = Object.Instantiate(prefab, root.transform).GetComponent<ToolGunShared>();
            gun.connectedCharacter = shooter;
            gun.weaponRoot.transform.position = origin;
            gun.BestAccuracy = gun.WorstAccuracy = 360;
            gun.passThroughAlliedTroopsSync.Value = true;
            Physics.SyncTransforms();
        }

        GameCharacter Character(string name, Vector3 position, TeamColor team, TeamColor display) {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RyanAssets/Characters/RobotNPC.prefab");
            GameObject instance = Object.Instantiate(prefab, root.transform);
            instance.name = name;
            instance.transform.position = position;
            // Keep one predictable body volume while retaining the authored entity and team data.
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            BoxCollider body = instance.AddComponent<BoxCollider>();
            body.center = Vector3.up;
            body.size = new Vector3(1f, 2f, 1f);
            GameCharacter character = instance.GetComponent<GameCharacter>();
            character.TeamSync.Value = new TeamConfig(team, display);
            return character;
        }

        Vector3 Aim => enemy.transform.position + Vector3.up;

        GameObject Wall(Vector3 position) {
            GameObject wall = new GameObject("Wall");
            wall.transform.SetParent(root.transform);
            wall.transform.position = position;
            wall.AddComponent<BoxCollider>().size = new Vector3(4f, 4f, 0.5f);
            Physics.SyncTransforms();
            return wall;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void AlliedTroop_DoesNotBlockSightOrBullet_DespiteDifferentDisplayColour() {
            Assert.That(gun.HasClearShot(Aim, enemy.transform), Is.True);
            Assert.That(gun.Shoot(Aim).Value.transform.IsChildOf(enemy.transform), Is.True);
        }

        [Test]
        public void AlliedTroop_OverlappingMuzzle_DoesNotBlockShot() {
            ally.transform.position = origin - Vector3.up;
            Physics.SyncTransforms();
            Assert.That(gun.HasClearShot(Aim, enemy.transform), Is.True);
            Assert.That(gun.Shoot(Aim).Value.transform.IsChildOf(enemy.transform), Is.True);
        }

        [Test]
        public void HostileTroop_BlocksShot_EvenWithSameDisplayColour() {
            ally.TeamSync.Value = new TeamConfig(TeamColor.Red, TeamColor.Blue);
            Assert.That(gun.HasClearShot(Aim, enemy.transform), Is.False);
            Assert.That(gun.Shoot(Aim).Value.transform.IsChildOf(ally.transform), Is.True);
        }

        [Test]
        public void OrdinaryWall_BlocksSightAndBullet_BehindAlliedTroop() {
            GameObject wall = Wall(origin + Vector3.forward * 6f);
            Assert.That(gun.HasClearShot(Aim, enemy.transform), Is.False);
            Assert.That(gun.Shoot(Aim).Value.transform, Is.EqualTo(wall.transform));
        }

        [Test]
        public void MuzzleInsideWall_CannotShootPastIt() {
            Wall(origin);
            Assert.That(gun.HasClearShot(Aim, enemy.transform), Is.False);
            Assert.That(gun.Shoot(Aim).Value.distance, Is.Zero);
        }

        [Test]
        public void SharedGun_DefaultPolicy_StillStopsOnAlliedTroops() {
            gun.passThroughAlliedTroopsSync.Value = false;
            Assert.That(gun.HasClearShot(Aim, enemy.transform), Is.False);
            Assert.That(gun.Shoot(Aim).Value.transform.IsChildOf(ally.transform), Is.True);
        }

        [Test]
        public void DenseShotLine_StillFindsWall_AfterBufferExpansion() {
            for (int i = 0; i < 80; i++) {
                GameObject child = new GameObject("Allied collider");
                child.transform.SetParent(ally.transform);
                child.transform.position = origin + Vector3.forward * (1f + i * 0.05f);
                child.AddComponent<BoxCollider>().size = Vector3.one * 0.1f;
            }
            GameObject wall = Wall(origin + Vector3.forward * 8f);
            Assert.That(gun.HasClearShot(Aim, enemy.transform), Is.False);
            Assert.That(gun.Shoot(Aim).Value.transform, Is.EqualTo(wall.transform));
        }
    }
}
