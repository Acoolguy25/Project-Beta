using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Builds the world around the generated course, in the style of the tier each stretch belongs to:
    /// big landmarks standing off the route (floating castle, giant tree, pyramids, ice mountain,
    /// volcano, shattered planet), dense floating rocks and small islands, clouds over a cloud sea, bird
    /// flocks, the far horizon rings, ambient weather, and the course dressing that makes the route look
    /// built (rock under every floor, towers, arches over checkpoints, lamps, ropes and pennants).
    /// <para>
    /// Placement is planned from the course seed (<see cref="OB_ScenePlanner"/>,
    /// <see cref="OB_DressingPlanner"/>), so every racer sees the same world, and everything keeps clear
    /// of the space the course needs (<see cref="OB_CourseClearance"/>), so nothing blocks a jump or the
    /// chase camera. Rocks, islands and dressing are GPU-instanced (<see cref="OB_InstancedLayer"/>);
    /// landmarks and clouds are pooled objects moved for each course.
    /// </para>
    /// </summary>
    public sealed class OB_Backdrop : MonoBehaviour {
        [Serializable]
        public struct TierScenery {
            [Tooltip("Floating rock meshes (radius 1), picked at random.")]
            public Mesh[] rocks;
            [Tooltip("Small island meshes with the tier's props, picked at random.")]
            public Mesh[] islands;
            public Material material;
        }

        [Serializable]
        public struct TierDressing {
            [Tooltip("Rock under a floor: unit footprint, top at 0, bottom at -1 (scaled to the floor and its depth).")]
            public Mesh[] keels;
            [Tooltip("Band along a floor's bottom edge (unit cube).")]
            public Mesh trim;
            [Tooltip("Tower shaft (width 1, top at 0, bottom at -1) and the finial at its foot.")]
            public Mesh pillar, pillarCap;
            [Tooltip("Arch over a 6 m checkpoint pad, origin at the middle of its entry edge.")]
            public Mesh arch;
            [Tooltip("Banners down the arch posts, hanging from their origin.")]
            public Mesh[] banners;
            [Tooltip("Lamp post, origin at the pad's top level.")]
            public Mesh lamp;
            [Tooltip("Rope or chain, one metre along +Z (stretched per segment).")]
            public Mesh rope;
            [Tooltip("Pennants, icicles, lanterns or shards hanging from ropes, cycled.")]
            public Mesh[] pennants;
            [Tooltip("Stone, wood and metal.")]
            public Material solid;
            [Tooltip("Cloth that sways in the wind (banners, pennants).")]
            public Material cloth;
        }

        [Header("Layers")]
        public OB_InstancedLayer scenery;
        public OB_InstancedLayer dressing;
        public OB_Landmarks landmarks;
        public OB_HorizonLayers horizon;
        public OB_AmbientWeather weather;
        public OB_BirdFlocks birds;
        [Tooltip("Tells the horizon and weather when the player's tier changes.")]
        public OB_Atmosphere atmosphere;

        [Header("Authored pieces")]
        [Tooltip("Rock, island and dressing meshes per difficulty tier (indexed by OB_Tier).")]
        public TierScenery[] tierScenery;
        public TierDressing[] tierDressing;
        [Tooltip("Cloud cluster prefabs, cycled.")]
        public GameObject[] cloudPrefabs;
        [Tooltip("The cloud sea; kept under the camera, below the course's lowest point (its billows are in world space, so they hold still).")]
        public Transform cloudSea;
        [Tooltip("How far below the course's lowest point the cloud sea sits (m).")]
        [Min(0f)] public float seaDepth = 80f;
        [Tooltip("Least far clip of the game camera while this world is up (m): the horizon rings stand 2.4 to 3.4 km out, and landmarks stay in view across tiers instead of popping in.")]
        [Min(100f)] public float cameraFarClip = 3800f;

        [Header("Placement")]
        [Tooltip("A course with fewer stages than this per tier draws all its rocks, islands and clouds in the style of the tier the player is in (the one the sky shows): its tiers' stretches are too short to have scenery of their own.")]
        [Min(1)] public int stagesPerTierStretch = 3;
        public OB_ClearanceSettings clearanceSettings = OB_ClearanceSettings.Default;
        public OB_SceneSettings sceneSettings = OB_SceneSettings.Default;
        public OB_DressingSettings dressingSettings = OB_DressingSettings.Default;

        /// <summary>Seed for the lobby's sky before the first course arrives.</summary>
        const int LobbySeed = 7;

        readonly OB_CourseClearance clearance = new();
        readonly OB_ScenePlanner scenePlanner = new();
        readonly OB_DressingPlanner dressingPlanner = new();
        readonly List<Transform> clouds = new();
        float seaLevel;
        /// <summary>The course and lobby last arranged around, kept to restyle a short course's scenery when the tier changes.</summary>
        OB_CourseLayout arranged;
        Vector3 arrangedLobby;
        /// <summary>The tier all scenery is styled in on a short course, or null when each tier's stretch has its own.</summary>
        OB_Tier? sceneryStyle;
        Camera farView;
        float farViewOriginalClip;

        /// <summary>The free space of the current course (for diagnostics and tests).</summary>
        public OB_CourseClearance Clearance => clearance;
        public IReadOnlyList<OB_Placement> ScenePlacements => scenePlanner.Placements;
        public IReadOnlyList<OB_Placement> DressingPlacements => dressingPlanner.Placements;

        void OnEnable() {
            if (atmosphere != null) atmosphere.TierChanged += OnTierChanged;
        }

        void OnDisable() {
            if (atmosphere != null) atmosphere.TierChanged -= OnTierChanged;
            // The camera outlives the obby; hand it back as it was.
            if (farView != null) farView.farClipPlane = farViewOriginalClip;
            farView = null;
        }

        void LateUpdate() {
            Camera view = Camera.main;
            if (view == null) return;
            ReachHorizon(view);
            Follow(view.transform.position);
        }

        /// <summary>Lets the game camera see as far as the horizon rings, remembering its own clip to restore.</summary>
        void ReachHorizon(Camera view) {
            if (view != farView) {
                if (farView != null) farView.farClipPlane = farViewOriginalClip;
                farView = view;
                farViewOriginalClip = view.farClipPlane;
            }
            if (view.farClipPlane < cameraFarClip) view.farClipPlane = cameraFarClip;
        }

        /// <summary>Keeps the cloud sea under <paramref name="eye"/>, so its edge never shows however far the course runs (also called by preview renders).</summary>
        public void Follow(Vector3 eye) {
            if (cloudSea != null) cloudSea.position = new Vector3(eye.x, seaLevel, eye.z);
        }

        void OnTierChanged(OB_Tier tier, bool instant) {
            if (horizon != null) horizon.SetTier(tier, instant);
            if (weather != null) weather.SetTier(tier, instant);
            if (landmarks != null) landmarks.ShowTier(tier, instant);
            if (sceneryStyle.HasValue && sceneryStyle.Value != tier) Arrange(arranged, arrangedLobby);
        }

        /// <summary>Arranges the world around <paramref name="layout"/>, or around the lobby at <paramref name="lobby"/> when there is no course yet.</summary>
        public void Arrange(OB_CourseLayout layout, Vector3 lobby) {
            arranged = layout;
            arrangedLobby = lobby;
            sceneryStyle = HasTierStretches(layout) ? null : atmosphere != null ? atmosphere.Tier : OB_Tier.Basic;
            clearance.Build(layout, lobby, clearanceSettings);
            dressingPlanner.Plan(layout, clearance, dressingSettings);
            int seed = layout != null ? layout.seed : LobbySeed;
            scenePlanner.Plan(seed, layout?.StageCount ?? 1, clearance, sceneSettings, landmarks != null ? landmarks.Size : null, sceneryStyle);

            FillScenery();
            FillDressing();
            if (landmarks != null) landmarks.Arrange(scenePlanner.Placements);
            if (birds != null) birds.Arrange(scenePlanner.Placements);
            PlaceClouds();

            Bounds course = clearance.CourseBounds;
            seaLevel = course.min.y - seaDepth;
            Follow(course.center);
            if (horizon != null) horizon.SetAnchor(course.center);
        }

        /// <summary>True when every tier on <paramref name="layout"/> runs long enough to be dressed in its own style where it stands.</summary>
        bool HasTierStretches(OB_CourseLayout layout) {
            if (layout == null || layout.StageCount == 0) return true;
            Span<int> stages = stackalloc int[OB_Tiers.Count];
            foreach (var stage in layout.stages) stages[(int)stage.tier]++;
            foreach (int count in stages)
                if (count > 0 && count < stagesPerTierStretch) return false;
            return true;
        }

        /// <summary>Queues the instanced layers for <paramref name="view"/> only (editor preview renders, where Update does not run).</summary>
        public void DrawNow(Camera view) {
            if (scenery != null) scenery.Draw(view);
            if (dressing != null) dressing.Draw(view);
            if (birds != null) birds.Draw(view);
        }

        void FillScenery() {
            if (scenery == null) return;
            scenery.Clear();
            if (tierScenery == null) return;
            foreach (var placement in scenePlanner.Placements) {
                if ((int)placement.tier >= tierScenery.Length) continue;
                var set = tierScenery[(int)placement.tier];
                Mesh[] meshes = placement.kind switch {
                    (byte)OB_SceneryKind.Rock => set.rocks,
                    (byte)OB_SceneryKind.Island => set.islands,
                    _ => null
                };
                if (meshes == null || meshes.Length == 0) continue;
                scenery.Add(meshes[placement.variant % meshes.Length], set.material, placement.matrix);
            }
        }

        void FillDressing() {
            if (dressing == null) return;
            dressing.Clear();
            if (tierDressing == null) return;
            foreach (var placement in dressingPlanner.Placements) {
                if ((int)placement.tier >= tierDressing.Length) continue;
                var set = tierDressing[(int)placement.tier];
                var prop = (OB_DressingProp)placement.kind;
                Mesh mesh = prop switch {
                    OB_DressingProp.Keel => Pick(set.keels, placement.variant),
                    OB_DressingProp.Trim => set.trim,
                    OB_DressingProp.Pillar => set.pillar,
                    OB_DressingProp.PillarCap => set.pillarCap,
                    OB_DressingProp.Arch => set.arch,
                    OB_DressingProp.Banner => Pick(set.banners, placement.variant),
                    OB_DressingProp.Lamp => set.lamp,
                    OB_DressingProp.Rope => set.rope,
                    OB_DressingProp.Pennant => Pick(set.pennants, placement.variant),
                    _ => null
                };
                bool sways = prop is OB_DressingProp.Banner or OB_DressingProp.Pennant;
                dressing.Add(mesh, sways ? set.cloth : set.solid, placement.matrix);
            }
        }

        static Mesh Pick(Mesh[] meshes, int variant) => meshes == null || meshes.Length == 0 ? null : meshes[(variant & int.MaxValue) % meshes.Length];

        void PlaceClouds() {
            int used = 0;
            if (cloudPrefabs != null && cloudPrefabs.Length > 0)
                foreach (var placement in scenePlanner.Placements) {
                    if (placement.kind != (byte)OB_SceneryKind.Cloud) continue;
                    if (used == clouds.Count) {
                        var prefab = cloudPrefabs[used % cloudPrefabs.Length];
                        if (prefab == null) break;
                        clouds.Add(Instantiate(prefab, transform).transform);
                    }
                    var cloud = clouds[used++];
                    cloud.SetPositionAndRotation(placement.matrix.GetColumn(3), placement.matrix.rotation);
                    cloud.localScale = placement.matrix.lossyScale;
                    cloud.gameObject.SetActive(true);
                }
            for (int i = used; i < clouds.Count; i++) clouds[i].gameObject.SetActive(false);
        }
    }
}
