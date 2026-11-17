using System.Collections.Generic;
using System.IO;
using System.Linq;
using RyanAssets.Rounds;
using TMPro;
using UnityEditor;
using UnityEngine;
using static RyanAssets.Authoring.UniverseAuthoringKit;

namespace Universes.UniverseData.capture_the_flag.Editor {
    /// <summary>
    /// The Flag Wars arena, point-symmetric through the centre so neither team is favoured:
    /// <list type="bullet">
    /// <item>Two raised, team-coloured bases (red at -x, blue at +x) with parapets, a back wall with
    /// banners and lights, the flag on a dais and three exits: a front ramp to the mid lane and side
    /// ramps to the north and south lanes.</item>
    /// <item>Three lanes marked by roads. Mid: a 5 m bridge in the centre (the speed power-up sits on
    /// top), sandbags, a tank and a health power-up on each half. North/south: a hangar, a raised
    /// catwalk and a bunker, oil tanks, an APC, containers and crates.</item>
    /// <item>Lane dividers of containers, rocks and trees with gaps to rotate through.</item>
    /// </list>
    /// Art: Cartoon Military pack, KayKit Space Base / Prototype / Medieval Hexagon (CC0) and Poly Haven
    /// textures (CC0), see CREDITS.md. Layout numbers are in metres; the robot is about 5.6 m tall.
    /// Everything is placed on the red half and mirrored with <see cref="Mirrored"/>.
    /// </summary>
    internal static class CTF_ArenaAuthoring {
        // ---- field
        static readonly Vector2 SignBox = new(26f, 5f);
        static readonly Vector2 TitleBox = new(30f, 7f);
        /// <summary>Largest share of the screen height a base sign or the title may cover up close.</summary>
        const float SignMaxScreen = 0.12f;
        const float TitleMaxScreen = 0.16f;
        const float HalfLength = 120f;
        const float HalfWidth = 72f;
        const float WallHeight = 7f;
        const float WallThickness = 1.5f;
        /// <summary>Grass beyond the walls, so the scenery ring and the horizon are not void.</summary>
        const float Backdrop = 110f;
        const float KillY = -15f;

        // ---- lanes
        const float LaneZ = 44f;
        const float MidRoadWidth = 10f;
        const float SideRoadWidth = 8f;
        const float RoadThickness = 0.04f;

        // ---- bases (f = metres toward the centre from the base middle, r = metres to the side)
        const float BaseX = 92f;
        const float BaseHeight = 2.4f;
        const float BaseDepth = 30f;
        const float BaseWidth = 44f;
        const float BaseRampRun = 12f;
        const float FrontRampWidth = 12f;
        const float SideRampWidth = 10f;
        const float SideRampF = 2f;
        /// <summary>Chest high on the 5.6 m robot (its eye is 4.1 m up): you see and shoot over it, it covers legs and body.</summary>
        const float ParapetHeight = 3.5f;
        const float ParapetThickness = 1.2f;
        const float BackWallHeight = 9f;
        const float FlagF = -4f;
        const float StandRadius = 3.4f;
        static readonly float[] SpawnF = { 6f, 10f };
        static readonly float[] SpawnR = { -13f, -4.5f, 4.5f, 13f };

        // ---- centre
        const float BridgeHeight = 5f;
        const float BridgeLength = 18f;
        const float BridgeWidth = 10f;
        const float BridgeRampRun = 14f;
        const float BridgeRampWidth = 10f;
        /// <summary>Waist high on the robot, so the bridge and catwalks are not shooting galleries.</summary>
        const float RailHeight = 3f;
        // ---- cover (the robot is about 5.6 m tall)
        /// <summary>Low cover: chest high (the robot's eye is 4.1 m up), shoot over it.</summary>
        const float LowCoverHeight = 3.5f;
        /// <summary>Full cover: hides a standing robot completely.</summary>
        const float FullCoverHeight = 7f;
        const float FullCoverLength = 8f, FullCoverThickness = 1.4f;
        const float HealthPowerUpX = -48f;
        static readonly Vector3 LobbySpawn = new(0f, 0.5f, -10f);

        // ---- look
        static readonly Color SkyTint = new(0.52f, 0.56f, 0.62f);
        static readonly Color FogColor = new(0.74f, 0.81f, 0.9f);
        const float FogStart = 160f, FogEnd = 560f;
        static readonly Vector3 SunEuler = new(42f, -35f, 0f);
        const float SunIntensity = 1.35f;
        const float TeamLightIntensity = 3f, TeamLightRange = 55f;
        /// <summary>How far each base floor's tiles are tinted toward the team colour.</summary>
        const float BaseFloorTint = 0.4f;
        /// <summary>Neutral props: khaki tint, how much of it is plain grey, and how much original colour survives.</summary>
        static readonly Color NeutralTint = new(0.78f, 0.74f, 0.6f);
        const float NeutralGreyShare = 0.35f, NeutralKeep = 0.12f;

