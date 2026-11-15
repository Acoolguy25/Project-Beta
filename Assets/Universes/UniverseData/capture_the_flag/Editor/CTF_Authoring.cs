using System.Collections.Generic;
using System.IO;
using RyanAssets.Authoring;
using RyanAssets.Rounds;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static RyanAssets.Authoring.UniverseAuthoringKit;
#if !UNITY_SERVER
using Universes.UniverseData.capture_the_flag.Client;
#endif

namespace Universes.UniverseData.capture_the_flag.Editor {
    /// <summary>
    /// Builds Flag Wars: a mirrored military arena (Cartoon Military pack), two bases with flag
    /// stands, flag prefabs and HUD. Safe to re-run. Server runner: run
    /// <c>Universes.UniverseData.capture_the_flag.Editor.CTF_Authoring.BuildServerAssets</c> in the Linux Server profile.
    /// </summary>
    public static class CTF_Authoring {
        public const string Universe = "capture_the_flag";
        public const string Root = "Assets/Universes/UniverseData/capture_the_flag";
        static readonly UniverseAuthoringKit Kit = new(Root);
        const string ServerPrefab = Root + "/Server/CTF_ServerRunner.prefab";
        const string Military = "Assets/CartoonMilitaryModelPack/Prefebs/";
        const float BaseX = 82f;
        const float BaseHeight = 1.2f;

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
            Kit.SaveTexture("Crate", (u, v) => {
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float grain = 0.85f + 0.15f * Hash(Mathf.Floor(u * 48f), Mathf.Floor(v * 6f));
                bool brace = Mathf.Abs(u - v) < 0.06f || edge < 0.08f;
                return brace ? new Color(0.36f, 0.3f, 0.16f) : new Color(0.5f, 0.45f, 0.26f) * grain;
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
            view.label = WorldText("Label", root.transform, new Vector3(0f, 5.6f, 0f), CTF_Rules.Name(team) + " FLAG", 3f, Color.white, new Vector2(8f, 2f));
            view.label.gameObject.AddComponent<Billboard>();
            return Kit.Save(root, "", root.name);
        }

        static GameObject BuildHud() {
#if UNITY_SERVER
            return AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/FlagWarsHUD.prefab");
#else
            var root = Canvas("FlagWarsHUD");
            var hud = root.AddComponent<CTF_ClientController>();
            Color white = new(0.97f, 0.98f, 1f), yellow = new(1f, 0.85f, 0.25f);
            var score = Panel("ScorePanel", root.transform, new Vector2(0.4f, 0.8f), new Vector2(0.6f, 0.88f), Ink);
            Panel("RedBack", score.transform, new Vector2(0f, 0f), new Vector2(0.38f, 1f), new Color(0.85f, 0.2f, 0.18f, 0.85f));
            Panel("BlueBack", score.transform, new Vector2(0.62f, 0f), new Vector2(1f, 1f), new Color(0.18f, 0.42f, 0.9f, 0.85f));
            hud.redScoreLabel = Text("Red", score.transform, new Vector2(0f, 0f), new Vector2(0.38f, 1f), 46, white, TextAlignmentOptions.Center, true);
            hud.blueScoreLabel = Text("Blue", score.transform, new Vector2(0.62f, 0f), new Vector2(1f, 1f), 46, white, TextAlignmentOptions.Center, true);
            hud.timerLabel = Text("Timer", score.transform, new Vector2(0.38f, 0f), new Vector2(0.62f, 1f), 22, yellow, TextAlignmentOptions.Center, true);
            var team = Panel("TeamPanel", root.transform, new Vector2(0.012f, 0.52f), new Vector2(0.2f, 0.58f), Ink);
            hud.teamStripe = Panel("Stripe", team.transform, new Vector2(0f, 0f), new Vector2(0.04f, 1f), white);
            hud.teamLabel = Text("Team", team.transform, new Vector2(0.08f, 0f), new Vector2(0.98f, 1f), 26, white, TextAlignmentOptions.MidlineLeft, true);
            var objective = Panel("ObjectivePanel", root.transform, new Vector2(0.27f, 0.14f), new Vector2(0.73f, 0.2f), Ink);
            hud.objectiveLabel = Text("Objective", objective.transform, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), 24, white, TextAlignmentOptions.Center);
            hud.toastLabel = Text("Toast", root.transform, new Vector2(0.62f, 0.7f), new Vector2(0.988f, 0.78f), 24, white, TextAlignmentOptions.Right, true);
            hud.bannerLabel = Text("Banner", root.transform, new Vector2(0.1f, 0.6f), new Vector2(0.9f, 0.72f), 48, yellow, TextAlignmentOptions.Center, true);
            hud.sfx = Sfx(root.transform);
            hud.takenClip = hud.returnedClip = Clip(LandClip);
            hud.capturedClip = Clip(CoinClip);
            return Kit.Save(root, "", "FlagWarsHUD");
#endif
        }

