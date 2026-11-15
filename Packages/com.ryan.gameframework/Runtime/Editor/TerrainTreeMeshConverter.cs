using UnityEditor;
using UnityEngine;

namespace RyanAssets.Editor {
    /// <summary>Moves legacy terrain trees into prefab instances, retaining placement and collisions.</summary>
    public static class TerrainTreeMeshConverter {
        public static int Convert(Terrain terrain, string terrainCopyPath) {
            var original = terrain.terrainData;
            var trees = original.treeInstances;
            if (trees.Length == 0) return 0;
            if (AssetDatabase.LoadMainAssetAtPath(terrainCopyPath) != null)
                throw new System.InvalidOperationException("A terrain copy already exists at " + terrainCopyPath);
            var prototypes = original.treePrototypes;
            var container = new GameObject("Terrain trees");
            Undo.RegisterCreatedObjectUndo(container, "Convert terrain trees");
            container.transform.SetParent(terrain.transform, false);
            for (int i = 0; i < trees.Length; i++) {
                TreeInstance tree = trees[i];
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length || prototypes[tree.prototypeIndex].prefab == null) continue;
                var prefab = prototypes[tree.prototypeIndex].prefab;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container.transform);
                instance.name = prefab.name + "_" + i;
                instance.transform.localPosition = Vector3.Scale(tree.position, original.size);
                instance.transform.localRotation = Quaternion.AngleAxis(tree.rotation * Mathf.Rad2Deg, Vector3.up);
                instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
                GameObjectUtility.SetStaticEditorFlags(instance, StaticEditorFlags.BatchingStatic);
                if (instance.GetComponent<LODGroup>() == null) {
                    var lod = instance.AddComponent<LODGroup>();
                    lod.SetLODs(new[] { new LOD(0.065f, instance.GetComponentsInChildren<Renderer>(true)) });
                    lod.RecalculateBounds();
                }
            }
            // A private TerrainData copy leaves the imported environment reusable.
            var copy = Object.Instantiate(original);
            copy.name = original.name + "_MeshTrees";
            copy.treeInstances = System.Array.Empty<TreeInstance>();
            copy.treePrototypes = System.Array.Empty<TreePrototype>();
            AssetDatabase.CreateAsset(copy, terrainCopyPath);
            Undo.RecordObject(terrain, "Use converted terrain");
            terrain.terrainData = copy;
            var collider = terrain.GetComponent<TerrainCollider>();
            if (collider != null) { Undo.RecordObject(collider, "Use converted terrain collisions"); collider.terrainData = copy; }
            terrain.Flush();
            AssetDatabase.SaveAssets();
            return trees.Length;
        }
    }
}