        // ---- asset paths
        const string Military = "Assets/CartoonMilitaryModelPack/Prefebs/";
        const string KayKit = "Assets/ThirdParty/KayKit/";
        const string Space = KayKit + "SpaceBaseBits/Models/";
        const string Proto = KayKit + "PrototypeBits/Models/";
        const string Medieval = KayKit + "MedievalHexagonPack/";
        const string Nature = Medieval + "decoration/nature/";
        const string Props = Medieval + "decoration/props/";
        const string PolyHaven = "Assets/ThirdParty/PolyHaven/Textures/";

        /// <summary>
        /// How a prop collides. Box: its whole bounds (solid blocks: crates, containers, rocks). Trunk: a
        /// narrow box (trees). Mesh: its own shape, for open props (stilted guard posts, light towers,
        /// the radar, the tank's barrel, the open hangar) where a bounds box would make empty air solid.
        /// </summary>
        enum Solid { None, Box, Trunk, Mesh }

        sealed class Mats {
            public Material grass, road, concrete, wall, sandbag, space, proto, medieval;
            public Material[] floor, parapet, trim, banner;
        }

        /// <summary>Opens a new scene, builds the arena and returns the configured map object.</summary>
        public static GameObject Build(GameObject[] powerUpPrefabs) {
            CTF_Authoring.Kit.NewLitScene(SkyTint, FogColor, FogStart, FogEnd, SunEuler, SunIntensity);
            TuneSky();
            var m = BuildMaterials();
            var world = new GameObject("World").transform;
            var mapGo = new GameObject("FlagWarsMap");
            var map = mapGo.AddComponent<CTF_Map>();

            Field(world, m);
            map.flagStands = new Transform[2];
            var spawns = new[] { new List<Transform>(), new List<Transform>() };
            for (int t = 0; t < 2; t++) {
                var team = (CTF_Team)t;
                map.flagStands[t] = Base(team, world, mapGo.transform, m, spawns[t]);
            }
            map.redSpawns = spawns[0].ToArray();
            map.blueSpawns = spawns[1].ToArray();
            map.lobbySpawn = Empty("LobbySpawn", mapGo.transform, LobbySpawn).transform;
            map.killY = KillY;

            map.title = Centre(world, m);
            MidLane(world, m);
            NorthLane(world, m);
            SouthLane(world, m);
            Dividers(world, m);
            Scenery(world, m);
            map.powerUps = PowerUps(mapGo.transform, powerUpPrefabs);
            return mapGo;
        }

        // ---------------------------------------------------------------- look
        static void TuneSky() {
            var sky = RenderSettings.skybox;
            if (sky == null) return;
            sky.SetFloat("_AtmosphereThickness", 1f);
            sky.SetFloat("_Exposure", 1.3f);
            sky.SetColor("_GroundColor", new Color(0.42f, 0.44f, 0.4f));
            EditorUtility.SetDirty(sky);
        }

        static Mats BuildMaterials() {
            var kit = CTF_Authoring.Kit;
            var concrete = kit.Tex("Concrete");
            var m = new Mats {
                grass = Surface("Grass", "leafy_grass", new Color(0.74f, 0.95f, 0.64f), (HalfLength + Backdrop) * 2f / 5f, (HalfWidth + Backdrop) * 2f / 5f, kit.Tex("Grass")),
                road = Surface("Road", "cobblestone_floor_01", new Color(0.88f, 0.86f, 0.82f), HalfLength * 2f / 3f, 3f, concrete),
                concrete = Tiled(kit.Mat("Concrete", Color.white, concrete, smoothness: 0.15f), 4f, 4f),
                wall = Surface("Wall", "white_stucco", new Color(0.7f, 0.68f, 0.64f), HalfLength * 2f / 4f, 2f, concrete),
                sandbag = Tiled(kit.Mat("Sandbag", Color.white, kit.Tex("Sandbag"), smoothness: 0.05f), 3f, 1f),
                space = Atlas("KayKitSpace", NeutralAtlas("SpaceNeutral", Space + "spacebits_texture.png")),
                proto = Atlas("KayKitProto", NeutralAtlas("ProtoNeutral", Proto + "prototypebits_texture.png")),
                medieval = Atlas("KayKitMedieval", Medieval + "tiles/base/hexagons_medieval.png"),
                floor = new Material[2], parapet = new Material[2], trim = new Material[2], banner = new Material[2],
            };
            for (int t = 0; t < 2; t++) {
                var team = (CTF_Team)t;
                Color color = CTF_Rules.Color(team);
                // Neutral grey tiles, so each base floor reads as its team colour rather than terracotta.
                m.floor[t] = Surface($"BaseFloor{team}", "floor_tiles_06", Color.Lerp(Color.white, color, BaseFloorTint), BaseDepth / 3f, BaseWidth / 3f, concrete);
                m.parapet[t] = Surface($"Parapet{team}", "white_stucco", Color.Lerp(new Color(0.62f, 0.62f, 0.62f), color, 0.45f), 4f, 1f, concrete);
                m.trim[t] = kit.Mat($"Team{team}", color, emission: 0.8f);
                m.banner[t] = kit.Mat($"Flag{team}", color, emission: 0.35f);
            }
            return m;
        }

