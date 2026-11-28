using System.Collections.Generic;
using System.IO;
using System.Linq;
using RyanAssets.Authoring;
using RyanAssets.Rounds;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static RyanAssets.Authoring.UniverseAuthoringKit;

namespace Universes.UniverseData.tower_defense.Editor {
    /// <summary>
    /// Rebuilds Tower Defense: materials, tower (one model per level) / placement ghost / enemy / pad /
    /// effect prefabs (Cartoon Military models on military helipads), the HUD
    /// (<see cref="TD_HudAuthoring"/>), the map (<see cref="TD_MapAuthoring"/>) and the start scene.
    /// Safe to re-run.
    /// Server runner: run BuildServerAssets from the Linux Server profile.
    /// </summary>
    public static class TD_Authoring {
        public const string Universe = "tower_defense";
        public const string Root = "Assets/Universes/UniverseData/tower_defense";
        public const string IconFolder = Root + "/UI/Icons";
        internal static readonly UniverseAuthoringKit Kit = new(Root);
        const string ServerPrefab = Root + "/Server/TD_ServerRunner.prefab";
        const string Military = "Assets/CartoonMilitaryModelPack/Prefebs/";
        public const string AudioFolder = Root + "/Audio";
        public const string FireClip = "Packages/com.ryan.gameframework/Runtime/Tools/Audio/pistol-gun-shot.mp3";
        const string Turrets = Military + "CustomPart_Prefebs/Turrets_Prefebs/";

        /// <summary>
        /// Turret model per level (index 0 = level 1) and plinth-relative size per tower, index-aligned
        /// with <see cref="TD_TowerKind"/>. Every upgrade swaps to a different, heavier model (and adds a
        /// trim band, see <see cref="TrimBands"/>), so each level reads at a glance. A bare name is a
        /// turret part; a name with a folder is any Cartoon Military prefab (the Tesla's masts and the
        /// Artillery's vehicles: every turret part is already taken by the first four towers).
        /// </summary>
        static readonly (string[] models, float size)[] TowerModels = {
            (new[] { "Turret_08_Prefeb", "Turret_09_Prefeb", "Turret_07_Prefeb" }, 4.4f), // Gunner: gatling, rifle, armoured gatling
            (new[] { "Turret_12_Prefeb", "Turret_06_Prefeb", "Turret_01_Prefeb" }, 4.8f), // Cannon: low triple gun, armoured gun, twin pods
            (new[] { "Turret_10_Prefeb", "Turret_13_Prefeb", "Turret_11_Prefeb" }, 4.6f), // Frost: emitter, twin prong, cryo tank
            (new[] { "Turret_04_Prefeb", "Turret_02_Prefeb", "Turret_03_Prefeb" }, 5.2f), // Sniper: long gun, sensor gun, heavy battery
            (new[] { "Building_Prefebs/GuardPoint_01_Prefeb", "Building_Prefebs/GuardPoint_03_Prefeb", "Building_Prefebs/GuardPoint_02_Prefeb" }, 4.6f), // Tesla: stilt tower, tall tower, armoured tower (a coil orb on top)
            (new[] { "SelfPropelledArtillery_Prefebs/SelfPropelledArtillery_01_Prefeb", "SelfPropelledArtillery_Prefebs/SelfPropelledArtillery_02_Prefeb", "MLRS_Prefebs/MLRS_01_Prefeb" }, 6f), // Artillery: light gun, heavy gun, rocket battery (team-painted, see TeamPaint)
        };

        /// <summary>The Tesla's glowing coil orb: size (m) and how far above the mast's top it floats.</summary>
        const float CoilSize = 1.1f * Visual, CoilLift = 0.4f * Visual;
        /// <summary>Gold Trim: a ring of this radius (fraction of the pad) around the tower's foot.</summary>
        const float GoldTrimRadius = 0.46f;

        /// <summary>How far each level's paint leans toward the tower's colour (index 0 = level 1; 0 = the model's own colours).</summary>
        static readonly float[] LevelPaint = { 0f, 0.38f, 0.6f };
        /// <summary>Frost towers keep their textures but are always cooled toward frost blue, from level 1.</summary>
        const float FrostBasePaint = 0.3f;
        /// <summary>Faint glow of a frost tower's paint (an HDR multiple of its colour).</summary>
        const float FrostGlow = 0.12f;
        /// <summary>
        /// The Artillery's models are army vehicles like the enemies (green and desert tan), so every level
        /// is repainted in the tower's own blue from level 1: its textures are swapped for desaturated
        /// copies (<see cref="TeamTextureFolder"/>) and painted this strongly, with a faint glow.
        /// </summary>
        const float TeamPaint = 0.8f, TeamGlow = 0.1f;
        /// <summary>Greyscale copies of the pack textures the Artillery uses, named "{texture}_Team.png".</summary>
        const string TeamTextureFolder = Root + "/Textures/Team";
        /// <summary>Plinth plates in the tower's colour under the turret, per level (index 0 = level 1): the colour says which tower, the count which level.</summary>
        static readonly int[] TrimBands = { 1, 2, 3 };
        /// <summary>Height of one plinth plate under a turret, in metres.</summary>
        const float PlateHeight = 0.16f;
        /// <summary>Plinth radius as a share of the pad's width: the top of the stone tower it sits on.</summary>
        const float PlinthWidth = 0.34f;
        /// <summary>Extra fit size for models whose long barrel makes them read small at the tower's size.</summary>
        static readonly Dictionary<string, float> ModelFit = new() {
            ["Turret_04_Prefeb"] = 1.4f, ["Turret_01_Prefeb"] = 1.1f,
            ["Building_Prefebs/GuardPoint_03_Prefeb"] = 1.2f, ["Building_Prefebs/GuardPoint_02_Prefeb"] = 1.3f,
            ["SelfPropelledArtillery_Prefebs/SelfPropelledArtillery_02_Prefeb"] = 1.05f, ["MLRS_Prefebs/MLRS_01_Prefeb"] = 1.1f,
        };
        /// <summary>Gap in metres between a turret's top and its level stars.</summary>
        const float BadgeGap = 1f;
        /// <summary>Uniform tower scale per level (index 0 = level 1).</summary>
        static readonly float[] LevelScale = { 1f, 1.12f, 1.24f };

