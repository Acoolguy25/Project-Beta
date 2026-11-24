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
        [Tooltip("Index-aligned with TY_Catalog.Items: a rendered picture of each item for the HUD.")]
        public Sprite[] itemPictures;
        [Tooltip("Buy pad per lane, indexed by TY_ItemKind.")]
        public GameObject[] padPrefabs;
        public GameObject orePrefab, goldCoinPrefab;
        [Tooltip("The daily reward chest (with a TY_ChestView), placed on the local player's own plot.")]
        public GameObject chestPrefab;
        public Material[] oreMaterials;
        [Tooltip("Client-only HUD and plot presentation.")]
        public GameObject clientPrefab;

        /// <summary>Footprint of a pickup (gold coin, daily chest) the player walks into.</summary>
        static readonly Vector3 PickupSize = new(2.4f, 3f, 2.4f);

        public int PlotCount => plots?.Length ?? 0;
        public bool IsConfigured => PlotCount > 0 && plazaSpawn != null && itemPrefabs != null && itemPrefabs.Length == TY_Catalog.Count
            && padPrefabs != null && padPrefabs.Length == System.Enum.GetValues(typeof(TY_ItemKind)).Length;

        public Sprite Picture(int item) => itemPictures != null && item >= 0 && item < itemPictures.Length ? itemPictures[item] : null;

        void Start() {
#if !UNITY_SERVER
            if (clientPrefab != null) Instantiate(clientPrefab, transform);
#endif
        }

        public Vector3 World(int plot, Vector3 local) => plots[plot].transform.TransformPoint(local);
        public float Yaw(int plot) => plots[plot].transform.eulerAngles.y;
        ZoneBox Zone(int plot, Vector3 local, Vector3 size) => new(World(plot, local) + Vector3.up * 0.5f, size, Yaw(plot));

        public ZoneBox ButtonZone(int plot, int item) => Zone(plot, TY_Catalog.LanePosition(TY_Catalog.Items[item].kind), TY_Catalog.ButtonSize);
        public ZoneBox GoldenZone(int plot, Vector3 local) => Zone(plot, local, PickupSize);
        public ZoneBox ChestZone(int plot) => Zone(plot, TY_Catalog.ChestPosition, PickupSize);
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
