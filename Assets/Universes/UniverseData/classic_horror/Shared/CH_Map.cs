using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// The authored set for The Drowned Choir on the Flooded Grounds map: where the night starts, where items can lie,
    /// the boat and its part places, the singers' graves, Lily's marker, the chapel altar, the hiding places, and the
    /// presentation the clients need. <b>Ryan > Classic Horror > Build Story Set</b> places and wires these.
    /// </summary>
    public sealed class CH_Map : MonoBehaviour {
        [Header("Night")]
        [Tooltip("The jetty where the boat docks; investigators start and regroup here.")]
        public Transform arrival;
        public Transform monsterSpawn;
        [Tooltip("Reachable places across the village where items can lie (at least seven), also the priest's patrol points.")]
        public Transform[] searchLocations;

        [Header("Story set")]
        [Tooltip("The boat's fuel, toolbox, lantern and bell places, in that order.")]
        public Transform[] boatSlots = new Transform[4];
        [Tooltip("Everyone within boardingRadius of this point is aboard when the boat leaves.")]
        public Transform boardingPoint;
        [Min(1f)] public float boardingRadius = 8f;
        [Tooltip("One grave per drowned singer, in CH_Story.Singers order.")]
        public Transform[] graves = new Transform[CH_Story.SingerCount];
        [Tooltip("Lily's marker on the jetty steps, where her music box goes.")]
        public Transform lilyMarker;
        [Tooltip("The chapel altar, where the bell waits in Act III.")]
        public Transform altar;
        [Tooltip("Places the music box can be hidden (it picks one each night).")]
        public Transform[] musicBoxSpots = new Transform[0];
        public CH_HidingSpot[] hidingSpots = new CH_HidingSpot[0];

        [Header("Presentation")]
        public CH_VoiceBank voices;
        public CH_CutsceneSet cutscenes;
        [Tooltip("The priest as a client-only puppet (no networking or collision), used by cutscenes and apparitions.")]
        public GameObject priestActorPrefab;
        [Tooltip("Mara, the ferry's skipper, as a client-only puppet for the cutscenes.")]
        public GameObject maraActorPrefab;
        [Tooltip("What each item looks like, in CH_ItemKind order. The same prefab lies in the world, is carried and is placed.")]
        public GameObject[] itemPrefabs = new GameObject[8];
        [Tooltip("A faint marker shown where a carried item belongs.")]
        public GameObject slotMarkerPrefab;
        [Tooltip("Lit when a singer's grave receives their relic.")]
        public GameObject graveCandlesPrefab;
        public GameObject clientPrefab;
        public Light[] practicalLights;
        [Tooltip("The emissive part of each practical lamp, in the same order as practicalLights (may hold nulls). Flickers with its light.")]
        public Renderer[] practicalGlows;
        [Tooltip("Height of the flooded ground's water surface. Investigators are never spawned at or below this line.")]
        public float waterLevel = 16.5f;

        void Start() {
#if !UNITY_SERVER
            if (clientPrefab != null) Instantiate(clientPrefab, transform);
#endif
        }

        public bool IsConfigured => arrival != null && monsterSpawn != null && voices != null
            && searchLocations != null && searchLocations.Length >= 7
            && boatSlots != null && boatSlots.Length == 4 && System.Array.TrueForAll(boatSlots, slot => slot != null)
            && graves != null && graves.Length == CH_Story.SingerCount && System.Array.TrueForAll(graves, grave => grave != null)
            && lilyMarker != null && altar != null && boardingPoint != null;
    }
}
