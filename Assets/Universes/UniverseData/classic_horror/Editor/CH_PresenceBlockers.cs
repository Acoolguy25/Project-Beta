using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Editor {
    /// <summary>
    /// Keeps the Presence off fences, gates, flood walls and invisible map blockers. Its NavMesh agent is small
    /// (radius 0.1 m) and steps up to 1 m, so without this it walked along and over obstacles an investigator
    /// cannot cross and caught players from the far side of a fence. Each obstacle collider gets an oriented
    /// NotWalkable volume, padded for an investigator's width and never thinner than the surface's 1.5 m jump links; then the
    /// Investigation surface is rebaked. Re-runnable: old volumes are removed first.
    /// </summary>
    public static class CH_PresenceBlockers {
        const string RootName = "Presence Blockers";
        /// <summary>
        /// Names of the obstacle colliders investigators cannot pass whose box is a tight fit: straight fence runs and
        /// posts, gate leaves, the straight flood wall and the invisible blockers. Gate frames are left out because
        /// their box would close the opening people walk through, and the long curved flood wall at the map edge
        /// because its box would swallow the walkable bank beside it.
        /// </summary>
        static readonly Regex Blocking = new(@"^(Struct_Fence\d_(Mid|Column)|Struct_Fence1_Gate_A_Door|Struct_FloodWall_A|_BLOCKERCUBE)", RegexOptions.CultureInvariant);
        /// <summary>The LOD1 and LOD2 copies share the LOD0 collider's place, so one volume each is enough.</summary>
        static readonly Regex LowerDetail = new(@"_LOD[1-9]$", RegexOptions.CultureInvariant);
        /// <summary>Extra width on each side, in metres: investigator radius 0.3 less the Presence's own 0.1.</summary>
        public const float SidePadding = 0.2f;
        /// <summary>
        /// Least width of a volume across the obstacle, in metres. The surface generates jump links across gaps up
        /// to the agent's 1.5 m jump distance, so the hole a thin fence leaves has to be wider than that or the
        /// Presence jumps the fence anyway.
        /// </summary>
        public const float MinimumWidth = 2f;
        /// <summary>Extra height below and above each obstacle, so ground-level NavMesh under it is removed.</summary>
        public const float VerticalPadding = 0.6f;
        const int NotWalkable = 1;

        [MenuItem("Ryan/Classic Horror/Block Presence At Fences And Flood Walls")]
        public static void Build() {
            var surface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (surface == null) throw new System.InvalidOperationException("Open classic_horror_start first: no NavMeshSurface in the scene.");
            var scene = surface.gameObject.scene;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == RootName) Object.DestroyImmediate(root);
            var blockers = new GameObject(RootName).transform;
            SceneManager_Move(blockers.gameObject, scene);

            int count = 0;
            var seen = new HashSet<Collider>();
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude)) {
                if (collider.isTrigger || !seen.Add(collider) || !Blocking.IsMatch(collider.name) || LowerDetail.IsMatch(collider.name)) continue;
                if (!LocalBounds(collider, out Bounds bounds)) continue;
                var volumeObject = new GameObject(collider.name);
                volumeObject.transform.SetParent(blockers, false);
                var source = collider.transform;
                volumeObject.transform.SetPositionAndRotation(source.position, source.rotation);
                // The sign is kept so a mirrored obstacle's box lands on it rather than on its mirror image.
                volumeObject.transform.localScale = source.lossyScale;
                var volume = volumeObject.AddComponent<NavMeshModifierVolume>();
                Vector3 s = Abs(source.lossyScale);
                volume.center = bounds.center;
                Vector3 world = Vector3.Scale(bounds.size, s) + new Vector3(2f * SidePadding, 2f * VerticalPadding, 2f * SidePadding);
                world.x = Mathf.Max(world.x, MinimumWidth);
                world.z = Mathf.Max(world.z, MinimumWidth);
                volume.size = new Vector3(world.x / Mathf.Max(s.x, 1e-3f), world.y / Mathf.Max(s.y, 1e-3f), world.z / Mathf.Max(s.z, 1e-3f));
                volume.area = NotWalkable;
                count++;
            }
            surface.BuildNavMesh();
            SaveNavMesh(surface);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Classic Horror] {count} Presence blocker volumes; Investigation NavMesh rebaked");
        }

        static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static void SceneManager_Move(GameObject obj, UnityEngine.SceneManagement.Scene scene) {
            if (obj.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
        }

        static bool LocalBounds(Collider collider, out Bounds bounds) {
            switch (collider) {
                case BoxCollider box: bounds = new Bounds(box.center, box.size); return true;
                case MeshCollider mesh when mesh.sharedMesh != null: bounds = mesh.sharedMesh.bounds; return true;
                case CapsuleCollider capsule:
                    Vector3 size = Vector3.one * capsule.radius * 2f;
                    size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                    bounds = new Bounds(capsule.center, size); return true;
                case SphereCollider sphere: bounds = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f); return true;
            }
            bounds = default;
            return false;
        }

        /// <summary>Writes the new NavMesh over the surface's existing asset, at the same path.</summary>
        static void SaveNavMesh(NavMeshSurface surface) {
            string folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(surface.gameObject.scene.path), System.IO.Path.GetFileNameWithoutExtension(surface.gameObject.scene.path));
            string path = (folder + "/NavMesh-" + surface.name + ".asset").Replace('\\', '/');
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            AssetDatabase.SaveAssets();
        }
    }
}
