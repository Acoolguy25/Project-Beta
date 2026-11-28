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
        [Tooltip("The doors of the walk-in hiding spots, in a fixed order (the network refers to them by index; at most 64).")]
        public HS_HideDoor[] doors;
        [Header("Client presentation")]
        public GameObject pingPrefab;
        public GameObject clientPrefab;
        [Tooltip("Candle and lantern lights that flicker on clients (HS_LightFlicker animates them).")]
        public Light[] flickerLights;
        [Tooltip("The glow sprite of each flickering light (same order as Flicker Lights; null where a light has none), dimmed with it.")]
        public Renderer[] flickerGlows;
        [Tooltip("Where the grandfather clock ticks (world space), for the ambience.")]
        public Vector3 clockPosition;
        [Tooltip("Every light a Blackout dims (the sun and the lamps, not the flickering candles), filled by HS_Authoring, so a " +
                 "light spawned later (a flashlight beam) or one from another scene is never caught.")]
        public Light[] blackoutLights;
        [Tooltip("The flashlight's beam (HS_FlashlightBeam): a spot light and a cone, drawn on every client for a moment.")]
        public GameObject flashlightBeamPrefab;
        [Tooltip("Shown only to the hider who threw a decoy, where it landed, for as long as it is live (HS_DecoyMarker).")]
        public GameObject decoyMarkerPrefab;
        [Tooltip("A glowing pumpkin of the pumpkin hunt (HS_PumpkinView), placed on every client where the server lights one.")]
        public GameObject pumpkinPrefab;

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

        /// <summary>The door with this index, or null for an index the map does not have.</summary>
        public HS_HideDoor Door(int index) => doors != null && index >= 0 && index < doors.Length ? doors[index] : null;

        /// <summary>The door whose spot <paramref name="position"/> is inside, or -1.</summary>
        public int DoorAround(Vector3 position) {
            if (doors == null) return -1;
            for (int i = 0; i < doors.Length; i++)
                if (doors[i] != null && doors[i].Contains(position)) return i;
            return -1;
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
