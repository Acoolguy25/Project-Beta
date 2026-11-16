using System.IO;
using RyanAssets.Authoring;
using RyanAssets.Rounds;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static RyanAssets.Authoring.UniverseAuthoringKit;
#if !UNITY_SERVER
using RyanAssets.UI.Editor;
#endif

namespace Universes.UniverseData.capture_the_flag.Editor {
    /// <summary>
    /// Builds Flag Wars: the three-lane arena (<see cref="CTF_ArenaAuthoring"/>), flag and power-up
    /// prefabs, and the HUD (<see cref="CTF_HudAuthoring"/>). Safe to re-run. Server runner: run
    /// <c>Universes.UniverseData.capture_the_flag.Editor.CTF_Authoring.BuildServerAssets</c> in the Linux Server profile.
    /// </summary>
    public static class CTF_Authoring {
        public const string Universe = "capture_the_flag";
        public const string Root = "Assets/Universes/UniverseData/capture_the_flag";
        internal static readonly UniverseAuthoringKit Kit = new(Root);
        const string ServerPrefab = Root + "/Server/CTF_ServerRunner.prefab";
        static readonly Vector2 FlagLabelBox = new(8f, 2f);
        /// <summary>Largest share of the screen height the flag label may cover up close.</summary>
        const float FlagLabelMaxScreen = 0.08f;
        const string KayKitSpace = "Assets/ThirdParty/KayKit/SpaceBaseBits/Models/";

        /// <summary>
        /// Makes a world label face the camera plane (no skew near the screen edges), stay upright and
        /// shrink at close range so it never covers more than <paramref name="maxScreen"/> of the screen.
        /// </summary>
        internal static Billboard FacingLabel(TMPro.TMP_Text label, float boxHeight, float maxScreen) {
            var billboard = label.gameObject.AddComponent<Billboard>();
            billboard.facing = Billboard.Facing.CameraPlane;
            billboard.yawOnly = true;
            billboard.referenceHeight = boxHeight;
            billboard.maxScreenHeight = maxScreen;
            return billboard;
        }

        [MenuItem("Ryan/Flag Wars/Build Client Assets")]
        public static void BuildClientAssets() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            Kit.EnsureFolders("Prefabs", "Materials", "Textures", "Server");
            BuildTextures();
            BuildStartScene();
            RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));
            AssetDatabase.SaveAssets();
            Debug.Log("[FlagWars] Client assets built.");
        }

        public static void BuildClientAssetsBatch() {
            BuildClientAssets();
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) RenderPreviews();
        }

        [MenuItem("Ryan/Flag Wars/Build Server Assets (Linux Server profile)")]
        public static void BuildServerAssets() {
#if UNITY_SERVER
            Kit.EnsureFolders("Server");
            Kit.BuildServerRunner<Universes.UniverseData.capture_the_flag.Server.CTF_ServerRunner>(ServerPrefab, Kit.ServerScene(Universe));
            RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));
            Debug.Log("[FlagWars] Server assets built.");
#else
            throw new System.InvalidOperationException("Switch to the Linux Server build profile (UNITY_SERVER) to build the server runner prefab.");
