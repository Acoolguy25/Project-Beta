using UnityEditor;
using UnityEngine;

namespace RyanAssets.Editor {
    public static class EditorTools {
        [MenuItem("Assets/Print Assembly Qualified Name")]
        private static void Print() {
            MonoScript script = Selection.activeObject as MonoScript;

            if (script == null) {
                Debug.LogWarning("Select a C# script asset first.");
                return;
            }

            System.Type type = script.GetClass();

            if (type == null) {
                Debug.LogWarning("Could not get class from script.");
                return;
            }

            Debug.Log(type.AssemblyQualifiedName);
        }

    }
}