        // ---------------------------------------------------------------- scene
        /// <summary>A pack model with a box collider over it, so it works as cover.</summary>
        static GameObject Cover(string model, Transform parent, Vector3 position, float size, float yaw, Material fallback) {
            var anchor = Empty(Path.GetFileNameWithoutExtension(model), parent, position);
            var go = Model(Military + model, anchor.transform, size);
            if (go == null) go = Cube("Box", anchor.transform, Vector3.up * size * 0.25f, new Vector3(size, size * 0.5f, size * 0.5f), fallback, false);
            var bounds = RendererBounds(anchor);
            var box = anchor.AddComponent<BoxCollider>();
            box.center = bounds.center - anchor.transform.position;
            box.size = bounds.size;
            anchor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return anchor;
        }

        /// <summary>Places the same thing on both halves, mirrored through the centre (point symmetry).</summary>
        static void Mirrored(System.Action<Vector3, float> place, Vector3 position, float yaw) {
            place(position, yaw);
            place(new Vector3(-position.x, position.y, -position.z), yaw + 180f);
        }

        static void BuildStartScene() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var hud = BuildHud();
            var flags = new[] { BuildFlag(CTF_Team.Red), BuildFlag(CTF_Team.Blue) };
            var grass = Tiled(Kit.Mat("Grass", Color.white, Kit.Tex("Grass"), smoothness: 0.05f), 24f, 14f);
            var concrete = Tiled(Kit.Mat("Concrete", Color.white, Kit.Tex("Concrete"), smoothness: 0.15f), 6f, 6f);
            var road = Tiled(Kit.Mat("Road", new Color(0.55f, 0.55f, 0.57f), Kit.Tex("Concrete"), smoothness: 0.1f), 30f, 1f);
            var crate = Kit.Mat("Crate", Color.white, Kit.Tex("Crate"), smoothness: 0.1f);
            var sandbag = Tiled(Kit.Mat("Sandbag", Color.white, Kit.Tex("Sandbag"), smoothness: 0.05f), 2f, 1f);
            var red = Kit.Mat("TeamRed", CTF_Rules.Color(CTF_Team.Red), emission: 0.2f);
            var blue = Kit.Mat("TeamBlue", CTF_Rules.Color(CTF_Team.Blue), emission: 0.2f);
            var wall = Tiled(Kit.Mat("Wall", new Color(0.62f, 0.6f, 0.55f), Kit.Tex("Concrete")), 20f, 1f);
            Kit.NewLitScene(new Color(0.5f, 0.7f, 1f), new Color(0.75f, 0.84f, 0.95f), 160f, 460f, new Vector3(48f, -40f, 0f));

            var world = new GameObject("World");
            Cube("Ground", world.transform, new Vector3(0f, -0.5f, 0f), new Vector3(220f, 1f, 120f), grass);
            Cube("Road", world.transform, new Vector3(0f, 0.01f, 0f), new Vector3(150f, 0.04f, 6f), road, false);
            // Perimeter walls.
            Cube("WallN", world.transform, new Vector3(0f, 3f, 60f), new Vector3(220f, 6f, 1.5f), wall);
            Cube("WallS", world.transform, new Vector3(0f, 3f, -60f), new Vector3(220f, 6f, 1.5f), wall);
            Cube("WallE", world.transform, new Vector3(110f, 3f, 0f), new Vector3(1.5f, 6f, 120f), wall);
            Cube("WallW", world.transform, new Vector3(-110f, 3f, 0f), new Vector3(1.5f, 6f, 120f), wall);

