using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RyanAssets.Editor {
    /// <summary>Preserves reflected geometry by replacing unsupported boxes with convex box meshes.</summary>
    public static class MirroredBoxColliderRepair {
        public static int RepairScene(Scene scene, string meshAssetPath) {
            if (!scene.IsValid() || !scene.isLoaded || !meshAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Provide a loaded scene and an Assets mesh destination.");
            var meshes = new Dictionary<string, Mesh>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(meshAssetPath))
                if (asset is Mesh mesh) meshes[mesh.name] = mesh;
            int repaired = 0;
            foreach (var root in scene.GetRootGameObjects()) {
                foreach (var box in root.GetComponentsInChildren<BoxCollider>(true)) {
                    Vector3 scale = box.transform.lossyScale, size = box.size;
                    if (scale.x >= 0 && scale.y >= 0 && scale.z >= 0 && size.x >= 0 && size.y >= 0 && size.z >= 0) continue;
                    size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                    if (size.x < 0.0001f || size.y < 0.0001f || size.z < 0.0001f)
                        throw new InvalidOperationException("Degenerate box: " + box.name);
                    string key = "Box_" + Hash128.Compute(FormattableString.Invariant($"{box.center.x:R},{box.center.y:R},{box.center.z:R}/{size.x:R},{size.y:R},{size.z:R}"));
                    if (!meshes.TryGetValue(key, out var mesh)) {
                        mesh = CreateBox(key, box.center, size);
                        if (meshes.Count == 0) AssetDatabase.CreateAsset(mesh, meshAssetPath);
                        else AssetDatabase.AddObjectToAsset(mesh, meshAssetPath);
                        meshes.Add(key, mesh);
                    }
                    var replacement = Undo.AddComponent<MeshCollider>(box.gameObject);
                    replacement.convex = true;
                    replacement.sharedMesh = mesh;
                    replacement.sharedMaterial = box.sharedMaterial;
                    replacement.isTrigger = box.isTrigger;
                    replacement.enabled = box.enabled;
                    replacement.contactOffset = box.contactOffset;
                    replacement.includeLayers = box.includeLayers;
                    replacement.excludeLayers = box.excludeLayers;
                    replacement.layerOverridePriority = box.layerOverridePriority;
                    replacement.providesContacts = box.providesContacts;
                    Undo.DestroyObjectImmediate(box);
                    repaired++;
                }
            }
            AssetDatabase.SaveAssets();
            return repaired;
        }
        static Mesh CreateBox(string name, Vector3 center, Vector3 size) {
            var vertices = new Vector3[8];
            for (int i = 0; i < 8; i++) vertices[i] = center + Vector3.Scale(size * 0.5f,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var mesh = new Mesh { name = name, vertices = vertices,
                triangles = new[] { 0,2,1,1,2,3, 4,5,6,5,7,6, 0,1,4,1,5,4, 2,6,3,3,6,7, 0,4,2,2,4,6, 1,3,5,3,7,5 } };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
