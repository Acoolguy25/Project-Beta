using System.Collections.Generic;
using System.IO;
using System.Linq;
using RyanAssets.Characters.Shared;
using RyanAssets.Items.Platform;
using RyanAssets.Platformer;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if !UNITY_SERVER
using Universes.UniverseData.obby.Client;
#endif

namespace Universes.UniverseData.obby.Editor {
    /// <summary>
    /// Rebuilds the obby's textures, materials, piece prefabs, HUD, start scene and build-profile
    /// entries. Safe to run again: assets are overwritten in place, keeping their GUIDs.
    /// Run "Build Server Assets" from an Editor in the Linux Server profile for the server runner.
    /// </summary>
    public static class OB_Authoring {
        public const string Root = "Assets/Universes/UniverseData/obby";
        const string Pieces = Root + "/Prefabs/Pieces";
        const string StartScene = Root + "/obby_start.unity";
        const string ServerScene = Root + "/obby_server.unity";
        const string ServerPrefab = Root + "/Server/OB_ServerRunner.prefab";
        const string ThumbnailPath = Root + "/Resources/UniverseDataPub/obby/thumbnail.png";
        const float LobbyTop = 100f;
        const float LobbySize = 18f;

        static readonly Color[] Palette = {
            new(0.31f, 0.67f, 1f), new(0.38f, 0.86f, 0.42f), new(1f, 0.78f, 0.25f), new(0.98f, 0.45f, 0.38f),
            new(0.72f, 0.5f, 1f), new(0.25f, 0.85f, 0.82f), new(1f, 0.6f, 0.85f), new(0.95f, 0.95f, 0.98f)
        };

        // ---------------------------------------------------------------- entry points
        [MenuItem("Ryan/Obby/Build Client Assets")]
        public static void BuildClientAssets() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            EnsureFolders();
            BuildTextures();
            var prefabs = BuildPieces();
            var hud = BuildHud();
            BuildStartScene(prefabs, hud);
            RegisterScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[Obby] Client assets built.");
        }

        /// <summary>Batchmode entry: client assets, then a thumbnail render if a GPU device exists.</summary>
        public static void BuildClientAssetsBatch() {
            BuildClientAssets();
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) RenderPreviews();
        }

        [MenuItem("Ryan/Obby/Build Server Assets (Linux Server profile)")]
        public static void BuildServerAssets() {
#if UNITY_SERVER
            // The server Editor is usually a ParrelSync clone, which blocks asset saves by default.
            bool? blocked = CloneSaveBlock(null);
            CloneSaveBlock(false);
            try {
                SaveServerAssets();
            } finally {
                if (blocked.HasValue) CloneSaveBlock(blocked.Value);
            }
#else
            throw new System.InvalidOperationException("Switch to the Linux Server build profile (UNITY_SERVER) to build the server runner prefab.");
#endif
        }

        /// <summary>Reads (value null) or sets ParrelSync's cached "block asset saving in clones" preference.</summary>
        static bool? CloneSaveBlock(bool? value) {
            var prefs = System.Type.GetType("ParrelSync.Preferences, ParrelSync");
            var pref = prefs?.GetField("AssetModPref", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
            var property = pref?.GetType().GetProperty("Value");
            if (property == null) return null;
            if (value.HasValue) property.SetValue(pref, value.Value);
            return (bool)property.GetValue(pref);
        }

#if UNITY_SERVER
        static void SaveServerAssets() {
            EnsureFolders();
            var go = new GameObject("OB_ServerRunner");
            var runner = go.AddComponent<Universes.UniverseData.obby.Server.OB_ServerRunner>();
            // DebugBool switches default to on; ship the runner with real-time timers like the other universes.
            var serialized = new SerializedObject(runner);
            serialized.FindProperty("DebugTimerSpeedUp._editor_value").boolValue = false;
            serialized.FindProperty("DebugTimerInfinite._editor_value").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, ServerPrefab);
            Object.DestroyImmediate(go);
            if (prefab == null) throw new System.InvalidOperationException("Could not save " + ServerPrefab);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.SaveScene(scene, ServerScene);
            RegisterScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[Obby] Server assets built.");
        }