            var mapGo = new GameObject("FlagWarsMap");
            var map = mapGo.AddComponent<CTF_Map>();
            map.flagStands = new Transform[2];
            var spawns = new List<Transform>[] { new(), new() };
            for (int t = 0; t < 2; t++) {
                var team = (CTF_Team)t;
                float side = team == CTF_Team.Red ? -1f : 1f;
                var teamMat = team == CTF_Team.Red ? red : blue;
                Vector3 baseAt = new(side * BaseX, 0f, 0f);
                var b = Empty($"{CTF_Rules.Name(team)}Base", world.transform);
                // Raised base with ramps on three sides.
                Cube("Platform", b.transform, baseAt + new Vector3(0f, BaseHeight * 0.5f, 0f), new Vector3(30f, BaseHeight, 34f), concrete);
                for (int k = -1; k <= 1; k += 2) {
                    var ramp = Cube($"RampZ{k}", b.transform, baseAt + new Vector3(0f, BaseHeight * 0.5f - 0.3f, k * 19.2f), new Vector3(10f, 0.6f, 5f), concrete);
                    ramp.transform.rotation = Quaternion.Euler(k * 12f, 0f, 0f);
                }
                var front = Cube("RampFront", b.transform, baseAt + new Vector3(-side * 17.3f, BaseHeight * 0.5f - 0.3f, 0f), new Vector3(5f, 0.6f, 12f), concrete);
                front.transform.rotation = Quaternion.Euler(0f, 0f, side * 12f);
                Cube("Trim", b.transform, baseAt + new Vector3(0f, BaseHeight + 0.02f, 0f), new Vector3(30.2f, 0.06f, 1f), teamMat, false);
                // Flag stand.
                Vector3 standAt = baseAt + new Vector3(side * 4f, BaseHeight, 0f);
                Primitive(PrimitiveType.Cylinder, "FlagStand", b.transform, standAt + Vector3.up * 0.15f, new Vector3(3.4f, 0.15f, 3.4f), teamMat);
                var stand = Empty("FlagPoint", mapGo.transform, standAt + Vector3.up * 0.3f);
                map.flagStands[t] = stand.transform;
                var sign = WorldText("BaseSign", b.transform, baseAt + new Vector3(side * 12f, 9f, 0f), $"{CTF_Rules.Name(team)} BASE", 8f, CTF_Rules.Color(team), new Vector2(26f, 5f), team == CTF_Team.Red ? 90f : -90f);
                sign.fontStyle = FontStyles.Bold;
                sign.gameObject.AddComponent<Billboard>().yawOnly = true;
                // Buildings behind the flag, guard posts at the corners.
                Cover("Building_Prefebs/MilitaryBase_Prefeb.prefab", b.transform, baseAt + new Vector3(side * 12.5f, BaseHeight, 10f), 11f, team == CTF_Team.Red ? 90f : -90f, concrete);
                Cover("Building_Prefebs/Radar_Prefeb.prefab", b.transform, baseAt + new Vector3(side * 12f, BaseHeight, -11f), 7f, 0f, concrete);
                Cover("Building_Prefebs/GuardPoint_01_Prefeb.prefab", b.transform, baseAt + new Vector3(-side * 12f, BaseHeight, 14f), 5f, team == CTF_Team.Red ? 90f : -90f, concrete);
                Cover("Building_Prefebs/GuardPoint_01_Prefeb.prefab", b.transform, baseAt + new Vector3(-side * 12f, BaseHeight, -14f), 5f, team == CTF_Team.Red ? 90f : -90f, concrete);
                for (int k = 0; k < 6; k++) {
                    var s = Empty($"{CTF_Rules.Name(team)}Spawn{k}", mapGo.transform, baseAt + new Vector3(side * 8f, BaseHeight, -10f + k * 4f));
                    spawns[t].Add(s.transform);
                }
            }
            map.redSpawns = spawns[0].ToArray();
            map.blueSpawns = spawns[1].ToArray();
            map.lobbySpawn = Empty("LobbySpawn", mapGo.transform, new Vector3(0f, 0.5f, -3f)).transform;
            map.killY = -15f;
            map.flagPrefabs = flags;
            map.clientPrefab = hud;

