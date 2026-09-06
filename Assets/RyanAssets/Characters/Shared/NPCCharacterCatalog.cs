using UnityEngine;

namespace RyanAssets.Characters.Shared {
    public enum NPCCharacter { Robot = 0, Monster = 1 }

    [CreateAssetMenu(menuName = "Ryan/NPC Characters")]
    public sealed class NPCCharacterCatalog : ScriptableObject {
        public GameObject robot;
        public GameObject monster;
        public GameObject GetPrefab(NPCCharacter character) => character switch {
            NPCCharacter.Robot => robot,
            NPCCharacter.Monster => monster,
            _ => throw new System.ArgumentOutOfRangeException(nameof(character))
        };
    }
}
