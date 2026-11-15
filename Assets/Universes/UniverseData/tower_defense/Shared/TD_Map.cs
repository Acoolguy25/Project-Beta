using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>Lives in tower_defense_start: prefabs clients draw towers and enemies from, plus the player spawn.</summary>
    public sealed class TD_Map : MonoBehaviour {
        public Transform playerSpawn;
        [Min(0f)] public float spawnRadius = 6f;
        [Header("Prefabs (client presentation)")]
        [Tooltip("Index-aligned with TD_TowerKind.")]
        public GameObject[] towerPrefabs;
        [Tooltip("Index-aligned with TD_EnemyKind.")]
        public GameObject[] enemyPrefabs;
        public GameObject slotPrefab, tracerPrefab, explosionPrefab;
        [Tooltip("Client-only HUD.")]
        public GameObject clientPrefab;

        public bool IsConfigured => playerSpawn != null && towerPrefabs != null && towerPrefabs.Length == TD_Rules.Towers.Length
            && enemyPrefabs != null && enemyPrefabs.Length == TD_Rules.Enemies.Length;

        void Start() {
#if !UNITY_SERVER
            if (clientPrefab != null) Instantiate(clientPrefab, transform);
#endif
        }

        public Vector3 SpawnPoint() {
            Vector2 s = Random.insideUnitCircle * spawnRadius;
            return playerSpawn.position + new Vector3(s.x, 0f, s.y);
        }
    }
}
