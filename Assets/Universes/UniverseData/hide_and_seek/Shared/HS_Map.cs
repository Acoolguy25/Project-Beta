using UnityEngine;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>A named part of the map (a room or a yard), for the HUD's "where am I" label.</summary>
    [System.Serializable]
    public struct HS_MapArea {
        public string name;
        public Bounds bounds;
        public bool indoor;
    }

    /// <summary>
    /// Lives in hide_and_seek_start: where hiders scatter to, where seekers wait blindfolded, the named
    /// areas and the client prefabs. Points are found by the authoring script by probing the map's floors.
    /// </summary>
    public sealed class HS_Map : MonoBehaviour {
        [Tooltip("Shown on the HUD and the results screen.")]
        public string mapName = "Hide and Seek";
        [Tooltip("Floor points with headroom, spread over the whole map. Hiders start at random ones.")]
        public Vector3[] hidePoints;
        public Transform seekerSpawn, lobbySpawn;
        [Min(0f)] public float spawnScatter = 2f;
        [Tooltip("Anything below this height has fallen out of the map.")]
        public float killY = -30f;
        [Tooltip("Rooms first, then larger outdoor zones: the first area containing a point names it.")]
        public HS_MapArea[] areas;
        [Header("Client presentation")]
        public GameObject pingPrefab;
        public GameObject clientPrefab;
        [Tooltip("Candle and lantern lights that flicker on clients (HS_LightFlicker animates them).")]
        public Light[] flickerLights;
        [Tooltip("The glow sprite of each flickering light (same order as Flicker Lights; null where a light has none), dimmed with it.")]
        public Renderer[] flickerGlows;
        [Tooltip("Where the grandfather clock ticks (world space), for the ambience.")]
        public Vector3 clockPosition;

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

        /// <summary>The name of the area containing <paramref name="position"/>, or an empty string.</summary>
        public string AreaAt(Vector3 position) => TryGetArea(position, out HS_MapArea area) ? area.name : "";

        /// <summary>Whether <paramref name="position"/> is inside a room (not a yard or off the map).</summary>
        public bool IsIndoors(Vector3 position) => TryGetArea(position, out HS_MapArea area) && area.indoor;

        public bool TryGetArea(Vector3 position, out HS_MapArea area) {
            if (areas != null)
                foreach (var candidate in areas)
                    if (candidate.bounds.Contains(position)) { area = candidate; return true; }
            area = default;
            return false;
        }
    }
}
