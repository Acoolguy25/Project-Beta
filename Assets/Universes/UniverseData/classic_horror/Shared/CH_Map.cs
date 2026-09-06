using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    public sealed class CH_Map : MonoBehaviour {
        public Transform arrival;
        public Transform extraction;
        public Transform monsterSpawn;
        [Tooltip("At least nine reachable investigation locations. A case shuffles their assignments.")]
        public Transform[] searchLocations;
        [Tooltip("One of these locations becomes the source of each new haunting.")]
        public Transform[] sourceLocations;
        public CH_StoryLibrary storyLibrary;
        public GameObject clueViewPrefab;
        public GameObject sourceViewPrefab;
        public GameObject clientPrefab;
        public Light[] practicalLights;

        void Start() {
#if !UNITY_SERVER
            if (clientPrefab != null) Instantiate(clientPrefab, transform);
#endif
        }

        public bool IsConfigured => arrival != null && extraction != null && monsterSpawn != null
            && searchLocations != null && searchLocations.Length >= 9
            && sourceLocations != null && sourceLocations.Length >= 3 && storyLibrary != null;
    }
}