#endif
        }

        /// <summary>Generated fallbacks and the textures no free pack covers (sandbags, base concrete).</summary>
        static void BuildTextures() {
            Kit.SaveTexture("Grass", (u, v) => {
                float n = Hash(Mathf.Floor(u * 64f), Mathf.Floor(v * 64f));
                float c = 0.82f + 0.18f * n;
                return new Color(0.45f * c, 0.66f * c, 0.3f * c);
            });
            Kit.SaveTexture("Concrete", (u, v) => {
                float n = 0.86f + 0.14f * Hash(Mathf.Floor(u * 32f), Mathf.Floor(v * 32f));
                bool seam = Mathf.Repeat(u * 2f, 1f) < 0.02f || Mathf.Repeat(v * 2f, 1f) < 0.02f;
                return seam ? new Color(0.45f, 0.46f, 0.48f) : new Color(0.72f, 0.72f, 0.7f) * n;
            });
            Kit.SaveTexture("Sandbag", (u, v) => {
                float row = Mathf.Floor(v * 4f);
                float x = Mathf.Repeat(u * 3f + row * 0.5f, 1f), y = Mathf.Repeat(v * 4f, 1f);
                float bag = Mathf.Clamp01(1f - (Mathf.Pow(x * 2f - 1f, 4f) + Mathf.Pow(y * 2f - 1f, 4f)));
                return new Color(0.66f, 0.58f, 0.4f) * (0.55f + 0.45f * bag);
            });
        }

        // ---------------------------------------------------------------- prefabs
        static GameObject BuildFlag(CTF_Team team) {
            var root = new GameObject($"{CTF_Rules.Name(team)}Flag");
            var view = root.AddComponent<CTF_FlagView>();
            var pole = Kit.Mat("FlagPole", new Color(0.85f, 0.85f, 0.88f), smoothness: 0.6f, metallic: 0.6f);
            var cloth = Kit.Mat($"Flag{team}", CTF_Rules.Color(team), emission: 0.35f);
            var glow = Kit.Mat($"Glow{team}", CTF_Rules.Color(team), emission: 2f, alpha: 0.35f);
            Primitive(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(0f, 2.2f, 0f), new Vector3(0.14f, 2.2f, 0.14f), pole, false);
            Primitive(PrimitiveType.Sphere, "Finial", root.transform, new Vector3(0f, 4.5f, 0f), Vector3.one * 0.32f, pole, false);
            var hinge = Empty("ClothHinge", root.transform, new Vector3(0f, 3.75f, 0f));
            Cube("Cloth", hinge.transform, new Vector3(0.95f, 0f, 0f), new Vector3(1.9f, 1.2f, 0.06f), cloth, false);
            view.cloth = hinge.transform;
            var ring = Primitive(PrimitiveType.Cylinder, "HomeGlow", root.transform, new Vector3(0f, 0.04f, 0f), new Vector3(CTF_Rules.PickupRange * 2f, 0.03f, CTF_Rules.PickupRange * 2f), glow, false);
            NoShadows(ring);
            view.homeGlow = ring;
            view.label = WorldText("Label", root.transform, new Vector3(0f, 5.6f, 0f), CTF_Rules.Name(team) + " FLAG", 3f, Color.white, FlagLabelBox);
            FacingLabel(view.label, FlagLabelBox.y, FlagLabelMaxScreen);
            return Kit.Save(root, "", root.name);
        }

        /// <summary>Colour of each power-up's icon and ring (matches the HUD colours in CTF_ClientController).</summary>
        static Color PowerUpColor(CTF_PowerUpKind kind) => kind == CTF_PowerUpKind.Health ? new Color(0.35f, 1f, 0.45f) : new Color(1f, 0.85f, 0.25f);

        /// <summary>Icon size and height over the pad, in metres (the robot is about 5.6 m tall).</summary>
        const float PowerUpIconScale = 3.4f, PowerUpIconHeight = 3f;
        /// <summary>Ring glow: bright enough to read in daylight, low enough that bloom keeps its colour.</summary>
        const float PowerUpRingEmission = 0.9f, PowerUpRingAlpha = 0.3f;

        static string PowerUpIcon(CTF_PowerUpKind kind) => kind == CTF_PowerUpKind.Health ? "favorite" : "bolt";

        /// <summary>
        /// A power-up: a small landing pad, a glowing ring as wide as the pickup reach, and the
        /// spinning, bobbing icon that hides while it recharges.
        /// </summary>
        static GameObject BuildPowerUp(CTF_PowerUpKind kind) {
            var root = new GameObject($"{kind}PowerUp");
            var spot = root.AddComponent<CTF_PowerUp>();
            spot.kind = kind;
            Color color = PowerUpColor(kind);
            var pad = Model(KayKitSpace + "landingpad_small.fbx", root.transform, CTF_Rules.PowerUpRange * 1.6f);
            if (pad != null) {
                var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(KayKitSpace + "spacebits_texture.png");
                var padMat = Kit.Mat("KayKitSpace", Color.white, atlas, smoothness: 0.3f);
                foreach (var r in pad.GetComponentsInChildren<Renderer>()) r.sharedMaterial = padMat;
            } else {
                Debug.LogWarning("[FlagWars] KayKit Space Base Bits missing: power-up pads have no base model.");
            }
            var visual = Empty("Visual", root.transform);
            spot.visual = visual;
            var ring = Primitive(PrimitiveType.Cylinder, "Ring", visual.transform, new Vector3(0f, 0.12f, 0f),
                new Vector3(CTF_Rules.PowerUpRange * 2f, 0.02f, CTF_Rules.PowerUpRange * 2f), Kit.Mat($"Glow{kind}", color, emission: PowerUpRingEmission, alpha: PowerUpRingAlpha), false);
            NoShadows(ring);
            var spinner = Empty("Spinner", visual.transform, new Vector3(0f, PowerUpIconHeight, 0f));
            spot.spinner = spinner.transform;
            var icon = new GameObject("Icon", typeof(SpriteRenderer));
            icon.transform.SetParent(spinner.transform, false);
            icon.transform.localScale = Vector3.one * PowerUpIconScale;
            var sprite = icon.GetComponent<SpriteRenderer>();
            sprite.color = color;
            sprite.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
#if !UNITY_SERVER
            sprite.sprite = UIThemeAssets.Icon(PowerUpIcon(kind));
#endif
            return Kit.Save(root, "", root.name);
        }

        // ---------------------------------------------------------------- scene
        static void BuildStartScene() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var hud = CTF_HudAuthoring.Build();
            var flags = new[] { BuildFlag(CTF_Team.Red), BuildFlag(CTF_Team.Blue) };
            var powerUps = new[] { BuildPowerUp(CTF_PowerUpKind.Health), BuildPowerUp(CTF_PowerUpKind.Speed) };
            var mapGo = CTF_ArenaAuthoring.Build(powerUps);
            var map = mapGo.GetComponent<CTF_Map>();
            map.flagPrefabs = flags;
            map.clientPrefab = hud;
            EditorUtility.SetDirty(map);
            if (!map.IsConfigured) throw new System.InvalidOperationException("[FlagWars] The generated CTF_Map is not configured; see CTF_ArenaAuthoring.");
            Kit.SaveActiveScene(Kit.StartScene(Universe));
        }

        [MenuItem("Ryan/Flag Wars/Register Scenes")]
        public static void RegisterScenesMenu() => RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));

        // ---------------------------------------------------------------- previews
        [MenuItem("Ryan/Flag Wars/Render Previews")]
        public static void RenderPreviews() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
            var map = Object.FindAnyObjectByType<CTF_Map>();
            for (int i = 0; i < 2; i++) {
                var flag = (GameObject)PrefabUtility.InstantiatePrefab(map.flagPrefabs[i], map.transform);
                flag.transform.position = map.flagStands[i].position;
            }
            string dir = PreviewDir(Universe);
            var cam = PreviewCamera();
            Vector3 red = map.flagStands[0].position, blue = map.flagStands[1].position;
            Shoot(cam, new Vector3(0f, 150f, -165f), Vector3.zero, Path.Combine(dir, "overview.png"), 1600, 900);
            Shoot(cam, blue + new Vector3(-30f, 12f, -22f), blue + Vector3.up * 2f, Path.Combine(dir, "base.png"), 1600, 900);
            Shoot(cam, red + new Vector3(30f, 12f, 22f), red + Vector3.up * 2f, Path.Combine(dir, "base-red.png"), 1600, 900);
            Shoot(cam, new Vector3(-40f, 9f, -14f), new Vector3(10f, 3f, 4f), Path.Combine(dir, "midfield.png"), 1600, 900);
            Shoot(cam, new Vector3(-70f, 10f, 30f), new Vector3(0f, 2f, 48f), Path.Combine(dir, "north-lane.png"), 1600, 900);
            Shoot(cam, blue + new Vector3(-40f, 18f, -34f), blue + new Vector3(-10f, 0f, 0f), Path.Combine(dir, "thumbnail.png"), 640, 360);
            Object.DestroyImmediate(cam.gameObject);
            Kit.PublishThumbnail(Universe, Path.Combine(dir, "thumbnail.png"));
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
        }
    }
}
