using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
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

        /// <summary>
        /// The team a game mode assigned this placed structure, or null to keep the authored one.
        /// <para>
        /// The authored team is shared by every copy of the prefab, so it cannot say who built a
        /// particular building. A mode that gives structures to players - one commander's barracks
        /// in their colour, another's in theirs - replicates the placer's team here instead, and
        /// every reader of <see cref="Team"/> (overhead tags, damage rules, turrets) follows it.
        /// </para>
        /// </summary>
        private readonly SyncVar<TeamConfig> assignedTeam = new();

        public override string DisplayName {
            get => displayName;
            set => displayName = value;
        }

        public override TeamConfig Team => assignedTeam.Value ?? team;

        // IStructure implementation
        string IStructure.StructureID => StructureID;
        string IStructure.Description => Description;
        ulong IStructure.Cost => Cost;
        float IStructure.Duration => Duration;
        Sprite IStructure.Sprite => Sprite;

        public override string ToString() => DisplayName;

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            assignedTeam.OnChange += HandleAssignedTeamChanged;

            string categoryName = GetHierarchyName(Category, "Uncategorized");
            string structureName = GetHierarchyName(DisplayName, StructureID, gameObject.name);
            Transform structureRoot = TransformHelper.MkDirRecursive(
                $"Structures/{categoryName}/",
                gameObject.scene);

            transform.SetParent(structureRoot, true);
            gameObject.name = $"{structureName} ({NetworkObject.ObjectId})";
        }

        public override void OnStopNetwork() {
            assignedTeam.OnChange -= HandleAssignedTeamChanged;
            base.OnStopNetwork();
        }

        private void HandleAssignedTeamChanged(TeamConfig previous, TeamConfig next, bool asServer) =>
            RaiseTeamChanged();

#if UNITY_SERVER
        /// <summary>Gives this placed structure its own team, replacing the prefab's authored one.</summary>
        [Server]
        public void SetTeam(TeamConfig teamConfig) {
            assignedTeam.Value = teamConfig;
        }
#endif

        private static string GetHierarchyName(params string[] candidates) {
            foreach (string candidate in candidates) {
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate.Trim();
            }

            return "Unnamed";
        }
    }
}