        /// <summary>Vehicle model and size per enemy, index-aligned with <see cref="TD_EnemyKind"/>.</summary>
        static readonly (string model, float size)[] EnemyModels = {
            ("APC_Prefebs/APC_01_Prefeb.prefab", 3.6f), ("APC_Prefebs/APC_03_Prefeb.prefab", 4.6f), ("Tank_Prefebs/Tank_01_Prefeb.prefab", 5.2f),
            ("Chopper_Prefebs/Chopper_01_Prefeb.prefab", 6f), ("Tank_Prefebs/Tank_05_Prefeb.prefab", 8.5f),
            ("Tank_Prefebs/Tank_03_Prefeb.prefab", 5.6f), ("APC_Prefebs/APC_02_Prefeb.prefab", 4.8f),
            ("HoverCraft_Prefeb/HoverCraft_Prefeb.prefab", 6.8f), ("UAV_Prefebs/UAV_01_Prefeb.prefab", 2.8f),
        };

        /// <summary>The Airstrike jet (flies along +Z over the road) and its length in metres.</summary>
        const string JetModel = "Jet_Prefebs/Jet_02_Prefeb.prefab";
        const float JetSize = 11f * Visual;
        /// <summary>Points of a Tesla bolt (more = finer kinks).</summary>
        const int ArcPoints = 12;

        /// <summary>Width of a tower pad (the helipad model) in metres.</summary>
        const float PadSize = 3.6f;
        /// <summary>Models (towers, enemies, the jet) are drawn at this share of their design size so they fit the smaller battlefield and its cells.</summary>
        const float Visual = 0.7f;

        [MenuItem("Ryan/Tower Defense/Build Client Assets")]
        public static void BuildClientAssets() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            Kit.EnsureFolders("Prefabs", "Prefabs/Towers", "Prefabs/Ghosts", "Prefabs/Enemies", "Prefabs/Fx", "Materials", "Materials/Tiers", "Textures", "Meshes", "Server", "UI", "UI/Portraits", "Audio");
            TD_HudAuthoring.ImportIcons();
            BuildTextures();
            BuildScene();
            RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));
            AssetDatabase.SaveAssets();
            Debug.Log("[TowerDefense] Client assets built.");
        }

        /// <summary>Batch entry point: builds everything, then (with a GPU) portraits and previews.</summary>
        public static void BuildClientAssetsBatch() {
            BuildClientAssets();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            TD_Portraits.RenderAll();
            BuildClientAssets(); // re-generate the HUD with the fresh portraits
            RenderPreviews();
        }

        [MenuItem("Ryan/Tower Defense/Build Server Assets (Linux Server profile)")]
        public static void BuildServerAssets() {
#if UNITY_SERVER
            Kit.EnsureFolders("Server");
            Kit.BuildServerRunner<Universes.UniverseData.tower_defense.Server.TD_ServerRunner>(ServerPrefab, Kit.ServerScene(Universe));
            RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));
            Debug.Log("[TowerDefense] Server assets built.");
#else
            throw new System.InvalidOperationException("Switch to the Linux Server build profile (UNITY_SERVER) to build the server runner prefab.");
