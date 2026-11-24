using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.obby.Editor {
    /// <summary>
    /// Saves a freshly built hierarchy as a prefab without churning its file: when the prefab already
    /// exists with the same shape (names, components and children all the way down), the new values are
    /// copied onto the existing objects, so every object keeps its fileID and a rebuild with no real
    /// change leaves the file byte-identical. References inside the hierarchy are pointed at the existing
    /// objects. A changed shape falls back to a plain save (new fileIDs, once).
    /// </summary>
    public static class OB_StablePrefab {
        /// <summary>Properties that tie an object to its place or its prefab, never copied.</summary>
        static readonly HashSet<string> Skipped = new() {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "m_Script", "m_EditorHideFlags", "m_EditorClassIdentifier", "m_Father", "m_Children", "m_RootOrder", "m_Component"
        };

        /// <summary>Saves <paramref name="go"/> to <paramref name="path"/>, destroys it and returns the prefab asset.</summary>
        public static GameObject Save(GameObject go, string path) {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) {
                var contents = PrefabUtility.LoadPrefabContents(path);
                try {
                    if (SameShape(go.transform, contents.transform)) {
                        var map = new Dictionary<Object, Object>();
                        Map(go.transform, contents.transform, map);
                        Copy(go.transform, contents.transform, map);
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        Object.DestroyImmediate(go);
                        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    }
                } finally {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            if (prefab == null) throw new System.InvalidOperationException("Could not save " + path);
            return prefab;
        }

        /// <summary>Same names, components, children and nested prefabs, all the way down.</summary>
        static bool SameShape(Transform a, Transform b) {
            if (a.name != b.name || a.childCount != b.childCount) return false;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(a.gameObject) != PrefabUtility.IsAnyPrefabInstanceRoot(b.gameObject)) return false;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(a.gameObject)
                && PrefabUtility.GetCorrespondingObjectFromSource(a.gameObject) != PrefabUtility.GetCorrespondingObjectFromSource(b.gameObject)) return false;
            var ca = a.GetComponents<Component>();
            var cb = b.GetComponents<Component>();
            if (ca.Length != cb.Length) return false;
            for (int i = 0; i < ca.Length; i++)
                if (ca[i] == null || cb[i] == null || ca[i].GetType() != cb[i].GetType()) return false;
            for (int i = 0; i < a.childCount; i++)
                if (!SameShape(a.GetChild(i), b.GetChild(i))) return false;
            return true;
        }

        static void Map(Transform from, Transform to, Dictionary<Object, Object> map) {
            map[from.gameObject] = to.gameObject;
            var source = from.GetComponents<Component>();
            var target = to.GetComponents<Component>();
            for (int i = 0; i < source.Length; i++) map[source[i]] = target[i];
            for (int i = 0; i < from.childCount; i++) Map(from.GetChild(i), to.GetChild(i), map);
        }

        static void Copy(Transform from, Transform to, Dictionary<Object, Object> map) {
            GameObject a = from.gameObject, b = to.gameObject;
            if (b.layer != a.layer) b.layer = a.layer;
            if (b.tag != a.tag) b.tag = a.tag;
            if (b.activeSelf != a.activeSelf) b.SetActive(a.activeSelf);
            var flags = GameObjectUtility.GetStaticEditorFlags(a);
            if (GameObjectUtility.GetStaticEditorFlags(b) != flags) GameObjectUtility.SetStaticEditorFlags(b, flags);
            var source = from.GetComponents<Component>();
            var target = to.GetComponents<Component>();
            for (int i = 0; i < source.Length; i++) CopyComponent(source[i], target[i], map);
            for (int i = 0; i < from.childCount; i++) Copy(from.GetChild(i), to.GetChild(i), map);
        }

        /// <summary>Copies every serialized value but the bookkeeping ones, then points in-hierarchy references at the target's objects.</summary>
        static void CopyComponent(Component from, Component to, Dictionary<Object, Object> map) {
            var source = new SerializedObject(from);
            var target = new SerializedObject(to);
            SerializedProperty property = source.GetIterator();
            if (property.Next(true)) {
                do {
                    if (Skipped.Contains(property.name)) continue;
                    target.CopyFromSerializedProperty(property);
                } while (property.Next(false));
            }
            SerializedProperty reference = target.GetIterator();
            while (reference.Next(true)) {
                if (reference.propertyType != SerializedPropertyType.ObjectReference || Skipped.Contains(reference.name)) continue;
                Object value = reference.objectReferenceValue;
                if (value != null && map.TryGetValue(value, out Object mapped)) reference.objectReferenceValue = mapped;
            }
            target.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
