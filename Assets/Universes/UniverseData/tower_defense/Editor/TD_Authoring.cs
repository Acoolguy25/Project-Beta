using System.IO;
using System.Linq;
using RyanAssets.Authoring;
using RyanAssets.Rounds;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static RyanAssets.Authoring.UniverseAuthoringKit;
#if !UNITY_SERVER
using Universes.UniverseData.tower_defense.Client;
#endif

namespace Universes.UniverseData.tower_defense.Editor {
    /// <summary>
    /// Rebuilds Tower Defense's materials, tower/enemy/slot prefabs (Cartoon Military models), HUD and
    /// start scene. Safe to re-run. Server runner: run BuildServerAssets from the Linux Server profile.
    /// </summary>
    public static class TD_Authoring {
        public const string Universe = "tower_defense";
        public const string Root = "Assets/Universes/UniverseData/tower_defense";
        static readonly UniverseAuthoringKit Kit = new(Root);
        const string ServerPrefab = Root + "/Server/TD_ServerRunner.prefab";
        const string Military = "Assets/CartoonMilitaryModelPack/Prefebs/";

        [MenuItem("Ryan/Tower Defense/Build Client Assets")]
        public static void BuildClientAssets() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            Kit.EnsureFolders("Prefabs", "Prefabs/Towers", "Prefabs/Enemies", "Materials", "Textures", "Server");
            BuildTextures();
            BuildScene();
            RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));
            AssetDatabase.SaveAssets();
            Debug.Log("[TowerDefense] Client assets built.");
        }

        public static void BuildClientAssetsBatch() {
            BuildClientAssets();
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) RenderPreviews();
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

        static void BuildTextures() {
            Kit.SaveTexture("Tile", Studded);
            Kit.SaveTexture("Grass", (u, v) => {
                float n = Hash(Mathf.Floor(u * 64f), Mathf.Floor(v * 64f));
                float c = 0.8f + 0.2f * n;
                return new Color(0.48f * c, 0.66f * c, 0.3f * c);
            });
            Kit.SaveTexture("Dirt", (u, v) => {
                float n = Hash(Mathf.Floor(u * 48f), Mathf.Floor(v * 48f));
                float rut = Mathf.Abs(Mathf.Repeat(u * 2f, 1f) - 0.5f) < 0.06f ? 0.85f : 1f;
                float c = (0.85f + 0.15f * n) * rut;
                return new Color(0.62f * c, 0.5f * c, 0.36f * c);
            });
            Kit.SaveTexture("Hazard", (u, v) => Mathf.Repeat(u + v, 0.5f) < 0.25f ? new Color(1f, 0.8f, 0.15f) : new Color(0.12f, 0.12f, 0.14f));
        }

        // ---------------------------------------------------------------- prefabs
        static GameObject Slot(Material concrete, Material idle, Material focused) {
            var root = new GameObject("TowerSlot");
            var view = root.AddComponent<TD_SlotView>();
            Cube("Base", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(4.4f, 0.3f, 4.4f), concrete);
            view.pad = Cube("Pad", root.transform, new Vector3(0f, 0.33f, 0f), new Vector3(3.6f, 0.08f, 3.6f), idle, false).GetComponent<Renderer>();
            view.idle = idle; view.focused = focused;
            view.label = WorldText("Label", root.transform, new Vector3(0f, 1.6f, 0f), "TOWER PAD", 3f, new Color(1f, 0.9f, 0.5f), new Vector2(5f, 1.2f));
            view.label.gameObject.AddComponent<Billboard>();
            return Kit.Save(root, "", "TowerSlot");
        }

        static GameObject Tower(TD_TowerKind kind, string model, float size, Material concrete, Material tint, Material pip) {
            var stats = TD_Rules.Tower(kind);
            var root = new GameObject(stats.name);
            var view = root.AddComponent<TD_TowerView>();
            Cube("Plinth", root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(3.2f, 1.2f, 3.2f), concrete);
            Cube("Band", root.transform, new Vector3(0f, 1.25f, 0f), new Vector3(3.3f, 0.12f, 3.3f), tint, false);
            var head = Empty("Head", root.transform, new Vector3(0f, 1.3f, 0f));
            view.head = head.transform;
            var turret = Model(Military + "CustomPart_Prefebs/Turrets_Prefebs/" + model, head.transform, size);
            if (turret == null) Cube("Gun", head.transform, new Vector3(0f, 0.8f, 0.8f), new Vector3(0.6f, 0.6f, 2.2f), tint, false);
            else if (kind == TD_TowerKind.Frost)
                foreach (var r in turret.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => tint).ToArray();
            view.muzzle = Empty("Muzzle", head.transform, new Vector3(0f, size * 0.45f, size * 0.55f)).transform;
            view.levelPips = new GameObject[TD_Rules.MaxLevel];
            for (int i = 0; i < TD_Rules.MaxLevel; i++)
                view.levelPips[i] = Cube($"Pip{i}", root.transform, new Vector3(-0.6f + i * 0.6f, 1.25f, 1.66f), new Vector3(0.4f, 0.25f, 0.05f), pip, false);
            view.label = WorldText("Label", root.transform, new Vector3(0f, size + 2.6f, 0f), stats.name, 3f, Color.white, new Vector2(6f, 1.6f));
            view.label.gameObject.AddComponent<Billboard>();
            var audio = Empty("FireSound", root.transform, new Vector3(0f, 2f, 0f)).AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.maxDistance = 60f;
            audio.volume = kind == TD_TowerKind.Gunner ? 0.25f : 0.5f;
            audio.clip = Clip("Packages/com.ryan.gameframework/Runtime/Tools/Audio/pistol-gun-shot.mp3");
            view.fireSound = audio;
            return Kit.Save(root, "Towers", stats.name);
        }

        static GameObject Enemy(TD_EnemyKind kind, string model, float size, Material barBack, Material barFill, Material frost) {
            var stats = TD_Rules.Enemy(kind);
            var root = new GameObject(stats.name);
            var view = root.AddComponent<TD_EnemyView>();
            var body = Empty("Body", root.transform);
            view.body = body.transform;
            if (Model(Military + model, body.transform, size) == null)
                Cube("Hull", body.transform, new Vector3(0f, size * 0.25f, 0f), new Vector3(size * 0.5f, size * 0.4f, size), barFill, false);
            NoShadows(body);
            var bar = Empty("HealthBar", root.transform, new Vector3(0f, size * 0.7f + 1.2f, 0f));
            bar.AddComponent<Billboard>();
            NoShadows(Cube("Back", bar.transform, Vector3.zero, new Vector3(2.4f, 0.28f, 0.05f), barBack, false));
            var pivot = Empty("Pivot", bar.transform, new Vector3(-1.15f, 0f, -0.04f));
            NoShadows(Cube("Fill", pivot.transform, new Vector3(1.15f, 0f, 0f), new Vector3(2.3f, 0.2f, 0.05f), barFill, false));
            view.healthFill = pivot.transform;
            var ice = Primitive(PrimitiveType.Sphere, "Frost", root.transform, new Vector3(0f, size * 0.25f, 0f), Vector3.one * size * 0.9f, frost, false);
            NoShadows(ice);
            ice.SetActive(false);
            view.slowTint = ice.GetComponent<Renderer>();
            return Kit.Save(root, "Enemies", stats.name);
        }

        static GameObject Tracer(Material glow) {
            var go = new GameObject("Tracer");
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.widthMultiplier = 0.12f;
            line.sharedMaterial = glow;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.numCapVertices = 2;
            return Kit.Save(go, "", "Tracer");
        }

        // ---------------------------------------------------------------- HUD
        static GameObject BuildHud() {
#if UNITY_SERVER
            return AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/TowerDefenseHUD.prefab");
#else
            var root = Canvas("TowerDefenseHUD");
            var hud = root.AddComponent<TD_ClientController>();
            Color white = new(0.97f, 0.98f, 1f), yellow = new(1f, 0.85f, 0.25f), money = new(0.45f, 1f, 0.55f);
            var top = Panel("TopPanel", root.transform, new Vector2(0.33f, 0.79f), new Vector2(0.67f, 0.9f), Ink);
            hud.waveLabel = Text("Wave", top.transform, new Vector2(0.03f, 0.5f), new Vector2(0.97f, 0.98f), 30, yellow, TextAlignmentOptions.Center, true);
            hud.cashLabel = Text("Cash", top.transform, new Vector2(0.03f, 0.05f), new Vector2(0.4f, 0.5f), 30, money, TextAlignmentOptions.MidlineLeft, true);
            var barBack = Panel("BaseBar", top.transform, new Vector2(0.42f, 0.12f), new Vector2(0.97f, 0.42f), new Color(0.3f, 0.05f, 0.05f, 0.9f));
            hud.baseFill = Panel("Fill", barBack.transform, Vector2.zero, Vector2.one, new Color(0.3f, 0.9f, 0.4f, 0.95f));
            hud.baseFill.type = UnityEngine.UI.Image.Type.Filled;
            hud.baseFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            hud.baseFill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            hud.baseLabel = Text("Base", barBack.transform, Vector2.zero, Vector2.one, 20, white, TextAlignmentOptions.Center, true);
            hud.headlineLabel = Text("Headline", root.transform, new Vector2(0.25f, 0.73f), new Vector2(0.75f, 0.78f), 22, white, TextAlignmentOptions.Center);
            hud.promptLabel = Text("Prompt", root.transform, new Vector2(0.25f, 0.22f), new Vector2(0.75f, 0.28f), 30, white, TextAlignmentOptions.Center, true);
            hud.toastLabel = Text("Toast", root.transform, new Vector2(0.2f, 0.62f), new Vector2(0.8f, 0.71f), 38, yellow, TextAlignmentOptions.Center, true);
            hud.bannerLabel = Text("Banner", root.transform, new Vector2(0.15f, 0.4f), new Vector2(0.85f, 0.62f), 96, yellow, TextAlignmentOptions.Center, true);
            int n = TD_Rules.Towers.Length;
            hud.towerButtons = new TMP_Text[n];
            hud.towerButtonBacks = new UnityEngine.UI.Image[n];
            for (int i = 0; i < n; i++) {
                float x0 = 0.2f + i * 0.152f;
                var back = Panel($"Tower{i + 1}", root.transform, new Vector2(x0, 0.03f), new Vector2(x0 + 0.145f, 0.13f), hud.idleColor);
                hud.towerButtonBacks[i] = back;
                hud.towerButtons[i] = Text("Label", back.transform, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f), 21, white, TextAlignmentOptions.Center);
            }
            hud.sfx = Sfx(root.transform);
            hud.buildClip = hud.waveClip = Clip(CoinClip);
            hud.deniedClip = hud.leakClip = Clip(LandClip);
            return Kit.Save(root, "", "TowerDefenseHUD");
