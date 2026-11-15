using UnityEngine;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>
    /// Lives in hide_and_seek_start: where hiders scatter to, where seekers wait blindfolded, and
    /// the client prefabs. Points are found by the authoring script by probing the map's floors.
    /// </summary>
    public sealed class HS_Map : MonoBehaviour {
        [Tooltip("Floor points with headroom, spread over the whole map. Hiders start at random ones.")]
        public Vector3[] hidePoints;
        public Transform seekerSpawn, lobbySpawn;
        [Min(0f)] public float spawnScatter = 2f;
        [Tooltip("Anything below this height has fallen out of the map.")]
        public float killY = -30f;
        [Header("Client presentation")]
        public GameObject pingPrefab;
        public GameObject clientPrefab;

        public bool IsConfigured => hidePoints != null && hidePoints.Length > 0 && seekerSpawn != null && lobbySpawn != null;

        void Start() {
#if !UNITY_SERVER
            if (clientPrefab != null) Instantiate(clientPrefab, transform);
#endif
        }

        public Vector3 Scatter(Vector3 point) {
            Vector2 s = Random.insideUnitCircle * spawnScatter;
            return point + new Vector3(s.x, 0f, s.y);
        }
    }
}
