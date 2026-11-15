using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Globals;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Where each kind of thing may go on the War Valley map. Lives on the map's root, whose origin is
    /// the terrain's corner.
    /// <list type="bullet">
    /// <item>Everyone stays inside the map, held by the invisible edge walls past which the scenery
    /// only looks endless.</item>
    /// <item>The commanders' troops, vehicles and buildings stay inside the <see cref="AlliedArea"/>.
    /// The band around it is the frontier the waves march in from. It is baked into the NavMesh as
    /// <see cref="WV_NavAreas.Frontier"/>, which ground units on the commanders' side cannot path
    /// across. Aircraft and building placement are held to the same rectangle here.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WV_PlayArea : MonoBehaviour {
        [SerializeField, Tooltip("Size of the map in metres, from the root's origin.")]
        Vector2 mapSize = new(1500f, 1500f);

        [SerializeField, Min(0f), Tooltip("Width of the frontier band inside the map edge that the commanders' forces cannot enter.")]
        float frontierDepth = 220f;

        /// <summary>The play area of the loaded War Valley map, or null when none is loaded.</summary>
        public static WV_PlayArea Current { get; private set; }

        public Vector2 MapSize => mapSize;
        public float FrontierDepth => frontierDepth;

        /// <summary>The whole map on the ground plane, in world X/Z.</summary>
        public Rect MapArea {
            get {
                Vector3 origin = transform.position;
                return new Rect(origin.x, origin.z, mapSize.x, mapSize.y);
            }
        }

        /// <summary>The commanders' part of the map on the ground plane, in world X/Z.</summary>
        public Rect AlliedArea {
            get {
                Rect map = MapArea;
                float depth = Mathf.Min(frontierDepth, Mathf.Min(map.width, map.height) * 0.5f);
                return Rect.MinMaxRect(map.xMin + depth, map.yMin + depth, map.xMax - depth, map.yMax - depth);
            }
        }

        public bool IsInAlliedArea(Vector3 worldPosition) =>
            AlliedArea.Contains(new Vector2(worldPosition.x, worldPosition.z));

        /// <summary>The nearest point to <paramref name="worldPosition"/> inside the <see cref="AlliedArea"/>, at the same height.</summary>
        public Vector3 ClampToAlliedArea(Vector3 worldPosition) {
            Rect allied = AlliedArea;
            worldPosition.x = Mathf.Clamp(worldPosition.x, allied.xMin, allied.xMax);
            worldPosition.z = Mathf.Clamp(worldPosition.z, allied.yMin, allied.yMax);
            return worldPosition;
        }

        /// <summary>
        /// Where a unit on <paramref name="side"/> may be sent: the commanders' side is held inside the
        /// <see cref="AlliedArea"/>, the waves may go anywhere. Unchanged when no map is loaded.
        /// </summary>
        public static Vector3 ClampDestination(Vector3 destination, TeamColor side) =>
            Current == null || side == WV_Alliances.GetWaveSide() ? destination : Current.ClampToAlliedArea(destination);

        void OnEnable() {
            if (Current != null && Current != this)
                Debug.LogWarning($"Two War Valley play areas are loaded; {name} replaces {Current.name}.", this);
            Current = this;
            StructurePlacement.BuildAreaRule = GetBuildAreaProblem;
        }

        void OnDisable() {
            if (Current != this)
                return;
            Current = null;
            if (StructurePlacement.BuildAreaRule == GetBuildAreaProblem)
                StructurePlacement.BuildAreaRule = null;
        }

        string GetBuildAreaProblem(Vector3 groundPoint) =>
            IsInAlliedArea(groundPoint) ? null : "Too far out - build closer to the base";

        void OnDrawGizmosSelected() {
            Rect map = MapArea;
            Rect allied = AlliedArea;
            float y = transform.position.y + 2f;
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(new Vector3(map.center.x, y, map.center.y), new Vector3(map.width, 0f, map.height));
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(new Vector3(allied.center.x, y, allied.center.y), new Vector3(allied.width, 0f, allied.height));
        }
    }
}
