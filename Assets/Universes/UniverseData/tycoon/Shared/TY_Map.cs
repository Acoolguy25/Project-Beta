using RyanAssets.Rounds;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// The tycoon world in tycoon_start: plot anchors (+Z facing the plaza), the plaza spawn and the
    /// prefabs clients build plots from. The server only uses the anchors to place rule zones.
    /// </summary>
    public sealed class TY_Map : MonoBehaviour {
        [Tooltip("Plot bases. Each plot's forward axis points at the plaza.")]
        public TY_PlotView[] plots;
        public Transform plazaSpawn;
        [Min(0f)] public float plazaRadius = 8f;

        [Header("Prefabs (client presentation)")]
        [Tooltip("Index-aligned with TY_Catalog.Items; null for items without a model.")]
        public GameObject[] itemPrefabs;
        public GameObject buttonPrefab, claimPrefab, orePrefab;
        public Material[] oreMaterials;
        [Tooltip("Client-only HUD and plot presentation.")]
        public GameObject clientPrefab;

        public int PlotCount => plots?.Length ?? 0;
        public bool IsConfigured => PlotCount > 0 && plazaSpawn != null && itemPrefabs != null && itemPrefabs.Length == TY_Catalog.Count;

        void Start() {
#if !UNITY_SERVER
            if (clientPrefab != null) Instantiate(clientPrefab, transform);
#endif
        }

        public Vector3 World(int plot, Vector3 local) => plots[plot].transform.TransformPoint(local);
        public float Yaw(int plot) => plots[plot].transform.eulerAngles.y;
        ZoneBox Zone(int plot, Vector3 local, Vector3 size) => new(World(plot, local) + Vector3.up * 0.5f, size, Yaw(plot));

        public ZoneBox ButtonZone(int plot, int item) => Zone(plot, TY_Catalog.Items[item].buttonPosition, TY_Catalog.ButtonSize);
        public ZoneBox ClaimZone(int plot) => Zone(plot, TY_Catalog.ClaimPosition, TY_Catalog.PadSize);
        public ZoneBox CollectZone(int plot) => Zone(plot, TY_Catalog.CollectPosition, TY_Catalog.PadSize);
        public ZoneBox PlotZone(int plot) => Zone(plot, Vector3.zero, new Vector3(TY_Catalog.PlotSize, 30f, TY_Catalog.PlotSize));

        public Vector3 PlotSpawn(int plot) => World(plot, TY_Catalog.SpawnPosition);

        public Vector3 PlazaSpawn() {
            Vector2 scatter = Random.insideUnitCircle * plazaRadius;
            return plazaSpawn.position + new Vector3(scatter.x, 0f, scatter.y);
        }
    }
}
