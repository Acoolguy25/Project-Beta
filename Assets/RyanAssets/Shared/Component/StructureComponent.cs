using System;
using RyanAssets.Core;
using RyanAssets.Shared.Component;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace RyanAssets.Shared.Declarations {
    [Serializable]
    public class StructureComponent : EntityBase, IStructure {
        [Header("Structure Info")]
        public string StructureID;
        public string Description;
        public ulong Cost;
        public float Duration;
        public Sprite Sprite;

        [Header("Entity Info")]
        [SerializeField, FormerlySerializedAs("DisplayName")]
        private string displayName;
        [SerializeField, FormerlySerializedAs("Team")]
        private TeamConfig team;
        public string Category;

        public override string DisplayName {
            get => displayName;
            set => displayName = value;
        }

        public override TeamConfig Team => team;

        // IStructure implementation
        string IStructure.StructureID => StructureID;
        string IStructure.Description => Description;
        ulong IStructure.Cost => Cost;
        float IStructure.Duration => Duration;
        Sprite IStructure.Sprite => Sprite;

        public override string ToString() => DisplayName;

        public override void OnStartNetwork() {
            base.OnStartNetwork();

            string categoryName = GetHierarchyName(Category, "Uncategorized");
            string structureName = GetHierarchyName(DisplayName, StructureID, gameObject.name);
            Transform structureRoot = TransformHelper.MkDirRecursive(
                $"Structures/{categoryName}/",
                gameObject.scene);

            transform.SetParent(structureRoot, true);
            gameObject.name = $"{structureName} ({NetworkObject.ObjectId})";
        }

        private static string GetHierarchyName(params string[] candidates) {
            foreach (string candidate in candidates) {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate.Trim();
            }

            return "Unnamed";
        }
    }
}
