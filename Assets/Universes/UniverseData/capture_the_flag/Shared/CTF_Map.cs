using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>Lives in capture_the_flag_start: bases, spawn points and client prefabs.</summary>
    public sealed class CTF_Map : MonoBehaviour {
        [Tooltip("Flag stand of each team: index 0 red, 1 blue.")]
        public Transform[] flagStands = new Transform[2];
        public Transform[] redSpawns, blueSpawns;
        public Transform lobbySpawn;
        public float killY = -20f;
        [Header("Client presentation")]
        [Tooltip("Flag visuals: index 0 red, 1 blue.")]
        public GameObject[] flagPrefabs = new GameObject[2];
        public GameObject clientPrefab;

        public bool IsConfigured => flagStands is { Length: 2 } && flagStands[0] != null && flagStands[1] != null
            && redSpawns is { Length: > 0 } && blueSpawns is { Length: > 0 } && lobbySpawn != null;

        public Vector3 Stand(CTF_Team team) => flagStands[(int)team].position;
        public Transform[] Spawns(CTF_Team team) => team == CTF_Team.Red ? redSpawns : blueSpawns;

        void Start() {
#if !UNITY_SERVER
            if (clientPrefab != null) Instantiate(clientPrefab, transform);
#endif
        }
    }
}