#endif

        static void EnsureFolders() {
            foreach (string folder in new[] { "Prefabs", "Prefabs/Pieces", "Materials", "Textures", "Resources", "Resources/UniverseDataPub", "Resources/UniverseDataPub/obby", "Server" }) {
                string path = Root + "/" + folder;
                if (AssetDatabase.IsValidFolder(path)) continue;
                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
        }

        // ---------------------------------------------------------------- textures
        delegate Color Pixel(float u, float v);

        static Texture2D SaveTexture(string name, Pixel pixel, int size = 128, bool sprite = false) {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, pixel((x + 0.5f) / size, (y + 0.5f) / size));
            tex.Apply();
            string path = Root + "/Textures/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Hash(float x, float y) {
            float h = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        static void BuildTextures() {
            // A soft studded tile: bright face, darker bevelled edge, faint inner square.
            SaveTexture("Tile", (u, v) => {
                float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                float shade = edge < 0.04f ? 0.62f : edge < 0.08f ? 0.82f : 1f;
                float inner = Mathf.Abs(Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) - 0.25f) < 0.012f ? 0.9f : 1f;
                float c = shade * inner * (0.96f + 0.04f * Hash(u, v));
                return new Color(c, c, c, 1);
            });
            // Chevrons pointing along +V, for belts and boost pads.
            SaveTexture("Arrows", (u, v) => {
                float x = Mathf.Abs(u - 0.5f);
                float chevron = Mathf.Repeat(v - x * 0.9f, 0.5f);
                bool arrow = chevron < 0.18f && x < 0.38f;
                return arrow ? new Color(1f, 0.85f, 0.2f) : new Color(0.16f, 0.17f, 0.2f);
            });
            SaveTexture("Stripes", (u, v) => Mathf.Repeat(u + v, 0.5f) < 0.25f ? new Color(1f, 0.82f, 0.1f) : new Color(0.12f, 0.12f, 0.14f));
            SaveTexture("Lava", (u, v) => {
                float n = 0.5f + 0.25f * Mathf.Sin(u * 18f + Mathf.Sin(v * 9f) * 2f) + 0.25f * Mathf.Sin(v * 14f + Mathf.Cos(u * 7f) * 2f);
                return Color.Lerp(new Color(0.55f, 0.05f, 0.02f), new Color(1f, 0.65f, 0.1f), Mathf.Clamp01(n));
            });
            SaveTexture("Checker", (u, v) => (Mathf.FloorToInt(u * 4) + Mathf.FloorToInt(v * 4)) % 2 == 0 ? Color.white : new Color(0.1f, 0.1f, 0.12f));
            SaveTexture("Cracks", (u, v) => {
                float crack = Mathf.Min(Mathf.Abs(u - 0.5f - 0.12f * Mathf.Sin(v * 9f)), Mathf.Abs(v - 0.35f - 0.1f * Mathf.Sin(u * 11f)));
                float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                float c = crack < 0.015f ? 0.45f : edge < 0.05f ? 0.7f : 0.92f + 0.08f * Hash(u, v);
                return new Color(c, c * 0.88f, c * 0.7f, 1);
            });
        }

        // ---------------------------------------------------------------- materials
        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + name + ".png");

        static Material Mat(string name, Color color, Texture2D texture = null, float emission = 0f, float alpha = 1f, float smoothness = 0.25f, float metallic = 0f) {
            string path = Root + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            color.a = alpha;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", texture);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (emission > 0f) {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * emission);
                mat.SetTexture("_EmissionMap", texture);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            } else {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
            bool transparent = alpha < 1f;
            mat.SetFloat("_Surface", transparent ? 1f : 0f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", transparent ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", transparent ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : (float)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetFloat("_ZWrite", transparent ? 0f : 1f);
            mat.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            mat.renderQueue = transparent ? (int)UnityEngine.Rendering.RenderQueue.Transparent : -1;
            if (transparent) mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------------------------------------------------------- prefab helpers
        static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider = true) =>
            Primitive(PrimitiveType.Cube, name, parent, position, scale, material, collider);

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider) {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Save(GameObject go, string name) {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, Pieces + "/" + name + ".prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static BoxCollider Trigger(GameObject owner, Vector3 centre, Vector3 size) {
            var box = owner.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = centre;
            box.size = size;
            return box;
        }

        sealed class PiecePrefabs {
            public GameObject platform, checkpointPad, finishPad, lava, spring, boost, gravityZone, conveyor;
            public GameObject crumble, beat, wallJumpWall, mover, spinnerBar, spinnerCross, coin, lobby, gate;
            public Material[] palette;
            public Material beatSolidA, beatGhostA, beatSolidB, beatGhostB;
        }

        // ---------------------------------------------------------------- pieces
        static PiecePrefabs BuildPieces() {
            var tile = Tex("Tile");
            var p = new PiecePrefabs {
                palette = Palette.Select((c, i) => Mat($"Platform{i}", c, tile, smoothness: 0.35f)).ToArray(),
                beatSolidA = Mat("BeatPink", new Color(1f, 0.35f, 0.7f), tile, 0.4f),
                beatGhostA = Mat("BeatPinkGhost", new Color(1f, 0.35f, 0.7f), tile, 0.2f, 0.22f),
                beatSolidB = Mat("BeatBlue", new Color(0.25f, 0.65f, 1f), tile, 0.4f),
                beatGhostB = Mat("BeatBlueGhost", new Color(0.25f, 0.65f, 1f), tile, 0.2f, 0.22f)
            };
            var lavaMat = Mat("Lava", new Color(1f, 0.45f, 0.1f), Tex("Lava"), 1.6f, smoothness: 0.6f);
            var grey = Mat("PadGrey", new Color(0.78f, 0.8f, 0.86f), tile, smoothness: 0.35f);
            var pole = Mat("Pole", new Color(0.92f, 0.92f, 0.95f), smoothness: 0.6f, metallic: 0.5f);
            var flagIdle = Mat("FlagIdle", new Color(0.9f, 0.2f, 0.2f), emission: 0.2f);
            var flagReached = Mat("FlagReached", new Color(0.2f, 1f, 0.35f), emission: 1.2f);
            var gold = Mat("Gold", new Color(1f, 0.78f, 0.2f), emission: 0.6f, smoothness: 0.8f, metallic: 0.9f);
            var checker = Mat("FinishChecker", Color.white, Tex("Checker"), smoothness: 0.3f);
            var springRed = Mat("SpringRed", new Color(0.92f, 0.18f, 0.18f), smoothness: 0.5f);
            var springTop = Mat("SpringTop", new Color(1f, 0.85f, 0.2f), emission: 0.5f, smoothness: 0.5f);
            var coil = Mat("SpringCoil", new Color(0.75f, 0.75f, 0.8f), smoothness: 0.8f, metallic: 0.9f);
            var boostMat = Mat("Boost", new Color(0.4f, 1f, 1f), Tex("Arrows"), 1.2f);
            var belt = Mat("Belt", Color.white, Tex("Arrows"), smoothness: 0.2f);
            var zone = Mat("GravityZone", new Color(0.65f, 0.4f, 1f), emission: 0.3f, alpha: 0.07f);
            var cracks = Mat("Crumble", new Color(0.95f, 0.8f, 0.55f), Tex("Cracks"));
            var stripes = Mat("WallJump", Color.white, Tex("Stripes"), 0.15f);
            var moverMat = Mat("Mover", new Color(0.85f, 0.95f, 1f), tile, 0.25f, smoothness: 0.5f);
            var lobbyMat = Mat("Lobby", new Color(0.88f, 0.9f, 0.95f), tile, smoothness: 0.35f);
            var gateMat = Mat("Gate", new Color(0.35f, 0.75f, 1f), emission: 0.8f, alpha: 0.3f);

            p.platform = Save(Cube("Platform", null, Vector3.zero, Vector3.one, p.palette[0]), "Platform");

            {   // Checkpoint pad: authored at its generated size (6 x 1 x 6), origin at the box centre.
                var root = new GameObject("CheckpointPad");
                Cube("Floor", root.transform, Vector3.zero, new Vector3(6f, 1f, 6f), grey);
                Primitive(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(2.5f, 2f, 2.5f), new Vector3(0.15f, 1.5f, 0.15f), pole, false);
                var flag = Cube("Flag", root.transform, new Vector3(3.15f, 3.05f, 2.5f), new Vector3(1.2f, 0.75f, 0.06f), flagIdle, false);
                var marker = root.AddComponent<CheckpointMarker>();
                marker.flagRenderers = new[] { flag.GetComponent<Renderer>() };
                marker.idleMaterial = flagIdle;
                marker.reachedMaterial = flagReached;
                p.checkpointPad = Save(root, "CheckpointPad");
            }
            {
                var root = new GameObject("FinishPad");
                Cube("Floor", root.transform, Vector3.zero, new Vector3(10f, 1f, 10f), checker);
                Cube("PillarL", root.transform, new Vector3(-4.6f, 3f, 0f), new Vector3(0.6f, 5f, 0.6f), gold);
                Cube("PillarR", root.transform, new Vector3(4.6f, 3f, 0f), new Vector3(0.6f, 5f, 0.6f), gold);
                Cube("Banner", root.transform, new Vector3(0f, 5.8f, 0f), new Vector3(9.8f, 0.8f, 0.4f), checker, false);
                var text = new GameObject("Label", typeof(TextMeshPro));
                text.transform.SetParent(root.transform, false);
                text.transform.localPosition = new Vector3(0f, 5.8f, -0.25f);
                var tmp = text.GetComponent<TextMeshPro>();
                tmp.text = "FINISH";
                tmp.fontSize = 6f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = new Color(1f, 0.85f, 0.2f);
                tmp.rectTransform.sizeDelta = new Vector2(9f, 1f);
                p.finishPad = Save(root, "FinishPad");
            }
            {
                var go = Cube("Lava", null, Vector3.zero, Vector3.one, lavaMat);
                go.AddComponent<HazardVolume>().glowRenderer = go.GetComponent<Renderer>();
                p.lava = Save(go, "Lava");
            }
            {   // Spring: origin on the surface it sits on.
                var root = new GameObject("Spring");
                Cube("Base", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(1.7f, 0.3f, 1.7f), springRed);
                var visual = new GameObject("Visual").transform;
                visual.SetParent(root.transform, false);
                visual.localPosition = new Vector3(0f, 0.3f, 0f);
                Primitive(PrimitiveType.Cylinder, "Coil", visual, new Vector3(0f, 0.12f, 0f), new Vector3(0.9f, 0.12f, 0.9f), coil, false);
                Cube("Top", visual, new Vector3(0f, 0.3f, 0f), new Vector3(1.4f, 0.14f, 1.4f), springTop, false);
                var trigger = new GameObject("Trigger");
                trigger.transform.SetParent(root.transform, false);
                Trigger(trigger, new Vector3(0f, 0.75f, 0f), new Vector3(1.5f, 0.9f, 1.5f));
                var pad = trigger.AddComponent<LaunchPad>();
                pad.stretchVisual = visual;
                p.spring = Save(root, "Spring");
            }
            {
                var root = new GameObject("BoostPad");
                Cube("Panel", root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(2.6f, 0.06f, 2.6f), boostMat, false);
                var trigger = new GameObject("Trigger");
                trigger.transform.SetParent(root.transform, false);
                Trigger(trigger, new Vector3(0f, 0.6f, 0f), new Vector3(2.4f, 1.2f, 2.4f));
                var pad = trigger.AddComponent<LaunchPad>();
                pad.localLaunchVelocity = new Vector3(0f, 9f, 24f);
                pad.stretchAmount = 0f;
                p.boost = Save(root, "BoostPad");
            }
            {
                var go = Cube("GravityZone", null, Vector3.zero, Vector3.one, zone);
                go.GetComponent<BoxCollider>().isTrigger = true;
                go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.AddComponent<GravityZone>();
                p.gravityZone = Save(go, "GravityZone");
            }
            {
                var go = Cube("Conveyor", null, Vector3.zero, Vector3.one, belt);
                var belt2 = go.AddComponent<ConveyorSurface>();
                belt2.beltRenderer = go.GetComponent<Renderer>();
                p.conveyor = Save(go, "Conveyor");
            }
            {
                var root = new GameObject("CrumbleBlock");
                root.AddComponent<BoxCollider>();
                var visual = Cube("Visual", root.transform, Vector3.zero, Vector3.one, cracks, false);
                root.AddComponent<CrumbleBlock>().visual = visual.transform;
                p.crumble = Save(root, "CrumbleBlock");
            }
            {
                var go = Cube("BeatBlock", null, Vector3.zero, Vector3.one, p.beatSolidA);
                var block = go.AddComponent<TimedToggleBlock>();
                block.visuals = new[] { go.GetComponent<Renderer>() };
                block.solidMaterial = p.beatSolidA;
                block.ghostMaterial = p.beatGhostA;
                p.beat = Save(go, "BeatBlock");
            }
            {
                var go = Cube("WallJumpWall", null, Vector3.zero, Vector3.one, stripes);
                go.AddComponent<WallJumpSurface>();
                p.wallJumpWall = Save(go, "WallJumpWall");
            }
            {
                var go = Cube("Mover", null, Vector3.zero, Vector3.one, moverMat);
                var body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                go.AddComponent<NetworkPlatform>();
                go.AddComponent<GroundMotionTransfer>().transferRotation = false;
                p.mover = Save(go, "Mover");
            }
            p.spinnerBar = Spinner("SpinnerBar", lavaMat, pole, false);
            p.spinnerCross = Spinner("SpinnerCross", lavaMat, pole, true);
            p.coin = BuildCoin(gold);
            {
                var go = Cube("Gate", null, Vector3.zero, Vector3.one, gateMat);
                go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                p.gate = Save(go, "StartGate");
            }
            {
                var root = new GameObject("Lobby");
                Cube("Floor", root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(LobbySize, 1f, LobbySize), lobbyMat);
                for (int side = -1; side <= 1; side += 2) {
                    Cube($"Rail{side}", root.transform, new Vector3(side * (LobbySize * 0.5f - 0.25f), 0.4f, 0f), new Vector3(0.5f, 0.8f, LobbySize), p.palette[0]);
                }
                Cube("RailBack", root.transform, new Vector3(0f, 0.4f, -LobbySize * 0.5f + 0.25f), new Vector3(LobbySize, 0.8f, 0.5f), p.palette[0]);
                for (int i = 0; i < 4; i++) {
                    float x = (i % 2 == 0 ? -1 : 1) * (LobbySize * 0.5f - 0.6f), z = (i < 2 ? -1 : 1) * (LobbySize * 0.5f - 0.6f);
                    Cube($"Pillar{i}", root.transform, new Vector3(x, 2f, z), new Vector3(0.8f, 4f, 0.8f), p.palette[1 + i]);
                }
                var sign = new GameObject("Sign", typeof(TextMeshPro));
                sign.transform.SetParent(root.transform, false);
                sign.transform.localPosition = new Vector3(0f, 3.2f, LobbySize * 0.5f - 0.6f);
                var tmp = sign.GetComponent<TextMeshPro>();
                tmp.text = "SKY OBBY\n<size=50%>A new course every race. Reach the finish first!</size>";
                tmp.fontSize = 5f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                tmp.rectTransform.sizeDelta = new Vector2(12f, 3f);
                sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                p.lobby = Save(root, "Lobby");
            }
            return p;
        }

        static GameObject Spinner(string name, Material hazard, Material hub, bool cross) {
            var root = new GameObject(name);
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            root.AddComponent<NetworkPlatform>();
            for (int i = 0; i < (cross ? 2 : 1); i++) {
                var bar = Cube($"Bar{i}", root.transform, Vector3.zero, new Vector3(11.4f, 0.5f, 0.5f), hazard);
                bar.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                bar.AddComponent<HazardVolume>().glowRenderer = bar.GetComponent<Renderer>();
            }
            Primitive(PrimitiveType.Cylinder, "Hub", root.transform, new Vector3(0f, 0f, 0f), new Vector3(0.9f, 0.4f, 0.9f), hub, false);
            return Save(root, name);
        }

        static GameObject BuildCoin(Material gold) {
            var root = new GameObject("Coin");
            var spin = new GameObject("Spin").transform;
            spin.SetParent(root.transform, false);
            spin.gameObject.AddComponent<Spinner>().degreesPerSecond = new Vector3(0f, 140f, 0f);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.ryan.gameframework/Runtime/Items/Coin/Low Poly Coin.fbx");
            if (model != null) {
                var coin = (GameObject)PrefabUtility.InstantiatePrefab(model, spin);
                foreach (var c in coin.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                foreach (var r in coin.GetComponentsInChildren<Renderer>()) {
                    r.sharedMaterial = gold;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                // Normalise the imported coin to a 1.1 m upright disc, whatever its import scale/axis.
                var bounds = Bounds(coin);
                Vector3 e = bounds.size;
                if (e.y < e.x * 0.5f && e.y < e.z * 0.5f) coin.transform.localRotation = Quaternion.Euler(90f, 0f, 0f) * coin.transform.localRotation;
                bounds = Bounds(coin);
                float scale = 1.1f / Mathf.Max(0.0001f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
                coin.transform.localScale *= scale;
                bounds = Bounds(coin);
                coin.transform.localPosition -= bounds.center - spin.position;
            } else {
                Primitive(PrimitiveType.Cylinder, "Disc", spin, Vector3.zero, new Vector3(1f, 0.08f, 1f), gold, false).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            return Save(root, "Coin");
        }

        static Bounds Bounds(GameObject go) {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        // ---------------------------------------------------------------- HUD
        static readonly Color Ink = new(0.05f, 0.07f, 0.12f, 0.72f);

        static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static UnityEngine.UI.Image Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color) {
            var image = Rect(name, parent, min, max).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static TMP_Text Text(string name, Transform parent, Vector2 min, Vector2 max, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false) {
            var text = Rect(name, parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            if (bold) text.fontStyle = FontStyles.Bold;
            text.outlineWidth = 0.18f;
            text.outlineColor = new Color32(0, 0, 0, 200);
            return text;
        }

        static GameObject BuildHud() {
#if UNITY_SERVER
            // The client assembly is not compiled in the server profile; keep the prefab authored by a client Editor.
            return AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/ObbyHUD.prefab");
#else
            var root = new GameObject("ObbyHUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var hud = root.AddComponent<OB_ClientController>();
            Color white = new(0.97f, 0.98f, 1f), yellow = new(1f, 0.85f, 0.25f);

            var stage = Panel("StagePanel", root.transform, new Vector2(0.012f, 0.4f), new Vector2(0.25f, 0.6f), Ink);
            hud.stageLabel = Text("Stage", stage.transform, new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.97f), 40, yellow, bold: true);
            hud.stageNameLabel = Text("StageName", stage.transform, new Vector2(0.05f, 0.4f), new Vector2(0.95f, 0.64f), 26, white);
            hud.hintLabel = Text("Hint", stage.transform, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.42f), 19, new Color(0.8f, 0.86f, 0.95f));
            var stats = Panel("StatsPanel", root.transform, new Vector2(0.012f, 0.31f), new Vector2(0.25f, 0.39f), Ink);
            hud.timerLabel = Text("Timer", stats.transform, new Vector2(0.05f, 0.05f), new Vector2(0.62f, 0.95f), 28, white, TextAlignmentOptions.MidlineLeft, true);
            hud.coinsLabel = Text("Coins", stats.transform, new Vector2(0.6f, 0.05f), new Vector2(0.96f, 0.95f), 24, yellow, TextAlignmentOptions.MidlineRight);
            hud.leaderLabel = Text("Leader", root.transform, new Vector2(0.3f, 0.79f), new Vector2(0.7f, 0.85f), 24, white, TextAlignmentOptions.Center);
            hud.toastLabel = Text("Toast", root.transform, new Vector2(0.2f, 0.6f), new Vector2(0.8f, 0.7f), 44, yellow, TextAlignmentOptions.Center, true);
            hud.centerLabel = Text("Center", root.transform, new Vector2(0.3f, 0.38f), new Vector2(0.7f, 0.62f), 140, white, TextAlignmentOptions.Center, true);
            var results = Panel("Results", root.transform, new Vector2(0.3f, 0.2f), new Vector2(0.7f, 0.56f), new Color(0.05f, 0.07f, 0.12f, 0.88f));
            hud.resultsPanel = results.gameObject;
            Text("ResultsTitle", results.transform, new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.97f), 36, yellow, TextAlignmentOptions.Center, true).text = "RACE RESULTS";
            hud.resultsBody = Text("ResultsBody", results.transform, new Vector2(0.07f, 0.05f), new Vector2(0.93f, 0.8f), 26, white, TextAlignmentOptions.TopLeft);
            var audio = new GameObject("Sfx");
            audio.transform.SetParent(root.transform, false);
            hud.sfx = audio.AddComponent<AudioSource>();
            hud.sfx.playOnAwake = false;
            hud.sfx.spatialBlend = 0f;
            var coinClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Packages/com.ryan.gameframework/Runtime/Items/Coin/freesound_gamestudio-drop-coin-384921.mp3");
            hud.coinClip = coinClip;
            hud.checkpointClip = coinClip;
            hud.finishClip = coinClip;
            hud.fallClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/UnityTechnologies/SpaceRobotKyle/Sfx/Player_Land.wav");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/ObbyHUD.prefab");
            Object.DestroyImmediate(root);
            return prefab;
#endif
        }

        // ---------------------------------------------------------------- scenes
        static void BuildStartScene(PiecePrefabs p, GameObject hud) {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            {
                var sky = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Sky.mat");
                if (sky == null) {
                    sky = new Material(Shader.Find("Skybox/Procedural"));
                    AssetDatabase.CreateAsset(sky, Root + "/Materials/Sky.mat");
                }
                sky.SetColor("_SkyTint", new Color(0.45f, 0.65f, 1f));
                sky.SetColor("_GroundColor", new Color(0.55f, 0.7f, 0.95f));
                sky.SetFloat("_AtmosphereThickness", 0.8f);
                sky.SetFloat("_Exposure", 1.25f);
                EditorUtility.SetDirty(sky);
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.9f);
                RenderSettings.ambientEquatorColor = new Color(0.55f, 0.6f, 0.7f);
                RenderSettings.ambientGroundColor = new Color(0.35f, 0.38f, 0.45f);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = new Color(0.7f, 0.82f, 0.98f);
                RenderSettings.fogStartDistance = 120f;
                RenderSettings.fogEndDistance = 420f;
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;

                var sun = new GameObject("Sun", typeof(Light));
                var light = sun.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.color = new Color(1f, 0.96f, 0.88f);
                light.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
                RenderSettings.sun = light;

                var courseGo = new GameObject("Course");
                var course = courseGo.AddComponent<OB_Course>();
                var lobby = (GameObject)PrefabUtility.InstantiatePrefab(p.lobby, scene);
                lobby.transform.position = new Vector3(0f, LobbyTop, 0f);
                var spawn = new GameObject("LobbySpawn").transform;
                spawn.SetParent(courseGo.transform, false);
                spawn.position = new Vector3(0f, LobbyTop, -3f);
                var start = new GameObject("CourseStart").transform;
                start.SetParent(courseGo.transform, false);
                start.position = new Vector3(0f, LobbyTop, LobbySize * 0.5f);
                var gate = (GameObject)PrefabUtility.InstantiatePrefab(p.gate, scene);
                gate.transform.SetParent(courseGo.transform, false);
                gate.transform.position = new Vector3(0f, LobbyTop + 2f, LobbySize * 0.5f + 0.2f);
                gate.transform.localScale = new Vector3(LobbySize, 4f, 0.4f);

                course.lobbySpawn = spawn;
                course.courseStart = start;
                course.startGate = gate;
                course.platform = p.platform; course.checkpointPad = p.checkpointPad; course.finishPad = p.finishPad;
                course.lava = p.lava; course.spring = p.spring; course.boost = p.boost; course.gravityZone = p.gravityZone;
                course.conveyor = p.conveyor; course.crumble = p.crumble; course.beat = p.beat; course.wallJumpWall = p.wallJumpWall;
                course.mover = p.mover; course.spinnerBar = p.spinnerBar; course.spinnerCross = p.spinnerCross; course.coin = p.coin;
                course.platformPalette = p.palette;
                course.beatSolidA = p.beatSolidA; course.beatGhostA = p.beatGhostA; course.beatSolidB = p.beatSolidB; course.beatGhostB = p.beatGhostB;
                course.clientPrefab = hud;
                EditorUtility.SetDirty(course);
                EditorSceneManager.SaveScene(scene, StartScene);
            }
        }

        /// <summary>
        /// Clients load universe scenes from the shared scene list; the Linux Server profile overrides it with
        /// its own list, which also carries the server-only scene.
        /// </summary>
        [MenuItem("Ryan/Obby/Register Scenes")]
        public static void RegisterScenes() {
            if (File.Exists(StartScene) && EditorBuildSettings.scenes.All(s => s.path != StartScene))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Append(new EditorBuildSettingsScene(StartScene, true)).ToArray();
            var wanted = new[] { StartScene, ServerScene }.Where(File.Exists).ToArray();
            foreach (string guid in AssetDatabase.FindAssets("t:BuildProfile", new[] { "Assets/Settings/Build Profiles" })) {
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile == null || !new SerializedObject(profile).FindProperty("m_OverrideGlobalSceneList").boolValue) continue;
                var scenes = profile.scenes.ToList();
                int before = scenes.Count;
                foreach (string path in wanted)
                    if (scenes.All(s => s.path != path)) scenes.Add(new EditorBuildSettingsScene(path, true));
                if (scenes.Count == before) continue;
                profile.scenes = scenes.ToArray();
                EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- previews
        /// <summary>Builds a sample course in a temporary copy of the start scene and renders previews plus the browser thumbnail.</summary>
        [MenuItem("Ryan/Obby/Render Previews")]
        public static void RenderPreviews() {
            int seed = 20261115;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(StartScene, OpenSceneMode.Single);
            {
                var course = Object.FindAnyObjectByType<OB_Course>();
                var layout = course.Build(seed, 14);
                string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/obby-previews"));
                Directory.CreateDirectory(outDir);
                var camGo = new GameObject("PreviewCamera", typeof(Camera));
                var cam = camGo.GetComponent<Camera>();
                cam.fieldOfView = 55f;
                cam.farClipPlane = 1500f;
                cam.clearFlags = CameraClearFlags.Skybox;
                Vector3 start = course.LobbySpawn, end = layout.finishTop;
                Vector3 mid = (start + end) * 0.5f;
                Shoot(cam, mid + new Vector3(-150f, 90f, -40f), mid, Path.Combine(outDir, "overview.png"), 1600, 900);
                for (int s = 0; s < layout.StageCount; s++) {
                    var st = layout.stages[s];
                    Vector3 next = s + 1 < layout.StageCount ? layout.stages[s + 1].spawn : layout.finishTop;
                    Vector3 focus = (st.spawn + next) * 0.5f;
                    Vector3 dir = (next - st.spawn); dir.y = 0; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    Shoot(cam, focus - side * 22f - dir * 8f + Vector3.up * 12f, focus, Path.Combine(outDir, $"stage{s:00}-{st.kind}.png"), 960, 540);
                }
                Shoot(cam, start + new Vector3(-9f, 7f, -14f), start + new Vector3(0f, 2f, 18f), Path.Combine(outDir, "thumbnail.png"), 640, 360);
                File.Copy(Path.Combine(outDir, "thumbnail.png"), ThumbnailPath, true);
                Object.DestroyImmediate(camGo);
                course.Clear();
            }
            AssetDatabase.ImportAsset(ThumbnailPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(ThumbnailPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        static void Shoot(Camera cam, Vector3 position, Vector3 target, string path, int width, int height) {
            cam.transform.position = position;
            cam.transform.LookAt(target);
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
