using UnityEditor;
using UnityEngine;

namespace RyanAssets.UI.Editor {
    /// <summary>Assigns serialized (often private) fields from authoring scripts, failing loudly on typos.</summary>
    public static class SerializedFields {
        public static void Set(Object target, string field, Object value) =>
            Edit(target, field, p => p.objectReferenceValue = value);

        public static void Set(Object target, string field, bool value) =>
            Edit(target, field, p => p.boolValue = value);

        public static void Set(Object target, string field, float value) =>
            Edit(target, field, p => p.floatValue = value);

        public static void Set(Object target, string field, string value) =>
            Edit(target, field, p => p.stringValue = value);

        public static void SetEnum(Object target, string field, int value) =>
            Edit(target, field, p => p.enumValueIndex = value);

        static void Edit(Object target, string field, System.Action<SerializedProperty> assign) {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new System.ArgumentException($"{target.GetType().Name} has no serialized field '{field}'.");
            assign(property);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