#endif
        }

        // ---------------------------------------------------------------- scene
        static void BuildScene() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var tile = Kit.Tex("Tile");
            var concrete = Kit.Mat("Concrete", new Color(0.7f, 0.71f, 0.72f), tile, smoothness: 0.2f);
            var idle = Kit.Mat("PadIdle", new Color(0.35f, 0.8f, 1f), tile, 0.6f);
            var focused = Kit.Mat("PadFocused", new Color(1f, 0.85f, 0.3f), tile, 1.3f);
            var barBack = Kit.Mat("BarBack", new Color(0.15f, 0.05f, 0.05f), emission: 0.2f);
            var barFill = Kit.Mat("BarFill", new Color(0.3f, 1f, 0.35f), emission: 0.8f);
            var frost = Kit.Mat("FrostShell", new Color(0.55f, 0.85f, 1f), emission: 0.6f, alpha: 0.3f);
            var pip = Kit.Mat("Pip", new Color(1f, 0.8f, 0.2f), emission: 1.2f);
            var glow = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            string glowPath = Root + "/Materials/Tracer.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(glowPath) == null) AssetDatabase.CreateAsset(glow, glowPath);
            glow = AssetDatabase.LoadAssetAtPath<Material>(glowPath);
            glow.SetColor("_BaseColor", Color.white);
            Material[] tints = {
                Kit.Mat("TintGunner", new Color(0.95f, 0.8f, 0.3f), emission: 0.4f),
                Kit.Mat("TintCannon", new Color(0.95f, 0.45f, 0.25f), emission: 0.4f),
                Kit.Mat("TintFrost", new Color(0.55f, 0.85f, 1f), emission: 0.5f, smoothness: 0.8f),
                Kit.Mat("TintSniper", new Color(0.9f, 0.3f, 0.35f), emission: 0.4f)
            };
            var grass = Tiled(Kit.Mat("Grass", Color.white, Kit.Tex("Grass"), smoothness: 0.1f), 10f, 7f);
            var dirt = Kit.Mat("Dirt", Color.white, Kit.Tex("Dirt"), smoothness: 0.05f);
            var hazard = Kit.Mat("Hazard", Color.white, Kit.Tex("Hazard"), 0.2f);

            var slot = Slot(concrete, idle, focused);
            var towers = new[] {
                Tower(TD_TowerKind.Gunner, "Turret_01_Prefeb.prefab", 3.2f, concrete, tints[0], pip),
                Tower(TD_TowerKind.Cannon, "Turret_05_Prefeb.prefab", 3.8f, concrete, tints[1], pip),
                Tower(TD_TowerKind.Frost, "Turret_08_Prefeb.prefab", 3.4f, concrete, tints[2], pip),
                Tower(TD_TowerKind.Sniper, "Turret_10_Prefeb.prefab", 4.2f, concrete, tints[3], pip),
            };
            var enemies = new[] {
                Enemy(TD_EnemyKind.Scout, "APC_Prefebs/APC_01_Prefeb.prefab", 3.6f, barBack, barFill, frost),
                Enemy(TD_EnemyKind.Apc, "APC_Prefebs/APC_03_Prefeb.prefab", 4.6f, barBack, barFill, frost),
                Enemy(TD_EnemyKind.Tank, "Tank_Prefebs/Tank_01_Prefeb.prefab", 5.2f, barBack, barFill, frost),
                Enemy(TD_EnemyKind.Chopper, "Chopper_Prefebs/Chopper_01_Prefeb.prefab", 6f, barBack, barFill, frost),
                Enemy(TD_EnemyKind.Boss, "Tank_Prefebs/Tank_05_Prefeb.prefab", 8.5f, barBack, barFill, frost),
            };
            var tracer = Tracer(glow);
            var hud = BuildHud();

            Kit.NewLitScene(new Color(0.5f, 0.7f, 1f), new Color(0.75f, 0.84f, 0.95f), 160f, 450f, new Vector3(48f, 35f, 0f));
            var world = new GameObject("World");
            Cube("Ground", world.transform, new Vector3(0f, -0.5f, 0f), new Vector3(220f, 1f, 160f), grass);
            var lane = TD_Rules.Lane;
            for (int i = 0; i + 1 < lane.Length; i++) {
                Vector3 a = lane[i], b = lane[i + 1], mid = (a + b) * 0.5f;
                var seg = Cube($"Road{i}", world.transform, mid + new Vector3(0f, 0.02f, 0f), new Vector3(8f, 0.06f, Vector3.Distance(a, b) + 8f), dirt, false);
                seg.transform.rotation = Quaternion.LookRotation(b - a);
            }
            Vector3 start = lane[0], end = lane[^1];
            Place("Building_Prefebs/VehicleHangar_01_Prefeb.prefab", world.transform, start - (lane[1] - start).normalized * 10f, 16f, Yaw(lane[1] - start));
            Place("Building_Prefebs/MilitaryBase_Prefeb.prefab", world.transform, end + (end - lane[^2]).normalized * 12f, 22f, Yaw(lane[^2] - end));
            var flag = WorldText("BaseSign", world.transform, end + (end - lane[^2]).normalized * 12f + Vector3.up * 16f, "DEFEND THE BASE", 8f, new Color(1f, 0.85f, 0.3f), new Vector2(24f, 3f));
            flag.gameObject.AddComponent<Billboard>();
            var stripe = Cube("FinishLine", world.transform, end + new Vector3(0f, 0.05f, 0f), new Vector3(9f, 0.08f, 1.5f), hazard, false);
            stripe.transform.rotation = Quaternion.LookRotation(end - lane[^2]);
            string[] props = { "Building_Prefebs/OilTank_Prefeb.prefab", "Building_Prefebs/LightTower_Prefeb.prefab", "Building_Prefebs/Radar_Prefeb.prefab", "Building_Prefebs/GuardPoint_01_Prefeb.prefab" };
            Vector3[] propSpots = { new(-90f, 0f, 60f), new(-90f, 0f, -60f), new(90f, 0f, 60f), new(-20f, 0f, 62f), new(55f, 0f, 65f), new(-95f, 0f, 0f), new(95f, 0f, -60f), new(15f, 0f, -62f) };
            for (int i = 0; i < propSpots.Length; i++) Place(props[i % props.Length], world.transform, propSpots[i], 9f + i % 3 * 3f, i * 47f);

            var mapGo = new GameObject("TowerDefenseMap");
            var map = mapGo.AddComponent<TD_Map>();
            map.playerSpawn = Empty("PlayerSpawn", mapGo.transform, end + new Vector3(-8f, 0f, -12f)).transform;
            map.towerPrefabs = towers;
            map.enemyPrefabs = enemies;
            map.slotPrefab = slot;
            map.tracerPrefab = tracer;
            map.explosionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/vfx_Explosion_01.prefab");
            map.clientPrefab = hud;
            EditorUtility.SetDirty(map);
            Kit.SaveActiveScene(Kit.StartScene(Universe));
        }

        static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        static void Place(string model, Transform parent, Vector3 position, float size, float yaw) {
            var anchor = Empty(Path.GetFileNameWithoutExtension(model), parent, position);
            Model(Military + model, anchor.transform, size, yaw: yaw, keepColliders: true);
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
            int[] built = { 0, 2, 5, 6, 8, 9, 12, 13 };
            for (int i = 0; i < TD_Rules.Slots.Length; i++) {
                var prefab = System.Array.IndexOf(built, i) is int k && k >= 0 ? map.towerPrefabs[k % map.towerPrefabs.Length] : map.slotPrefab;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, map.transform);
                go.transform.position = TD_Rules.Slots[i];
            }
            var kinds = new[] { TD_EnemyKind.Scout, TD_EnemyKind.Scout, TD_EnemyKind.Apc, TD_EnemyKind.Tank, TD_EnemyKind.Chopper, TD_EnemyKind.Apc, TD_EnemyKind.Boss, TD_EnemyKind.Scout };
            for (int i = 0; i < kinds.Length; i++) {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(map.enemyPrefabs[(int)kinds[i]], map.transform);
                Vector3 p = TD_Rules.EnemyPosition(kinds[i], 40f + i * 22f, out Vector3 f);
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(f));
            }
            var cam = PreviewCamera();
            Shoot(cam, new Vector3(-20f, 95f, -110f), new Vector3(0f, 0f, 0f), Path.Combine(dir, "overview.png"), 1600, 900);
            Shoot(cam, new Vector3(-62f, 14f, 50f), new Vector3(-40f, 2f, 10f), Path.Combine(dir, "lane.png"), 1600, 900);
            Shoot(cam, new Vector3(-75f, 22f, 55f), new Vector3(-35f, 0f, 5f), Path.Combine(dir, "thumbnail.png"), 640, 360);
            Object.DestroyImmediate(cam.gameObject);
            Kit.PublishThumbnail(Universe, Path.Combine(dir, "thumbnail.png"));
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
        }
    }
}