            // ---- midfield cover, point-symmetric so neither side is favoured
            var mid = Empty("Midfield", world.transform);
            Cover("Building_Prefebs/ControlTower_Prefeb.prefab", mid.transform, new Vector3(0f, 0f, 22f), 14f, 0f, concrete);
            Cover("Building_Prefebs/ControlTower_Prefeb.prefab", mid.transform, new Vector3(0f, 0f, -22f), 14f, 180f, concrete);
            Mirrored((p, y) => Cover("Building_Prefebs/VehicleHangar_01_Prefeb.prefab", mid.transform, p, 16f, y, concrete), new Vector3(-40f, 0f, 34f), 0f);
            Mirrored((p, y) => Cover("Building_Prefebs/OilTank_Prefeb.prefab", mid.transform, p, 8f, y, concrete), new Vector3(-24f, 0f, -36f), 0f);
            Mirrored((p, y) => Cover("Tank_Prefebs/Tank_01_Prefeb.prefab", mid.transform, p, 8f, y, concrete), new Vector3(-14f, 0f, 8f), 30f);
            Mirrored((p, y) => Cover("APC_Prefebs/APC_01_Prefeb.prefab", mid.transform, p, 7f, y, concrete), new Vector3(-50f, 0f, -12f), 70f);
            Mirrored((p, y) => Cover("Building_Prefebs/GuardPoint_02_Prefeb.prefab", mid.transform, p, 6f, y, concrete), new Vector3(-58f, 0f, 22f), 90f);
            // Sandbag walls and crate stacks.
            var cover = new (Vector3 p, Vector3 s, float yaw)[] {
                (new(-30f, 0f, 0f), new(8f, 1.4f, 1.6f), 90f), (new(-20f, 0f, -14f), new(7f, 1.4f, 1.6f), 20f),
                (new(-44f, 0f, 10f), new(6f, 1.4f, 1.6f), 0f), (new(-62f, 0f, -26f), new(7f, 1.4f, 1.6f), 70f),
                (new(-8f, 0f, -6f), new(5f, 1.4f, 1.6f), 0f), (new(-36f, 0f, -28f), new(6f, 1.4f, 1.6f), 45f),
                (new(-66f, 0f, 6f), new(6f, 1.4f, 1.6f), 90f),
            };
            foreach (var c in cover)
                Mirrored((p, y) => Cube("Sandbags", mid.transform, p + Vector3.up * c.s.y * 0.5f, c.s, sandbag).transform.rotation = Quaternion.Euler(0f, y, 0f), c.p, c.yaw);
            var crates = new[] { new Vector3(-26f, 0f, 16f), new Vector3(-12f, 0f, 30f), new Vector3(-54f, 0f, 40f), new Vector3(-46f, 0f, -40f), new Vector3(-6f, 0f, -40f), new Vector3(-70f, 0f, -44f) };
            var rnd = new System.Random(7);
            foreach (var at in crates)
                Mirrored((p, y) => {
                    var stack = Empty("Crates", mid.transform, p);
                    for (int k = 0; k < 3; k++) {
                        float s = 2.2f;
                        Vector3 o = k < 2 ? new Vector3(k * 2.4f, s * 0.5f, 0f) : new Vector3(1.2f, s * 1.5f, 0f);
                        Cube("Crate", stack.transform, Quaternion.Euler(0f, y, 0f) * o, Vector3.one * s, crate).transform.rotation = Quaternion.Euler(0f, y + (float)rnd.NextDouble() * 10f, 0f);
                    }
                }, at, 0f);
            Mirrored((p, y) => Cover("Building_Prefebs/LightTower_Prefeb.prefab", mid.transform, p, 12f, y, concrete), new Vector3(-100f, 0f, 52f), 0f);
            Mirrored((p, y) => Cover("Building_Prefebs/LightTower_Prefeb.prefab", mid.transform, p, 12f, y, concrete), new Vector3(-100f, 0f, -52f), 0f);
            var title = WorldText("Title", world.transform, new Vector3(0f, 18f, 0f), "FLAG WARS\n<size=45%>Take their flag. Bring it home. First to 3.</size>", 9f, new Color(1f, 0.85f, 0.25f), new Vector2(30f, 7f));
            title.gameObject.AddComponent<Billboard>();
            EditorUtility.SetDirty(map);
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
            for (int i = 0; i < 2; i++) PrefabUtility.InstantiatePrefab(map.flagPrefabs[i], map.transform).GetType();
            var placed = map.GetComponentsInChildren<CTF_FlagView>();
            for (int i = 0; i < placed.Length && i < 2; i++) placed[i].transform.position = map.flagStands[i].position;
            string dir = PreviewDir(Universe);
            var cam = PreviewCamera();
            Shoot(cam, new Vector3(0f, 110f, -120f), Vector3.zero, Path.Combine(dir, "overview.png"), 1600, 900);
            Vector3 blue = map.flagStands[1].position;
            Shoot(cam, blue + new Vector3(-26f, 9f, -18f), blue + Vector3.up * 2f, Path.Combine(dir, "base.png"), 1600, 900);
            Shoot(cam, new Vector3(-30f, 6f, -12f), new Vector3(20f, 2f, 6f), Path.Combine(dir, "midfield.png"), 1600, 900);
            Shoot(cam, blue + new Vector3(-30f, 14f, -26f), blue + new Vector3(-8f, 0f, 0f), Path.Combine(dir, "thumbnail.png"), 640, 360);
            Object.DestroyImmediate(cam.gameObject);
            Kit.PublishThumbnail(Universe, Path.Combine(dir, "thumbnail.png"));
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
        }
    }
}