        /// <summary>A Poly Haven texture set on URP Lit (colour + OpenGL normal when imported as one), or the fallback texture.</summary>
        static Material Surface(string name, string set, Color tint, float tileX, float tileY, Texture2D fallback) {
            string dir = $"{PolyHaven}{set}/{set}";
            var colour = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}_diff.jpg");
            if (colour == null) Debug.LogWarning($"[FlagWars] Poly Haven texture {set} missing; {name} uses a generated texture.");
            var mat = CTF_Authoring.Kit.Mat(name, tint, colour != null ? colour : fallback, smoothness: 0.15f);
            string normalPath = $"{dir}_nor_gl.jpg";
            bool hasNormal = colour != null && AssetImporter.GetAtPath(normalPath) is TextureImporter { textureType: TextureImporterType.NormalMap };
            mat.SetTexture("_BumpMap", hasNormal ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) : null);
            if (hasNormal) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
            return Tiled(mat, tileX, tileY);
        }

        /// <summary>
        /// Saturated red and blue belong to the teams (bases, flags, beacons, players). The space and
        /// prototype packs paint containers, cargo and barrels red, blue and green, so the arena uses a
        /// copy of their palette with the colour washed toward khaki, saved in this game's Textures folder.
        /// </summary>
        static string NeutralAtlas(string name, string sourcePath) {
            if (!File.Exists(sourcePath)) return sourcePath; // Atlas() warns
            var source = new Texture2D(2, 2);
            source.LoadImage(File.ReadAllBytes(sourcePath));
            var pixels = source.GetPixels();
            for (int i = 0; i < pixels.Length; i++) {
                Color c = pixels[i];
                float grey = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                Color neutral = NeutralTint * Mathf.Lerp(0.35f, 1.05f, grey);
                pixels[i] = Color.Lerp(neutral, new Color(grey, grey, grey), NeutralGreyShare) * (1f - NeutralKeep) + c * NeutralKeep;
                pixels[i].a = c.a;
            }
            source.SetPixels(pixels);
            string path = $"{CTF_Authoring.Root}/Textures/{name}.png";
            File.WriteAllBytes(path, source.EncodeToPNG());
            Object.DestroyImmediate(source);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(sourcePath) is TextureImporter from && AssetImporter.GetAtPath(path) is TextureImporter to) {
                to.filterMode = from.filterMode;
                to.mipmapEnabled = from.mipmapEnabled;
                to.wrapMode = from.wrapMode;
                to.SaveAndReimport();
            }
            return path;
        }

        /// <summary>One material per KayKit pack: all its models share one palette texture.</summary>
        static Material Atlas(string name, string texturePath) {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (atlas == null) Debug.LogWarning($"[FlagWars] KayKit atlas missing: {texturePath}.");
            return CTF_Authoring.Kit.Mat(name, Color.white, atlas, smoothness: 0.3f);
        }

        // ---------------------------------------------------------------- building blocks
        /// <summary>Places the same thing on both halves, mirrored through the centre (point symmetry).</summary>
        static void Mirrored(System.Action<Vector3, float> place, Vector3 position, float yaw) {
            place(position, yaw);
            place(new Vector3(-position.x, position.y, -position.z), yaw + 180f);
        }

        /// <summary>
        /// A model placed with its base on <paramref name="position"/> (local to <paramref name="parent"/>), largest side <paramref name="size"/>
        /// m, optionally repainted with a pack material, with a collider: the whole bounds (cover) or a
        /// narrow trunk (trees: you bump the trunk and walk under the canopy).
        /// </summary>
        static GameObject Prop(string path, Transform parent, Vector3 position, float size, float yaw, Material material, Solid solid) {
            var anchor = Empty(Path.GetFileNameWithoutExtension(path), parent, position);
            var model = Model(path, anchor.transform, size);
            if (model == null) {
                Debug.LogWarning($"[FlagWars] Model missing: {path}.");
                Object.DestroyImmediate(anchor);
                return null;
            }
            if (material != null)
                foreach (var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => material).ToArray();
            if (solid == Solid.Mesh) {
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                    if (filter.sharedMesh != null) filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            } else if (solid != Solid.None) {
                var bounds = RendererBounds(model);
                var box = anchor.AddComponent<BoxCollider>();
                box.center = bounds.center - anchor.transform.position;
                float narrow = solid == Solid.Trunk ? 0.3f : 1f;
                box.size = new Vector3(bounds.size.x * narrow, bounds.size.y, bounds.size.z * narrow);
            }
            anchor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            foreach (Transform t in anchor.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            return anchor;
        }

        /// <summary>A solid block whose top is at <paramref name="height"/>, centred on (x, z).</summary>
        static GameObject Deck(string name, Transform parent, float x, float z, float sizeX, float sizeZ, float height, Material mat) =>
            Cube(name, parent, new Vector3(x, height * 0.5f, z), new Vector3(sizeX, height, sizeZ), mat);

        /// <summary>
        /// A walkable slope from <paramref name="top"/> (middle of its upper edge) running
        /// <paramref name="run"/> metres along <paramref name="down"/> to the ground.
        /// </summary>
        static GameObject Ramp(string name, Transform parent, Vector3 top, Vector3 down, float run, float width, Material mat, float thickness = 0.6f) {
            down.y = 0f;
            down.Normalize();
            float rise = top.y;
            float length = Mathf.Sqrt(run * run + rise * rise);
            float pitch = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            var rotation = Quaternion.LookRotation(down) * Quaternion.Euler(pitch, 0f, 0f);
            Vector3 centre = top + down * (run * 0.5f) + Vector3.down * (rise * 0.5f) - rotation * Vector3.up * (thickness * 0.5f);
            var ramp = Cube(name, parent, centre, new Vector3(width, thickness, length), mat);
            ramp.transform.rotation = rotation;
            return ramp;
        }

        static void Road(string name, Transform parent, Vector3 from, Vector3 to, float width, Material mat) {
            Vector3 along = to - from;
            var road = Cube(name, parent, (from + to) * 0.5f + Vector3.up * RoadThickness * 0.5f, new Vector3(width, RoadThickness, along.magnitude), mat, false);
            road.transform.rotation = Quaternion.LookRotation(along.normalized);
        }

        static void Sandbags(Transform parent, Vector3 position, float length, float yaw, Material mat) {
            var wall = Cube("Sandbags", parent, position + Vector3.up * (LowCoverHeight * 0.5f), new Vector3(length, LowCoverHeight, 1.8f), mat);
            wall.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>A concrete blast wall taller than a robot: full cover at lane entrances and by the health pads.</summary>
        static void BlastWall(Transform parent, Vector3 position, float yaw, Mats m) {
            var wall = Cube("BlastWall", parent, position + Vector3.up * (FullCoverHeight * 0.5f), new Vector3(FullCoverLength, FullCoverHeight, FullCoverThickness), m.concrete);
            wall.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Two big crates side by side with a third on top.</summary>
        static void Crates(Transform parent, Vector3 position, float yaw, Mats m) {
            var stack = Empty("Crates", parent, position);
            const float size = 3.4f;
            // Prop positions are local to the stack, which turns as one afterwards.
            Prop(Props + "crate_A_big.fbx", stack.transform, new Vector3(-size * 0.55f, 0f, 0f), size, 4f, m.medieval, Solid.Box);
            Prop(Props + "crate_B_big.fbx", stack.transform, new Vector3(size * 0.55f, 0f, 0f), size, -6f, m.medieval, Solid.Box);
            Prop(Props + "crate_A_big.fbx", stack.transform, new Vector3(0f, size * 0.9f, 0f), size, 12f, m.medieval, Solid.Box);
            stack.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ---------------------------------------------------------------- field
        static void Field(Transform world, Mats m) {
            var field = Empty("Field", world).transform;
            Cube("Ground", field, new Vector3(0f, -0.5f, 0f), new Vector3((HalfLength + Backdrop) * 2f, 1f, (HalfWidth + Backdrop) * 2f), m.grass);
            float height = WallHeight * 0.5f;
            Cube("WallN", field, new Vector3(0f, height, HalfWidth), new Vector3(HalfLength * 2f + WallThickness, WallHeight, WallThickness), m.wall);
            Cube("WallS", field, new Vector3(0f, height, -HalfWidth), new Vector3(HalfLength * 2f + WallThickness, WallHeight, WallThickness), m.wall);
            Cube("WallE", field, new Vector3(HalfLength, height, 0f), new Vector3(WallThickness, WallHeight, HalfWidth * 2f), m.wall);
            Cube("WallW", field, new Vector3(-HalfLength, height, 0f), new Vector3(WallThickness, WallHeight, HalfWidth * 2f), m.wall);

            // Lanes read as roads: mid from front ramp to front ramp, sides from base to base.
            float midEnd = BaseX - BaseDepth * 0.5f - BaseRampRun;
            Road("MidRoad", field, new Vector3(-midEnd, 0f, 0f), new Vector3(midEnd, 0f, 0f), MidRoadWidth, m.road);
            float sideEnd = BaseX + 3f;
            Road("NorthRoad", field, new Vector3(-sideEnd, 0f, LaneZ), new Vector3(sideEnd, 0f, LaneZ), SideRoadWidth, m.road);
            Road("SouthRoad", field, new Vector3(-sideEnd, 0f, -LaneZ), new Vector3(sideEnd, 0f, -LaneZ), SideRoadWidth, m.road);
            // Side ramps of each base onto the side roads.
            float rampFoot = BaseWidth * 0.5f + BaseRampRun;
            Mirrored((p, _) => Road("BaseLink", field, p, new Vector3(p.x, 0f, LaneZ * Mathf.Sign(p.z)), SideRoadWidth, m.road), new Vector3(-BaseX + SideRampF, 0f, rampFoot), 0f);
            Mirrored((p, _) => Road("BaseLink", field, p, new Vector3(p.x, 0f, LaneZ * Mathf.Sign(p.z)), SideRoadWidth, m.road), new Vector3(-BaseX + SideRampF, 0f, -rampFoot), 0f);

            // Flood lights in the four corners.
            Mirrored((p, y) => Prop(Military + "Building_Prefebs/LightTower_Prefeb.prefab", field, p, 14f, y, null, Solid.Mesh), new Vector3(-HalfLength + 6f, 0f, HalfWidth - 6f), 0f);
            Mirrored((p, y) => Prop(Military + "Building_Prefebs/LightTower_Prefeb.prefab", field, p, 14f, y, null, Solid.Mesh), new Vector3(-HalfLength + 6f, 0f, -HalfWidth + 6f), 0f);
        }

        // ---------------------------------------------------------------- bases
        /// <summary>Builds one team's base; returns its flag point. Red sits at -x facing +x, blue mirrors it.</summary>
        static Transform Base(CTF_Team team, Transform world, Transform mapRoot, Mats m, List<Transform> spawns) {
            int t = (int)team;
            float s = team == CTF_Team.Red ? 1f : -1f;
            float yaw = team == CTF_Team.Red ? 0f : 180f;
            // f: toward the centre, r: sideways; red's world = (-BaseX + f, y, r), blue is the point mirror.
            Vector3 At(float f, float y, float r) => new(s * (-BaseX + f), y, s * r);
            Vector3 forward = new(s, 0f, 0f), right = new(0f, 0f, s);
            var root = Empty($"{CTF_Rules.Name(team)}Base", world).transform;
            float halfDepth = BaseDepth * 0.5f, halfWidth = BaseWidth * 0.5f;

            Deck("Platform", root, At(0f, 0f, 0f).x, 0f, BaseDepth, BaseWidth, BaseHeight, m.floor[t]);
            // Exits: front to the mid lane, sides to the north and south lanes.
            Ramp("RampFront", root, At(halfDepth, BaseHeight, 0f), forward, BaseRampRun, FrontRampWidth, m.concrete);
            Ramp("RampNorth", root, At(SideRampF, BaseHeight, halfWidth), right, BaseRampRun, SideRampWidth, m.concrete);
            Ramp("RampSouth", root, At(SideRampF, BaseHeight, -halfWidth), -right, BaseRampRun, SideRampWidth, m.concrete);

            // Team-coloured edge trim.
            float trimY = BaseHeight + 0.03f;
            Cube("TrimFront", root, At(halfDepth - 0.4f, trimY, 0f), new Vector3(0.8f, 0.06f, BaseWidth), m.trim[t], false);
            Cube("TrimBack", root, At(-halfDepth + 0.4f, trimY, 0f), new Vector3(0.8f, 0.06f, BaseWidth), m.trim[t], false);
            Cube("TrimNorth", root, At(0f, trimY, halfWidth - 0.4f), new Vector3(BaseDepth, 0.06f, 0.8f), m.trim[t], false);
            Cube("TrimSouth", root, At(0f, trimY, -halfWidth + 0.4f), new Vector3(BaseDepth, 0.06f, 0.8f), m.trim[t], false);

            // Parapets with gaps for the ramps: cover to fight from.
            float py = BaseHeight + ParapetHeight * 0.5f;
            float frontGap = FrontRampWidth * 0.5f + 1f;
            float frontLength = halfWidth - frontGap;
            foreach (float side in new[] { -1f, 1f }) {
                Cube("ParapetFront", root, At(halfDepth - ParapetThickness * 0.5f, py, side * (frontGap + frontLength * 0.5f)), new Vector3(ParapetThickness, ParapetHeight, frontLength), m.parapet[t]);
                float gapBack = SideRampF - SideRampWidth * 0.5f - 1f, gapFront = SideRampF + SideRampWidth * 0.5f + 1f;
                float backLength = gapBack + halfDepth, frontSide = halfDepth - gapFront;
                Cube("ParapetSideBack", root, At(-halfDepth + backLength * 0.5f, py, side * (halfWidth - ParapetThickness * 0.5f)), new Vector3(backLength, ParapetHeight, ParapetThickness), m.parapet[t]);
                Cube("ParapetSideFront", root, At(gapFront + frontSide * 0.5f, py, side * (halfWidth - ParapetThickness * 0.5f)), new Vector3(frontSide, ParapetHeight, ParapetThickness), m.parapet[t]);
            }

            // Back wall with team banners, the base sign and a team light.
            float backF = -halfDepth - 2f;
            Cube("BackWall", root, At(backF, BackWallHeight * 0.5f, 0f), new Vector3(1.5f, BackWallHeight, BaseWidth + 8f), m.parapet[t]);
            foreach (float r in new[] { -16f, 16f })
                Cube("Banner", root, At(backF + 0.9f, BaseHeight + 3.6f, r), new Vector3(0.15f, 6f, 4.5f), m.banner[t], false);
            var sign = WorldText("BaseSign", root, At(backF, BackWallHeight + 4f, 0f), $"{CTF_Rules.Name(team)} BASE", 8f, CTF_Rules.Color(team), SignBox, team == CTF_Team.Red ? -90f : 90f);
            sign.fontStyle = FontStyles.Bold;
            CTF_Authoring.FacingLabel(sign, SignBox.y, SignMaxScreen);
            var light = new GameObject("TeamLight", typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.position = At(0f, 14f, 0f);
            light.type = LightType.Point;
            light.color = CTF_Rules.Color(team);
            light.intensity = TeamLightIntensity;
            light.range = TeamLightRange;
            light.shadows = LightShadows.None;

            // Buildings behind the flag, sniper posts at the front corners.
            Prop(Military + "Building_Prefebs/MilitaryBase_Prefeb.prefab", root, At(-11f, BaseHeight, 12f), 11f, yaw + 90f, null, Solid.Mesh);
            Prop(Military + "Building_Prefebs/Radar_Prefeb.prefab", root, At(-11f, BaseHeight, -13f), 8f, yaw, null, Solid.Mesh);
            Prop(Military + "Building_Prefebs/GuardPoint_01_Prefeb.prefab", root, At(halfDepth - 3f, BaseHeight, halfWidth - 4f), 7f, yaw + 90f, null, Solid.Mesh);
            Prop(Military + "Building_Prefebs/GuardPoint_01_Prefeb.prefab", root, At(halfDepth - 3f, BaseHeight, -halfWidth + 4f), 7f, yaw + 90f, null, Solid.Mesh);

            // Flag dais and the point the server uses.
            Vector3 standAt = At(FlagF, BaseHeight, 0f);
            Primitive(PrimitiveType.Cylinder, "FlagStand", root, standAt + Vector3.up * 0.15f, new Vector3(StandRadius, 0.15f, StandRadius), m.trim[t]);
            var stand = Empty($"{CTF_Rules.Name(team)}FlagPoint", mapRoot, standAt + Vector3.up * 0.3f);

            int k = 0;
            foreach (float f in SpawnF)
                foreach (float r in SpawnR)
                    spawns.Add(Empty($"{CTF_Rules.Name(team)}Spawn{k++}", mapRoot, At(f, BaseHeight, r)).transform);
            return stand.transform;
        }

        // ---------------------------------------------------------------- centre and lanes
        /// <summary>The bridge over the mid lane: high ground in the middle, the speed power-up on top.</summary>
        static GameObject Centre(Transform world, Mats m) {
            var centre = Empty("Centre", world).transform;
            Deck("Bridge", centre, 0f, 0f, BridgeLength, BridgeWidth, BridgeHeight, m.concrete);
            Mirrored((p, y) => Ramp("BridgeRamp", centre, p, Quaternion.Euler(0f, y, 0f) * Vector3.left, BridgeRampRun, BridgeRampWidth, m.concrete),
                new Vector3(-BridgeLength * 0.5f, BridgeHeight, 0f), 0f);
            Mirrored((p, y) => Cube("Rail", centre, p, new Vector3(BridgeLength, RailHeight, 0.6f), m.sandbag),
                new Vector3(0f, BridgeHeight + RailHeight * 0.5f, BridgeWidth * 0.5f - 0.3f), 0f);
            var title = WorldText("Title", centre, new Vector3(0f, 22f, 0f), "FLAG WARS", 9f, new Color(1f, 0.85f, 0.25f), TitleBox);
            title.fontStyle = FontStyles.Bold;
            CTF_Authoring.FacingLabel(title, TitleBox.y, TitleMaxScreen);
            return title.gameObject;
        }

        static void MidLane(Transform world, Mats m) {
            var lane = Empty("MidLane", world).transform;
            Mirrored((p, y) => Sandbags(lane, p, 8f, y, m.sandbag), new Vector3(-44f, 0f, 6f), 0f);
            Mirrored((p, y) => Sandbags(lane, p, 8f, y, m.sandbag), new Vector3(-52f, 0f, -6f), 0f);
            Mirrored((p, y) => Sandbags(lane, p, 9f, y, m.sandbag), new Vector3(-60f, 0f, 9f), 90f);
            Mirrored((p, y) => Prop(Military + "Tank_Prefebs/Tank_01_Prefeb.prefab", lane, p, 10f, y, null, Solid.Mesh), new Vector3(-28f, 0f, 9f), 25f);
            Mirrored((p, y) => Crates(lane, p, y, m), new Vector3(-66f, 0f, -9f), 0f);
            Mirrored((p, y) => Crates(lane, p, y, m), new Vector3(-20f, 0f, -10f), 15f);
            Mirrored((p, y) => Prop(Proto + "Barrel_A.fbx", lane, p, 2.6f, y, m.proto, Solid.Box), new Vector3(-26f, 0f, -3f), 0f);
            Mirrored((p, y) => Prop(Proto + "Barrel_B.fbx", lane, p, 2.6f, y, m.proto, Solid.Box), new Vector3(-24.5f, 0f, -5f), 0f);
            // Full cover either side of the health pad (x = HealthPowerUpX) and at the mid-lane mouth.
            Mirrored((p, y) => BlastWall(lane, p, y, m), new Vector3(-48f, 0f, 11f), 0f);
            Mirrored((p, y) => BlastWall(lane, p, y, m), new Vector3(-40f, 0f, -9.5f), 0f);
            Mirrored((p, y) => BlastWall(lane, p, y, m), new Vector3(-36f, 0f, -1f), 90f);
        }

        static void NorthLane(Transform world, Mats m) {
            var lane = Empty("SideLanes", world).transform;
            // North half of red's side (mirrored to the south half of blue's).
            Mirrored((p, y) => Prop(Military + "Building_Prefebs/VehicleHangar_01_Prefeb.prefab", lane, p, 22f, y, null, Solid.Mesh), new Vector3(-50f, 0f, 60f), 0f);
            Mirrored((p, y) => Prop(Military + "Building_Prefebs/GuardPoint_02_Prefeb.prefab", lane, p, 8f, y, null, Solid.Mesh), new Vector3(-82f, 0f, 54f), 90f);
            Mirrored((p, y) => Prop(Space + "cargo_A_stacked.fbx", lane, p, 9f, y, m.space, Solid.Box), new Vector3(-70f, 0f, 35f), 0f);
            Mirrored((p, y) => Prop(Space + "containers_B.fbx", lane, p, 8f, y, m.space, Solid.Box), new Vector3(-8f, 0f, 36f), 90f);
            Mirrored((p, y) => Crates(lane, p, y, m), new Vector3(-62f, 0f, 50f), 30f);
            Mirrored((p, y) => Crates(lane, p, y, m), new Vector3(-38f, 0f, 39f), -10f);
            Mirrored((p, y) => BlastWall(lane, p, y, m), new Vector3(-72f, 0f, 49f), 0f);
            // Raised catwalk with a ramp toward the centre: height over the lane.
            const float catHeight = 4f;
            Mirrored((p, y) => Deck("Catwalk", lane, p.x, p.z, 20f, 8f, catHeight, m.concrete), new Vector3(-24f, 0f, 54f), 0f);
            Mirrored((p, y) => Ramp("CatwalkRamp", lane, p, Quaternion.Euler(0f, y, 0f) * Vector3.right, 10f, 6f, m.concrete), new Vector3(-14f, catHeight, 54f), 0f);
            Mirrored((p, y) => Cube("CatwalkRail", lane, p, new Vector3(20f, RailHeight, 0.6f), m.sandbag), new Vector3(-24f, catHeight + RailHeight * 0.5f, 57.7f), 0f);
        }

        static void SouthLane(Transform world, Mats m) {
            var lane = world.Find("SideLanes");
            // South half of red's side (mirrored to the north half of blue's).
            Mirrored((p, y) => Prop(Military + "Building_Prefebs/OilTank_Prefeb.prefab", lane, p, 12f, y, null, Solid.Mesh), new Vector3(-30f, 0f, -60f), 0f);
            Mirrored((p, y) => Prop(Military + "Building_Prefebs/OilTank_Prefeb.prefab", lane, p, 10f, y, null, Solid.Mesh), new Vector3(-46f, 0f, -62f), 0f);
            Mirrored((p, y) => Prop(Military + "APC_Prefebs/APC_01_Prefeb.prefab", lane, p, 9f, y, null, Solid.Mesh), new Vector3(-58f, 0f, -38f), 70f);
            Mirrored((p, y) => Prop(Space + "rocks_A.fbx", lane, p, 7f, y, m.space, Solid.Box), new Vector3(-18f, 0f, -40f), 40f);
            Mirrored((p, y) => Crates(lane, p, y, m), new Vector3(-36f, 0f, -48f), 0f);
            Mirrored((p, y) => Prop(Proto + "Barrel_C.fbx", lane, p, 2.6f, y, m.proto, Solid.Box), new Vector3(-50f, 0f, -34f), 0f);
            Mirrored((p, y) => BlastWall(lane, p, y, m), new Vector3(-42f, 0f, -38.5f), 0f);
            // Bunker roof with a ramp down onto the lane.
            const float bunkerHeight = 4f;
            Mirrored((p, y) => Deck("Bunker", lane, p.x, p.z, 14f, 10f, bunkerHeight, m.concrete), new Vector3(-67f, 0f, -57f), 0f);
            Mirrored((p, y) => Ramp("BunkerRamp", lane, p, Quaternion.Euler(0f, y, 0f) * Vector3.forward, 10f, 6f, m.concrete), new Vector3(-67f, bunkerHeight, -52f), 0f);
            Mirrored((p, y) => Sandbags(lane, p, 10f, y, m.sandbag), new Vector3(-67f, bunkerHeight, -61.5f), 0f);
        }

        /// <summary>Broken walls between the lanes: block long sight lines, leave gaps to rotate through.</summary>
        static void Dividers(Transform world, Mats m) {
            var dividers = Empty("Dividers", world).transform;
            float z = LaneZ * 0.5f;
            // North divider, red half.
            Mirrored((p, y) => Prop(Space + "containers_A.fbx", dividers, p, 10f, y, m.space, Solid.Box), new Vector3(-68f, 0f, z), 0f);
            Mirrored((p, y) => Prop(Space + "containers_C.fbx", dividers, p, 9f, y, m.space, Solid.Box), new Vector3(-57f, 0f, z + 1f), 0f);
            Mirrored((p, y) => Prop(Nature + "rock_single_A.fbx", dividers, p, 8f, y, m.medieval, Solid.Box), new Vector3(-36f, 0f, z - 1f), 20f);
            Mirrored((p, y) => Prop(Nature + "rock_single_B.fbx", dividers, p, 9f, y, m.medieval, Solid.Box), new Vector3(-28f, 0f, z + 1f), 75f);
            Mirrored((p, y) => Prop(Nature + "rock_single_C.fbx", dividers, p, 7f, y, m.medieval, Solid.Box), new Vector3(-20f, 0f, z - 1f), 140f);
            Mirrored((p, y) => Prop(Nature + "trees_A_medium.fbx", dividers, p, 10f, y, m.medieval, Solid.Trunk), new Vector3(-32f, 0f, z + 3f), 0f);
            // South divider, red half (different gaps, so the two dividers are not copies).
            Mirrored((p, y) => Prop(Space + "cargo_B_stacked.fbx", dividers, p, 9f, y, m.space, Solid.Box), new Vector3(-60f, 0f, -z), 90f);
            Mirrored((p, y) => Sandbags(dividers, p, 8f, y, m.sandbag), new Vector3(-50f, 0f, -z), 0f);
            Mirrored((p, y) => Prop(Space + "rocks_B.fbx", dividers, p, 9f, y, m.space, Solid.Box), new Vector3(-24f, 0f, -z), 10f);
            Mirrored((p, y) => Prop(Nature + "rock_single_D.fbx", dividers, p, 7f, y, m.medieval, Solid.Box), new Vector3(-14f, 0f, -z + 1f), 60f);
            Mirrored((p, y) => Prop(Nature + "trees_B_medium.fbx", dividers, p, 10f, y, m.medieval, Solid.Trunk), new Vector3(-18f, 0f, -z - 3f), 0f);
        }

        /// <summary>Trees in the grass beyond the walls and hills on the horizon (decoration, no colliders).</summary>
        static void Scenery(Transform world, Mats m) {
            var scenery = Empty("Scenery", world).transform;
            var random = new System.Random(11);
            string[] trees = { "trees_A_large.fbx", "trees_B_large.fbx", "tree_single_A.fbx", "tree_single_B.fbx" };
            const float step = 16f, band = 26f;
            for (float x = -HalfLength - band; x <= HalfLength + band; x += step)
                foreach (float side in new[] { -1f, 1f })
                    Tree(new Vector3(x, 0f, side * (HalfWidth + 8f + (float)random.NextDouble() * band)));
            for (float z = -HalfWidth; z <= HalfWidth; z += step)
                foreach (float side in new[] { -1f, 1f })
                    Tree(new Vector3(side * (HalfLength + 8f + (float)random.NextDouble() * band), 0f, z));

            void Tree(Vector3 at) {
                string model = trees[random.Next(trees.Length)];
                Prop(Nature + model, scenery, at, 12f + (float)random.NextDouble() * 8f, (float)random.NextDouble() * 360f, m.medieval, Solid.None);
            }

            string[] hills = { "mountain_A_grass_trees.fbx", "mountain_B_grass_trees.fbx", "mountain_C_grass_trees.fbx", "hills_A_trees.fbx" };
            var hillSpots = new[] {
                new Vector3(-215f, 0f, -90f), new Vector3(-220f, 0f, 10f), new Vector3(-210f, 0f, 110f),
                new Vector3(-110f, 0f, 165f), new Vector3(10f, 0f, 170f),
            };
            for (int i = 0; i < hillSpots.Length; i++) {
                int index = i;
                Mirrored((p, y) => Prop(Nature + hills[index % hills.Length], scenery, p, 85f, y, m.medieval, Solid.None), hillSpots[i], i * 47f);
            }
        }

        static CTF_PowerUp[] PowerUps(Transform mapRoot, GameObject[] prefabs) {
            var spots = new List<CTF_PowerUp>();
            Mirrored((p, y) => spots.Add(PowerUp(prefabs[(int)CTF_PowerUpKind.Health], mapRoot, p)), new Vector3(HealthPowerUpX, 0f, 0f), 0f);
            spots.Add(PowerUp(prefabs[(int)CTF_PowerUpKind.Speed], mapRoot, new Vector3(0f, BridgeHeight, 0f)));
            return spots.ToArray();
        }

        static CTF_PowerUp PowerUp(GameObject prefab, Transform parent, Vector3 position) {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = position;
            return go.GetComponent<CTF_PowerUp>();
        }
    }
}