#endif
        }

        // ---------------------------------------------------------------- textures + materials
        static void BuildTextures() {
            // Worn dirt road: warm packed earth in soft blotches, two wheel ruts, scattered round pebbles and
            // darker shoulders. Tiles along V (the road's length), so every noise below wraps at the edge.
            const int Size = 256;
            Kit.SaveTexture("Road", (u, v) => {
                float blotch = TileNoise(u, v, 6) * 0.6f + TileNoise(u, v, 18) * 0.3f + TileNoise(u, v, 64) * 0.1f;
                float grain = Hash(Mathf.Floor(u * Size), Mathf.Floor(v * Size));
                float wobble = (TileNoise(0.37f, v, 10) - 0.5f) * 0.05f;
                float rutDistance = Mathf.Abs(Mathf.Abs(u - 0.5f + wobble) - 0.21f);
                float rut = 1f - 0.16f * (1f - Mathf.SmoothStep(0.02f, 0.07f, rutDistance));
                float shoulder = Mathf.SmoothStep(0.72f, 1f, Mathf.Abs(u - 0.5f) * 2f);
                float c = (0.84f + 0.16f * blotch + 0.05f * (grain - 0.5f)) * rut * (1f - 0.22f * shoulder);
                var dirt = new Color(0.84f * c, 0.68f * c, 0.49f * c);
                float pebble = Pebble(u, v, 14);
                return pebble > 0f ? Color.Lerp(dirt, new Color(0.62f, 0.56f, 0.48f) * (0.85f + 0.25f * pebble), Mathf.Clamp01(pebble * 2.5f) * 0.8f) : dirt;
            }, Size);
            Kit.SaveTexture("Blob", (u, v) => {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                return new Color(0f, 0f, 0f, Mathf.Clamp01(1f - d) * 0.55f);
            });
            Kit.SaveTexture("Hazard", (u, v) => Mathf.Repeat(u + v, 0.5f) < 0.25f ? new Color(1f, 0.8f, 0.15f) : new Color(0.12f, 0.12f, 0.14f));
            foreach (string name in new[] { "Blob" }) {
                var importer = (TextureImporter)AssetImporter.GetAtPath($"{Root}/Textures/{name}.png");
                if (importer == null || importer.alphaIsTransparency) continue;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        /// <summary>Smooth value noise on a <paramref name="cells"/>-cell grid that wraps at 0 and 1 in both axes (0..1).</summary>
        static float TileNoise(float u, float v, int cells) {
            float x = Mathf.Repeat(u, 1f) * cells, y = Mathf.Repeat(v, 1f) * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float Corner(int i, int j) => Hash((x0 + i) % cells + 0.5f, (y0 + j) % cells + 0.5f);
            return Mathf.Lerp(Mathf.Lerp(Corner(0, 0), Corner(1, 0), fx), Mathf.Lerp(Corner(0, 1), Corner(1, 1), fx), fy);
        }

        /// <summary>
        /// A round pebble in some of the cells of a <paramref name="cells"/> grid (wrapping): 0 outside one,
        /// rising to 1 at its centre.
        /// </summary>
        static float Pebble(float u, float v, int cells) {
            float x = Mathf.Repeat(u, 1f) * cells, y = Mathf.Repeat(v, 1f) * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            if (Hash(cx + 11.3f, cy + 4.1f) < 0.72f) return 0f;
            float px = 0.25f + 0.5f * Hash(cx + 2.7f, cy + 9.9f), py = 0.25f + 0.5f * Hash(cx + 6.1f, cy + 1.3f);
            float radius = 0.09f + 0.1f * Hash(cx + 3.3f, cy + 7.7f);
            float d = Vector2.Distance(new Vector2(x - cx, y - cy), new Vector2(px, py));
            return Mathf.Clamp01(1f - d / radius);
        }

        /// <summary>The KayKit atlas on URP Lit with a tint, used to pull the pack's lime grass toward a deeper green.</summary>
        internal static Material Tinted(string name, string atlasPath, Color tint) {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            return Kit.Mat(name, tint, atlas, smoothness: 0.08f);
        }

        /// <summary>Unlit (optionally transparent) URP material: health bars, glows, icons on quads.</summary>
        /// <paramref name="vertexColors"/>: use the Particles/Unlit shader, which multiplies in vertex colours
        /// (URP/Unlit ignores them), so a LineRenderer's start/end colour tints it.
        internal static Material Unlit(string name, Color color, Texture texture = null, bool transparent = false, bool additive = false, bool vertexColors = false) {
            string path = $"{Root}/Materials/{name}.mat";
            var shader = Shader.Find(additive || vertexColors ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Unlit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", texture);
            bool blend = transparent || additive;
            mat.SetFloat("_Surface", blend ? 1f : 0f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", blend ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : blend ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", blend ? 0f : 1f);
            mat.SetOverrideTag("RenderType", blend ? "Transparent" : "Opaque");
            mat.renderQueue = blend ? (int)RenderQueue.Transparent : -1;
            if (blend) mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal sealed class Materials {
            public Material barBack, barFill, frost, blob, star, marker, ring, rangeRing, bossRing, tracer, projectile, ghost;
            public Material shield, shieldFill, healRing, gold, coil;
            public Material[] trims;
            /// <summary>Painted (barely glowing) tower-colour plates under the turrets, per <see cref="TD_TowerKind"/>.</summary>
            public Material[] plinths;
        }

        static Materials BuildMaterials() {
            return new Materials {
                barBack = Unlit("BarBack", new Color(0.06f, 0.07f, 0.1f, 0.85f), transparent: true),
                barFill = Unlit("BarFill", Color.white),
                frost = Kit.Mat("FrostShell", new Color(0.6f, 0.88f, 1f), emission: 0.35f, alpha: 0.2f, smoothness: 0.9f),
                blob = Unlit("ShadowBlob", Color.white, Kit.Tex("Blob"), transparent: true),
                star = Unlit("Star", new Color(1f, 0.82f, 0.25f), TD_HudAuthoring.IconTexture("star"), transparent: true),
                marker = Unlit("BuildMarker", new Color(1f, 1f, 1f, 0.9f), TD_HudAuthoring.IconTexture("build"), transparent: true),
                ring = Unlit("RangeRing", Color.white, additive: true),
                // Range previews blend instead of adding, so the red "cannot afford" ring stays red on grass.
                rangeRing = Unlit("RangePreview", Color.white, transparent: true, vertexColors: true),
                bossRing = Unlit("BossRing", new Color(0.95f, 0.12f, 0.1f, 0.85f), transparent: true),
                tracer = Unlit("Tracer", Color.white, additive: true),
                projectile = Kit.Mat("Projectile", Color.white, emission: 2f),
                ghost = Unlit("Ghost", new Color(1f, 1f, 1f, 0.45f), transparent: true),
                shield = Kit.Mat("ShieldBubble", TD_Palette.Shield, emission: 0.5f, alpha: 0.22f, smoothness: 0.95f),
                shieldFill = Unlit("ShieldFill", TD_Palette.Shield),
                healRing = Unlit("HealRing", new Color(TD_Palette.Heal.r, TD_Palette.Heal.g, TD_Palette.Heal.b, 0.45f), transparent: true),
                gold = Kit.Mat("GoldTrim", TD_Palette.Star, emission: 0.8f, smoothness: 0.7f),
                coil = Kit.Mat("TeslaCoil", TD_Palette.Tower(TD_TowerKind.Tesla), emission: 1.1f, smoothness: 0.6f),
                trims = System.Enum.GetValues(typeof(TD_TowerKind)).Cast<TD_TowerKind>()
                    .Select(k => Kit.Mat("Trim" + k, TD_Palette.Tower(k), emission: 0.6f, smoothness: 0.5f)).ToArray(),
                plinths = System.Enum.GetValues(typeof(TD_TowerKind)).Cast<TD_TowerKind>()
                    .Select(k => Kit.Mat("Plinth" + k, TD_Palette.Tower(k), emission: 0.12f, smoothness: 0.35f)).ToArray(),
            };
        }

        // ---------------------------------------------------------------- prefab pieces
        static GameObject IconQuad(string name, Transform parent, Vector3 position, float size, Material material) {
            var quad = Primitive(PrimitiveType.Quad, name, parent, position, Vector3.one * size, material, false);
            NoShadows(quad);
            return quad;
        }

        static LineRenderer Ring(Transform parent, Material material, string name = "RangeRing") {
            var go = Empty(name, parent);
            var ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.positionCount = 72;
            ring.widthMultiplier = 0.3f;
            ring.sharedMaterial = material;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.SetActive(false);
            return ring;
        }

        /// <summary>A flat ring of <paramref name="radius"/> metres in the parent's space (pad glows).</summary>
        static LineRenderer FlatRing(string name, Transform parent, float radius, float height, Material material, int points = 48) {
            var go = Empty(name, parent);
            var ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = points;
            for (int i = 0; i < points; i++) {
                float a = i * Mathf.PI * 2f / points;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
            }
            ring.widthMultiplier = 0.22f;
            ring.sharedMaterial = material;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.alignment = LineAlignment.View;
            return ring;
        }

        /// <summary>Concrete helipad from the military pack with a glow ring, a floating build marker and a range preview.</summary>
        static GameObject Slot(Materials m) {
            var root = new GameObject("TowerSlot");
            var view = root.AddComponent<TD_SlotView>();
            var pad = Model("Assets/ThirdParty/KayKit/MedievalHexagonPack/buildings/blue/building_tower_base_blue.fbx", root.transform, PadSize, keepColliders: false);
            float top = pad != null ? RendererBounds(pad).max.y : 0.3f;
            if (pad == null) Debug.LogWarning("[TowerDefense] KayKit tower base missing; the tower pad has no model.");
            view.topHeight = top;
            view.padModel = pad;
            view.glow = FlatRing("Glow", root.transform, PadSize * 0.4f, top + 0.04f, m.ring);
            var marker = Empty("Marker", root.transform, new Vector3(0f, 2.6f, 0f));
            marker.AddComponent<Billboard>();
            IconQuad("Icon", marker.transform, Vector3.zero, 1.6f, m.marker);
            view.marker = marker.transform;
            view.rangeRing = Ring(root.transform, m.rangeRing);
            return Kit.Save(root, "", "TowerSlot");
        }

        /// <summary>
        /// A Cartoon Military turret per level (placed on a pad's top by the client; only the current
        /// level's model is active), level stars, owner label and the current / next-level range rings.
        /// </summary>
        static GameObject Tower(TD_TowerKind kind, Materials m) {
            var stats = TD_Rules.Tower(kind);
            var (models, size) = TowerModels[(int)kind];
            size *= Visual;
            var root = new GameObject(FileName(kind));
            var view = root.AddComponent<TD_TowerView>();
            var head = Empty("Head", root.transform);
            view.head = head.transform;
            view.tiers = new GameObject[TD_Rules.MaxLevel];
            view.levelScale = LevelScale.ToArray();
            view.badgeHeight = new float[TD_Rules.MaxLevel];
            var tierBounds = new Vector3[TD_Rules.MaxLevel];
            for (int level = 1; level <= TD_Rules.MaxLevel; level++) {
                var tier = Empty($"Tier{level}", head.transform);
                view.tiers[level - 1] = tier;
                string model = models[Mathf.Min(level, models.Length) - 1];
                var turret = TurretModel(kind, model, tier.transform, size, m);
                if (turret == null) continue;
                tierBounds[level - 1] = RendererBounds(turret).size * LevelScale[level - 1];
                Paint(turret, kind, level);
                Plinth(turret, tier.transform, kind, level, m);
                // Stars sit just above this level's own model, not at a height shared by all levels.
                view.badgeHeight[level - 1] = RendererBounds(turret).max.y - root.transform.position.y + BadgeGap;
                if (level == 1) {
                    var b = RendererBounds(turret);
                    var box = root.AddComponent<BoxCollider>();
                    box.center = b.center;
                    box.size = b.size;
                }
                tier.SetActive(level == 1);
            }
            CheckTiers(kind, tierBounds);
            view.muzzle = Empty("Muzzle", head.transform, new Vector3(0f, size * 0.45f, size * 0.55f)).transform;
            if (kind == TD_TowerKind.Tesla) {
                // Bolts leave from a glowing coil floating on top of each level's mast (its own muzzle),
                // and the level stars move up above the coil.
                view.tierMuzzles = new Transform[TD_Rules.MaxLevel];
                for (int level = 1; level <= TD_Rules.MaxLevel; level++) {
                    float top = view.badgeHeight[level - 1] > 0f ? view.badgeHeight[level - 1] - BadgeGap : size;
                    var coil = Primitive(PrimitiveType.Sphere, "Coil", view.tiers[level - 1].transform, new Vector3(0f, top + CoilLift + CoilSize * 0.5f, 0f), Vector3.one * CoilSize, m.coil, false);
                    NoShadows(coil);
                    view.tierMuzzles[level - 1] = coil.transform;
                    view.badgeHeight[level - 1] = top + CoilLift + CoilSize + BadgeGap;
                }
            }

            // Gold Trim unlock: a gold ring at the tower's foot (the client shows it for its owners' towers).
            var trim = FlatRing("GoldTrim", root.transform, PadSize * GoldTrimRadius, 0.12f, m.gold, 56);
            trim.widthMultiplier = 0.35f;
            trim.gameObject.SetActive(false);
            view.goldTrim = trim.gameObject;

            // Level stars only (the builder's name is on the info card): flat to the camera plane and
            // never taller than a few percent of the screen; the client hides them near the camera and
            // the screen's top and side edges.
            var badge = Empty("Badge", root.transform, new Vector3(0f, view.badgeHeight[0] > 0f ? view.badgeHeight[0] : size + 1.6f, 0f));
            var billboard = badge.AddComponent<Billboard>();
            billboard.facing = Billboard.Facing.CameraPlane;
            billboard.yawOnly = false;
            billboard.maxScreenHeight = 0.035f;
            billboard.referenceHeight = 0.8f;
            view.badge = badge.transform;
            view.levelStars = new GameObject[TD_Rules.MaxLevel];
            for (int i = 0; i < TD_Rules.MaxLevel; i++)
                view.levelStars[i] = IconQuad($"Star{i}", badge.transform, new Vector3((i - (TD_Rules.MaxLevel - 1) * 0.5f) * 0.85f, 0f, 0f), 0.8f, m.star);

            view.rangeRing = Ring(root.transform, m.rangeRing);
            view.nextRangeRing = Ring(root.transform, m.rangeRing, "NextRangeRing");
            view.nextRangeRing.widthMultiplier = 0.3f;
            return Kit.Save(root, "Towers", FileName(kind));
        }

        /// <summary>
        /// An upgrade must never look like a downgrade: each level's turret (with its level scale) must be at
        /// least as tall and as big (bounds volume) as the level below. Logs every tier's size; an error names
        /// the offending level so a model swap in <see cref="TowerModels"/> cannot slip through.
        /// </summary>
        static void CheckTiers(TD_TowerKind kind, Vector3[] sizes) {
            static float Volume(Vector3 s) => s.x * s.y * s.z;
            Debug.Log($"[TowerDefense] {kind} tier bounds: " + string.Join(", ", sizes.Select((s, i) => $"L{i + 1} height {s.y:0.00} m, volume {Volume(s):0} m3")));
            for (int i = 1; i < sizes.Length; i++)
                if (sizes[i].y < sizes[i - 1].y || Volume(sizes[i]) < Volume(sizes[i - 1]))
                    Debug.LogError($"[TowerDefense] {kind} level {i + 1} turret is smaller than level {i} (height {sizes[i].y:0.00} vs {sizes[i - 1].y:0.00} m, volume {Volume(sizes[i]):0} vs {Volume(sizes[i - 1]):0} m3). Pick a bigger model or add a ModelFit entry.");
        }

        /// <summary>
        /// The asset name of a tower's prefab (and its ghost): the kind's enum name, not its display name, so
        /// renaming a tower (Mortar to Artillery) keeps its files and their references.
        /// </summary>
        internal static string FileName(TD_TowerKind kind) => kind.ToString();

        /// <summary>The turret model for one level, with the pack's own textured materials.</summary>
        static string ModelPath(string model) => model.Contains('/') ? Military + model + ".prefab" : Turrets + model + ".prefab";

        static GameObject TurretModel(TD_TowerKind kind, string model, Transform parent, float size, Materials m) {
            var turret = Model(ModelPath(model), parent, size * (ModelFit.TryGetValue(model, out float fit) ? fit : 1f));
            if (turret == null) Debug.LogWarning($"[TowerDefense] Turret model {model} missing; {TD_Rules.Tower(kind).name} has no model at this level.");
            return turret;
        }

        /// <summary>
        /// Repaints a turret for its level: each material's colour leans toward the tower's colour (copies
        /// in Materials/Tiers; textures kept). Frost leans blue from level 1 and glows faintly.
        /// </summary>
        static void Paint(GameObject turret, TD_TowerKind kind, int level) {
            float amount = LevelPaint[level - 1];
            float glow = 0f;
            bool team = kind == TD_TowerKind.Mortar;
            if (kind == TD_TowerKind.Frost) {
                amount = Mathf.Max(amount, FrostBasePaint);
                glow = FrostGlow;
            }
            if (team) {
                amount = Mathf.Max(amount, TeamPaint);
                glow = TeamGlow;
            }
            if (amount <= 0f) return;
            Color paint = Color.Lerp(Color.white, TD_Palette.Tower(kind), amount);
            Color emission = TD_Palette.Tower(kind) * glow;
            foreach (var r in turret.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(source => source == null ? null
                    : Painted(source, $"{kind}_L{level}_{source.name}", paint, emission, team ? TeamTexture(source) : null)).ToArray();
        }

        /// <summary>The desaturated copy of <paramref name="source"/>'s main texture in <see cref="TeamTextureFolder"/>, or null (a warning: the model keeps its army colours).</summary>
        static Texture TeamTexture(Material source) {
            var texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture;
            if (texture == null) return null;
            var team = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TeamTextureFolder}/{texture.name}_Team.png");
            if (team == null) Debug.LogWarning($"[TowerDefense] No team texture for {texture.name} in {TeamTextureFolder}; the Artillery keeps its army colours there.");
            return team;
        }

        /// <summary>
        /// A stepped plinth in the tower's colour under the turret, <see cref="TrimBands"/> plates per level
        /// (1/2/3, each a little narrower): the colour says which tower and the steps which level, and the
        /// turret stands on it instead of hovering inside floating rings. Lifts the turret by its height.
        /// </summary>
        static void Plinth(GameObject turret, Transform tier, TD_TowerKind kind, int level, Materials m) {
            int count = TrimBands[level - 1];
            if (count <= 0) return;
            var b = RendererBounds(turret);
            // As wide as the stone tower's top at every level (the whole tower grows with its level scale).
            float radius = PadSize * PlinthWidth / LevelScale[level - 1];
            float bottom = b.min.y - tier.position.y;
            for (int i = 0; i < count; i++) {
                float r = radius * (1f - i * 0.12f);
                var plate = Primitive(PrimitiveType.Cylinder, $"Plinth{i + 1}", tier, new Vector3(0f, bottom + (i + 0.5f) * PlateHeight, 0f),
                    new Vector3(r * 2f, PlateHeight * 0.5f, r * 2f), m.plinths[(int)kind], false);
                NoShadows(plate);
            }
            turret.transform.localPosition += Vector3.up * (count * PlateHeight);
        }

        static Material Painted(Material source, string name, Color paint, Color emission = default, Texture baseMap = null) {
            string path = $"{Root}/Materials/Tiers/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                mat = new Material(source);
                AssetDatabase.CreateAsset(mat, path);
            } else {
                mat.shader = source.shader;
                mat.CopyPropertiesFromMaterial(source);
            }
            foreach (string property in new[] { "_BaseColor", "_Color" })
                if (source.HasProperty(property)) {
                    mat.SetColor(property, source.GetColor(property) * paint);
                    break;
                }
            if (baseMap != null)
                foreach (string property in new[] { "_BaseMap", "_MainTex" })
                    if (mat.HasProperty(property)) mat.SetTexture(property, baseMap);
            if (emission.maxColorComponent > 0f && mat.HasProperty("_EmissionColor")) {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                mat.SetColor("_EmissionColor", emission);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A see-through copy of a tower's level-1 model, previewed on a pad before building (tinted by the client).</summary>
        static GameObject Ghost(TD_TowerKind kind, Materials m) {
            var (models, size) = TowerModels[(int)kind];
            size *= Visual;
            var root = new GameObject(FileName(kind) + " Ghost");
            var turret = TurretModel(kind, models[0], root.transform, size, m);
            if (turret != null) {
                foreach (var r in turret.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => m.ghost).ToArray();
                NoShadows(turret);
            }
            return Kit.Save(root, "Ghosts");
        }

        /// <summary>How each enemy travels: wheels, tracks, rotors, a hovercraft cushion or a drone's buzz.</summary>
        static TD_EnemyAnimator.Style AnimationStyle(TD_EnemyKind kind) => kind switch {
            TD_EnemyKind.Scout or TD_EnemyKind.Apc or TD_EnemyKind.Medic => TD_EnemyAnimator.Style.Wheeled,
            TD_EnemyKind.Tank or TD_EnemyKind.Boss or TD_EnemyKind.Shield => TD_EnemyAnimator.Style.Tracked,
            TD_EnemyKind.Chopper => TD_EnemyAnimator.Style.Helicopter,
            TD_EnemyKind.Carrier => TD_EnemyAnimator.Style.Hover,
            _ => TD_EnemyAnimator.Style.Drone,
        };

        static GameObject Enemy(TD_EnemyKind kind, Materials m) {
            var stats = TD_Rules.Enemy(kind);
            var (model, size) = EnemyModels[(int)kind];
            size *= Visual;
            var root = new GameObject(stats.name);
            var view = root.AddComponent<TD_EnemyView>();
            var body = Empty("Body", root.transform);
            view.body = body.transform;
            if (Model(Military + model, body.transform, size) == null)
                Cube("Hull", body.transform, new Vector3(0f, size * 0.25f, 0f), new Vector3(size * 0.5f, size * 0.4f, size), m.barFill, false);
            NoShadows(body);
            view.bodyRenderers = body.GetComponentsInChildren<Renderer>();
            var animator = root.AddComponent<TD_EnemyAnimator>();
            animator.body = body.transform;
            animator.style = AnimationStyle(kind);
            animator.wheelTurnsPerMetre = 1f / (Mathf.PI * size * 0.16f); // wheel diameter is about a sixth of the vehicle's length
            animator.rockHeight = size * 0.012f;
            animator.hoverHeight = size * 0.035f;
            view.animator = animator;

            // Soft contact shadow (on the ground even for flying enemies).
            var blob = Primitive(PrimitiveType.Quad, "Shadow", root.transform, new Vector3(0f, (stats.flying ? -TD_Rules.FlyHeight : 0f) + 0.06f, 0f), new Vector3(size * 0.8f, size * 0.8f, 1f), m.blob, false);
            blob.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            NoShadows(blob);

            float barWidth = Mathf.Clamp(size * 0.5f, 2.2f, 4f);
            var bar = Empty("HealthBar", root.transform, new Vector3(0f, size * 0.7f + 1.2f, 0f));
            // Flat to the camera plane (readable from the steep build camera too) and never a wide
            // stripe across the screen when an enemy passes right by the camera.
            var barFacing = bar.AddComponent<Billboard>();
            barFacing.facing = Billboard.Facing.CameraPlane;
            barFacing.yawOnly = false;
            barFacing.maxScreenHeight = 0.02f;
            barFacing.referenceHeight = 0.42f;
            NoShadows(Cube("Back", bar.transform, Vector3.zero, new Vector3(barWidth + 0.16f, 0.42f, 0.02f), m.barBack, false));
            var pivot = Empty("Pivot", bar.transform, new Vector3(-barWidth * 0.5f, 0f, -0.03f));
            var fill = Cube("Fill", pivot.transform, new Vector3(barWidth * 0.5f, 0f, 0f), new Vector3(barWidth, 0.26f, 0.02f), m.barFill, false);
            NoShadows(fill);
            view.healthFill = pivot.transform;
            view.healthRenderer = fill.GetComponent<Renderer>();

            if (stats.shield > 0f) {
                // Shield: a translucent bubble around the hull and a thin cyan bar above the health bar.
                var bubble = Primitive(PrimitiveType.Sphere, "ShieldBubble", root.transform, new Vector3(0f, size * 0.28f, 0f), Vector3.one * size * 1.05f, m.shield, false);
                NoShadows(bubble);
                view.shieldBubble = bubble;
                var shieldBar = Empty("ShieldBar", bar.transform, new Vector3(-barWidth * 0.5f, 0.32f, -0.03f));
                var shieldFill = Cube("Fill", shieldBar.transform, new Vector3(barWidth * 0.5f, 0f, 0f), new Vector3(barWidth, 0.16f, 0.02f), m.shieldFill, false);
                NoShadows(shieldFill);
                view.shieldFill = shieldBar.transform;
            }
            if (stats.heal > 0f) {
                // Medic: its repair radius drawn on the ground in heal green, so players see what to kill first.
                var heal = FlatRing("HealRing", root.transform, TD_Rules.HealRadius, 0.18f, m.healRing, 72);
                heal.widthMultiplier = 0.3f;
            }

            if (TD_Rules.IsBoss(kind)) {
                // Boss marker: a red ring on the ground around the heavy tank, readable from above.
                var ring = FlatRing("BossRing", root.transform, size * 0.62f, 0.2f, m.bossRing, 64);
                ring.widthMultiplier = 0.45f;
            }

            var ice = Primitive(PrimitiveType.Sphere, "FrostShell", root.transform, new Vector3(0f, size * 0.25f, 0f), Vector3.one * size * 0.9f, m.frost, false);
            NoShadows(ice);
            ice.SetActive(false);
            view.slowTint = ice.GetComponent<Renderer>();
            return Kit.Save(root, "Enemies", stats.name);
        }

        // ---------------------------------------------------------------- effects
        static TD_EffectSet BuildEffects(Materials m) {
            var tracer = new GameObject("Tracer");
            var line = tracer.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.widthMultiplier = 0.12f;
            line.sharedMaterial = m.tracer;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.numCapVertices = 3;

            var projectile = Primitive(PrimitiveType.Sphere, "Projectile", null, Vector3.zero, Vector3.one, m.projectile, false);
            NoShadows(projectile);
            var trail = projectile.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.widthMultiplier = 0.5f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.sharedMaterial = m.tracer;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.minVertexDistance = 0.2f;

            var arc = new GameObject("TeslaArc");
            var bolt = arc.AddComponent<LineRenderer>();
            bolt.positionCount = ArcPoints;
            bolt.useWorldSpace = true;
            bolt.widthMultiplier = 0.28f;
            bolt.sharedMaterial = m.tracer;
            bolt.shadowCastingMode = ShadowCastingMode.Off;
            bolt.numCapVertices = 2;
            bolt.numCornerVertices = 1;

            var jet = new GameObject("AirstrikeJet");
            if (Model(Military + JetModel, jet.transform, JetSize) == null) Debug.LogWarning("[TowerDefense] Jet model missing; the Airstrike shows bombs without a jet.");

            return new TD_EffectSet {
                tracer = Kit.Save(tracer, "Fx", "Tracer"),
                projectile = Kit.Save(projectile, "Fx", "Projectile"),
                arc = Kit.Save(arc, "Fx", "TeslaArc"),
                jet = jet.transform.childCount > 0 ? Kit.Save(jet, "Fx", "AirstrikeJet") : null,
            };
        }

        // ---------------------------------------------------------------- scene
        static void BuildScene() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var m = BuildMaterials();
            var slot = Slot(m);
            var towers = System.Enum.GetValues(typeof(TD_TowerKind)).Cast<TD_TowerKind>().Select(k => Tower(k, m)).ToArray();
            var ghosts = System.Enum.GetValues(typeof(TD_TowerKind)).Cast<TD_TowerKind>().Select(k => Ghost(k, m)).ToArray();
            var enemies = System.Enum.GetValues(typeof(TD_EnemyKind)).Cast<TD_EnemyKind>().Select(k => Enemy(k, m)).ToArray();
            var effects = BuildEffects(m);
            var hud = TD_HudAuthoring.Build();

            Kit.NewLitScene(new Color(0.42f, 0.66f, 1f), new Color(0.7f, 0.82f, 0.95f), 180f, 520f, new Vector3(48f, -38f, 0f), 1.45f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.66f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.4f, 0.3f);
            AddPostProcessing();
            var baseModel = TD_MapAuthoring.Build(m);

            var mapGo = new GameObject("TowerDefenseMap");
            var map = mapGo.AddComponent<TD_Map>();
            Vector3 end = TD_Rules.Lane[^1];
            map.playerSpawn = Empty("PlayerSpawn", mapGo.transform, end + new Vector3(-8f, 0f, -12f) * TD_Rules.WorldScale).transform;
            map.towerPrefabs = towers;
            map.ghostPrefabs = ghosts;
            map.baseModel = baseModel;
            map.enemyPrefabs = enemies;
            map.slotPrefab = slot;
            map.effects = effects;
            map.clientPrefab = hud;
            EditorUtility.SetDirty(map);
            Kit.SaveActiveScene(Kit.StartScene(Universe));
        }

        static T Override<T>(VolumeProfile profile) where T : VolumeComponent {
            if (!profile.TryGet(out T component)) {
                component = profile.Add<T>(true);
                component.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            return component;
        }

        /// <summary>A bright, saturated grade: filmic tonemapping, bloom on glowing pads and shots, light vignette.</summary>
        static void AddPostProcessing() {
            string path = Root + "/Materials/TowerDefensePost.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            Override<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);
            var bloom = Override<Bloom>(profile);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.4f);
            bloom.scatter.Override(0.6f);
            var color = Override<ColorAdjustments>(profile);
            color.postExposure.Override(0.15f);
            color.contrast.Override(8f);
            color.saturation.Override(-6f);
            var vignette = Override<Vignette>(profile);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.5f);
            EditorUtility.SetDirty(profile);
            var volume = new GameObject("PostProcessing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 5f;
            volume.sharedProfile = profile;
        }

        [MenuItem("Ryan/Tower Defense/Register Scenes")]
        public static void RegisterScenesMenu() => RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));

        // ---------------------------------------------------------------- previews
        [MenuItem("Ryan/Tower Defense/Render Previews")]
        public static void RenderPreviews() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
            var map = Object.FindAnyObjectByType<TD_Map>();
            string dir = PreviewDir(Universe);
            // A handful of towers on cells beside the road (old pad numbers, see TD_TestCells in the tests).
            Vector2[] built = { new(-61f, -24f), new(-53f, 19f), new(-27f, 14f), new(-18f, -21f), new(-9f, 2f), new(9f, 20f), new(25f, 0f), new(43f, -18f) };
            for (int k = 0; k < built.Length; k++) {
                int cell = TD_Rules.NearestCell(new Vector3(built[k].x, 0f, built[k].y) * TD_Rules.WorldScale);
                var pad = (GameObject)PrefabUtility.InstantiatePrefab(map.slotPrefab, map.transform);
                pad.transform.position = TD_Rules.Slots[cell];
                var slot = pad.GetComponent<TD_SlotView>();
                slot.marker.gameObject.SetActive(false);
                var tower = ((GameObject)PrefabUtility.InstantiatePrefab(map.towerPrefabs[k % map.towerPrefabs.Length], map.transform)).GetComponent<TD_TowerView>();
                tower.transform.position = TD_Rules.Slots[cell] + Vector3.up * slot.topHeight;
                tower.ShowLevel(1 + k % TD_Rules.MaxLevel);
            }
            var kinds = new[] { TD_EnemyKind.Scout, TD_EnemyKind.Drone, TD_EnemyKind.Apc, TD_EnemyKind.Shield, TD_EnemyKind.Medic, TD_EnemyKind.Chopper, TD_EnemyKind.Boss, TD_EnemyKind.Carrier };
            for (int i = 0; i < kinds.Length; i++) {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(map.enemyPrefabs[(int)kinds[i]], map.transform);
                Vector3 p = TD_Rules.EnemyPosition(kinds[i], 24f + i * 13f, out Vector3 f);
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(f));
            }
            var cam = PreviewCamera();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            Shoot(cam, new Vector3(0f, 55f, -70f), new Vector3(0f, 0f, -5f), Path.Combine(dir, "overview.png"), 1600, 900);
            Shoot(cam, new Vector3(-30f, 9f, -62f), new Vector3(-42f, 2f, -36f), Path.Combine(dir, "tunnel.png"), 1600, 900);
            Shoot(cam, new Vector3(-38f, 9f, 30f), new Vector3(-24f, 1f, 6f), Path.Combine(dir, "lane.png"), 1600, 900);
            Shoot(cam, new Vector3(-45f, 14f, 35f), new Vector3(-21f, 0f, 3f), Path.Combine(dir, "thumbnail.png"), 640, 360);
            Object.DestroyImmediate(cam.gameObject);
            Kit.PublishThumbnail(Universe, Path.Combine(dir, "thumbnail.png"));
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
        }
    }
}
