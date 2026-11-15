using System.IO;
using System.Linq;
using RyanAssets.Authoring;
using RyanAssets.Platformer;
using RyanAssets.Rounds;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static RyanAssets.Authoring.UniverseAuthoringKit;
#if !UNITY_SERVER
using Universes.UniverseData.tycoon.Client;
#endif

namespace Universes.UniverseData.tycoon.Editor {
    /// <summary>
    /// Rebuilds Brick Tycoon's textures, materials, item/button prefabs, plot bases, HUD and start scene.
    /// Safe to re-run (assets are overwritten in place). Build the server runner from an Editor in the
    /// Linux Server profile: <c>-executeMethod Universes.UniverseData.tycoon.Editor.TY_Authoring.BuildServerAssets</c>.
    /// </summary>
    public static class TY_Authoring {
        public const string Universe = "tycoon";
        public const string Root = "Assets/Universes/UniverseData/tycoon";
        static readonly UniverseAuthoringKit Kit = new(Root);
        const string ServerPrefab = Root + "/Server/TY_ServerRunner.prefab";
        const float Radius = 64f;
        const int PlotCount = 6;

        static readonly Color[] PlotColors = {
            new(0.31f, 0.62f, 1f), new(0.98f, 0.42f, 0.36f), new(0.36f, 0.84f, 0.45f),
            new(1f, 0.78f, 0.25f), new(0.72f, 0.5f, 1f), new(0.25f, 0.85f, 0.82f)
        };

        [MenuItem("Ryan/Tycoon/Build Client Assets")]
        public static void BuildClientAssets() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            Kit.EnsureFolders("Prefabs", "Prefabs/Items", "Materials", "Textures", "Server");
            BuildTextures();
            var p = BuildPrefabs();
            BuildStartScene(p);
            RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));
            AssetDatabase.SaveAssets();
            Debug.Log("[Tycoon] Client assets built.");
        }

        public static void BuildClientAssetsBatch() {
            BuildClientAssets();
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) RenderPreviews();
        }

        [MenuItem("Ryan/Tycoon/Build Server Assets (Linux Server profile)")]
        public static void BuildServerAssets() {
#if UNITY_SERVER
            Kit.EnsureFolders("Server");
            Kit.BuildServerRunner<Universes.UniverseData.tycoon.Server.TY_ServerRunner>(ServerPrefab, Kit.ServerScene(Universe));
            RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));
            Debug.Log("[Tycoon] Server assets built.");
#else
            throw new System.InvalidOperationException("Switch to the Linux Server build profile (UNITY_SERVER) to build the server runner prefab.");
#endif
        }

        // ---------------------------------------------------------------- textures
        static void BuildTextures() {
            Kit.SaveTexture("Tile", Studded);
            Kit.SaveTexture("Grass", (u, v) => {
                float n = Hash(Mathf.Floor(u * 64f), Mathf.Floor(v * 64f));
                float c = 0.82f + 0.18f * n;
                return new Color(0.42f * c, 0.74f * c, 0.32f * c);
            });
            Kit.SaveTexture("Arrows", (u, v) => {
                float x = Mathf.Abs(v - 0.5f);
                float chevron = Mathf.Repeat(u - x * 0.9f, 0.5f);
                return chevron < 0.18f && x < 0.38f ? new Color(1f, 0.85f, 0.2f) : new Color(0.16f, 0.17f, 0.2f);
            });
            Kit.SaveTexture("Brick", (u, v) => {
                float row = Mathf.Floor(v * 8f);
                float x = Mathf.Repeat(u * 4f + (row % 2) * 0.5f, 1f);
                float y = Mathf.Repeat(v * 8f, 1f);
                bool mortar = x < 0.05f || y < 0.1f;
                float n = 0.9f + 0.1f * Hash(Mathf.Floor(u * 4f + (row % 2) * 0.5f), row);
                return mortar ? new Color(0.82f, 0.8f, 0.76f) : new Color(0.72f * n, 0.3f * n, 0.22f * n);
            });
            Kit.SaveTexture("Stripes", (u, v) => Mathf.Repeat(u + v, 0.5f) < 0.25f ? new Color(1f, 0.82f, 0.1f) : new Color(0.12f, 0.12f, 0.14f));
        }

        sealed class Prefabs {
            public GameObject plotBase, button, claim, ore;
            public GameObject[] items;
            public Material[] plotMats, oreMats, floorMats;
            public Material grass, plaza, stone;
        }

        // ---------------------------------------------------------------- prefabs
        static Prefabs BuildPrefabs() {
            var tile = Kit.Tex("Tile");
            var p = new Prefabs {
                plotMats = PlotColors.Select((c, i) => Kit.Mat($"Plot{i}", c, tile, smoothness: 0.35f)).ToArray(),
                oreMats = new[] {
                    Kit.Mat("Ore0", new Color(0.6f, 0.62f, 0.66f), tile, smoothness: 0.4f),
                    Kit.Mat("Ore1", new Color(0.35f, 0.95f, 0.55f), tile, 0.5f, smoothness: 0.6f),
                    Kit.Mat("Ore2", new Color(1f, 0.8f, 0.2f), tile, 0.9f, smoothness: 0.85f, metallic: 0.7f)
                },
                grass = Tiled(Kit.Mat("Grass", Color.white, Kit.Tex("Grass"), smoothness: 0.1f), 8f, 8f),
                plaza = Tiled(Kit.Mat("Plaza", new Color(0.86f, 0.88f, 0.92f), tile, smoothness: 0.3f), 8f, 8f),
                stone = Tiled(Kit.Mat("Stone", new Color(0.55f, 0.57f, 0.62f), tile, smoothness: 0.2f), 2f, 8f)
            };
            p.floorMats = PlotColors.Select((c, i) => Tiled(Kit.Mat($"PlotFloor{i}", Color.Lerp(c, Color.white, 0.25f), tile, smoothness: 0.3f), 10f, 10f)).ToArray();
            var belt = Tiled(Kit.Mat("Belt", Color.white, Kit.Tex("Arrows"), 0.15f, smoothness: 0.2f), 17f, 1f);
            var brickWall = Tiled(Kit.Mat("BrickWall", Color.white, Kit.Tex("Brick")), 10f, 1f);
            var steel = Kit.Mat("Steel", new Color(0.55f, 0.58f, 0.64f), smoothness: 0.7f, metallic: 0.8f);
            var dark = Kit.Mat("Dark", new Color(0.15f, 0.16f, 0.19f), smoothness: 0.4f);
            var lava = Kit.Mat("FurnaceGlow", new Color(1f, 0.45f, 0.1f), emission: 2.2f);
            var brick = Kit.Mat("Brick", Color.white, Kit.Tex("Brick"));
            var gold = Kit.Mat("Gold", new Color(1f, 0.78f, 0.2f), emission: 0.35f, smoothness: 0.85f, metallic: 0.95f);
            var green = Kit.Mat("ButtonGreen", new Color(0.25f, 0.9f, 0.35f), tile, 0.7f);
            var red = Kit.Mat("ButtonRed", new Color(0.95f, 0.3f, 0.25f), tile, 0.5f);
            var grey = Kit.Mat("ButtonGrey", new Color(0.6f, 0.62f, 0.66f), tile, 0.15f);
            var claimMat = Kit.Mat("Claim", new Color(0.3f, 1f, 0.6f), tile, 1.4f);
            var collectMat = Kit.Mat("Collect", new Color(0.3f, 0.75f, 1f), tile, 1.1f);
            var wood = Kit.Mat("Wood", new Color(0.55f, 0.36f, 0.2f), smoothness: 0.2f);
            var leaves = Kit.Mat("Leaves", new Color(0.3f, 0.7f, 0.3f), tile, smoothness: 0.15f);
            var glass = Kit.Mat("Glow", new Color(1f, 0.95f, 0.7f), emission: 1.6f);
            var upgradeGlow = Kit.Mat("UpgradeField", new Color(0.35f, 1f, 0.55f), emission: 1.2f, alpha: 0.35f);
            var washGlow = Kit.Mat("WashField", new Color(0.35f, 0.75f, 1f), emission: 1.4f, alpha: 0.35f);
            var stripes = Kit.Mat("Stripes", Color.white, Kit.Tex("Stripes"));

            {   // Plot base: floor, belt, furnace, collector, owner sign, claim pad.
                var root = new GameObject("PlotBase");
                var view = root.AddComponent<TY_PlotView>();
                Cube("Floor", root.transform, new Vector3(0f, -0.25f, 0f), new Vector3(TY_Catalog.PlotSize, 0.5f, TY_Catalog.PlotSize), p.floorMats[0]);
                Cube("Trim", root.transform, new Vector3(0f, -0.3f, 0f), new Vector3(TY_Catalog.PlotSize + 1.2f, 0.5f, TY_Catalog.PlotSize + 1.2f), p.plotMats[0]);
                float len = TY_Catalog.BeltEndX - TY_Catalog.BeltStartX + 2f;
                float mid = (TY_Catalog.BeltEndX + TY_Catalog.BeltStartX) * 0.5f + 1f;
                Cube("Belt", root.transform, new Vector3(mid, TY_Catalog.BeltTop * 0.5f, TY_Catalog.BeltZ), new Vector3(len, TY_Catalog.BeltTop, 1.6f), belt);
                for (int s = -1; s <= 1; s += 2)
                    Cube($"Rail{s}", root.transform, new Vector3(mid, TY_Catalog.BeltTop + 0.15f, TY_Catalog.BeltZ + s * 0.9f), new Vector3(len, 0.3f, 0.2f), steel);
                var furnace = Empty("Furnace", root.transform, TY_Catalog.FurnacePosition);
                Cube("Body", furnace.transform, new Vector3(0.5f, 2f, 0f), new Vector3(3.4f, 4f, 3.6f), brick);
                Cube("Mouth", furnace.transform, new Vector3(-1.25f, 1.1f, 0f), new Vector3(0.1f, 1.4f, 2f), lava, false);
                Cube("Chimney", furnace.transform, new Vector3(1.2f, 5f, 1.1f), new Vector3(0.9f, 2.5f, 0.9f), brick);
                var collect = Cube("CollectPad", root.transform, TY_Catalog.CollectPosition + new Vector3(0f, 0.1f, 0f), new Vector3(TY_Catalog.PadSize.x, 0.2f, TY_Catalog.PadSize.z), collectMat);
                view.bankLabel = WorldText("Bank", root.transform, TY_Catalog.CollectPosition + new Vector3(0f, 2.6f, 0f), "$0", 5f, new Color(0.6f, 0.95f, 1f), new Vector2(8f, 3f));
                view.bankLabel.gameObject.AddComponent<Billboard>();
                var sign = Empty("OwnerSign", root.transform, new Vector3(0f, 0f, TY_Catalog.PlotSize * 0.5f + 1.2f));
                Cube("PostL", sign.transform, new Vector3(-5.5f, 2.5f, 0f), new Vector3(0.4f, 5f, 0.4f), dark);
                Cube("PostR", sign.transform, new Vector3(5.5f, 2.5f, 0f), new Vector3(0.4f, 5f, 0.4f), dark);
                Cube("Board", sign.transform, new Vector3(0f, 5.4f, 0f), new Vector3(11.8f, 2.2f, 0.3f), p.plotMats[0], false);
                view.ownerLabel = WorldText("Owner", sign.transform, new Vector3(0f, 5.4f, 0.2f), "FREE PLOT", 6f, Color.white, new Vector2(11f, 2f), 180f);
                var claim = Empty("ClaimPad", root.transform, TY_Catalog.ClaimPosition);
                Cube("Pad", claim.transform, new Vector3(0f, 0.1f, 0f), new Vector3(TY_Catalog.PadSize.x, 0.2f, TY_Catalog.PadSize.z), claimMat);
                WorldText("Label", claim.transform, new Vector3(0f, 2.2f, 0f), "CLAIM\n<size=60%>step here</size>", 5f, new Color(0.6f, 1f, 0.7f), new Vector2(6f, 3f)).gameObject.AddComponent<Billboard>();
                view.claimPad = claim;
                view.itemRoot = Empty("Items", root.transform).transform;
                p.plotBase = Kit.Save(root, "", "PlotBase");
            }
            {
                var root = new GameObject("BuyButton");
                var view = root.AddComponent<TY_ButtonView>();
                view.pad = Cube("Pad", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(TY_Catalog.ButtonSize.x, 0.3f, TY_Catalog.ButtonSize.z), green).GetComponent<Renderer>();
                Cube("Rim", root.transform, new Vector3(0f, 0.07f, 0f), new Vector3(TY_Catalog.ButtonSize.x + 0.4f, 0.14f, TY_Catalog.ButtonSize.z + 0.4f), dark);
                view.label = WorldText("Label", root.transform, new Vector3(0f, 2.3f, 0f), "Item\n$0", 4f, Color.white, new Vector2(7f, 3f));
                view.label.gameObject.AddComponent<Billboard>();
                view.affordable = green; view.expensive = red; view.locked = grey;
                p.button = Kit.Save(root, "", "BuyButton");
            }
            {
                var ore = Cube("Ore", null, Vector3.zero, Vector3.one, p.oreMats[0], false);
                ore.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                p.ore = Kit.Save(ore, "", "Ore");
            }

            p.items = new GameObject[TY_Catalog.Count];
            for (int i = 0; i < TY_Catalog.Count; i++) {
                var item = TY_Catalog.Items[i];
                string name = item.name.Replace(" ", "");
                switch (item.kind) {
                    case TY_ItemKind.Dropper: p.items[i] = Dropper(name, i, item, steel, dark, PlotTint(i)); break;
                    case TY_ItemKind.Upgrader: p.items[i] = Upgrader(name, item.value >= 2 ? washGlow : upgradeGlow, steel, stripes, item.value >= 2); break;
                }
            }
            p.items[Index("Walls")] = Walls(brickWall, dark);
            p.items[Index("Lamp Posts")] = Lamps(dark, glass);
            p.items[Index("Front Gate")] = Gate(gold, brick);
            p.items[Index("Garden")] = Garden(wood, leaves);
            p.items[Index("Lookout Tower")] = Tower(wood, brick, p.plotMats[3]);
            p.items[Index("Golden Statue")] = Statue(gold, p.stone);
            return p;
        }

        static int Index(string name) => System.Array.FindIndex(TY_Catalog.Items, i => i.name == name);

        static Material PlotTint(int item) {
            Color[] tiers = { new(0.75f, 0.78f, 0.84f), new(0.45f, 0.7f, 1f), new(0.45f, 0.9f, 0.5f), new(1f, 0.7f, 0.3f), new(0.95f, 0.4f, 0.4f), new(0.75f, 0.45f, 1f), new(1f, 0.85f, 0.25f) };
            int tier = TY_Catalog.Items.Take(item + 1).Count(i => i.kind == TY_ItemKind.Dropper) - 1;
            return Kit.Mat($"DropperTier{tier}", tiers[Mathf.Clamp(tier, 0, tiers.Length - 1)], Kit.Tex("Tile"), tier >= 6 ? 0.4f : 0.1f, smoothness: 0.5f, metallic: tier >= 6 ? 0.6f : 0f);
        }

        /// <summary>Dropper tower behind the belt with an arm and spout over it. Origin on the belt line.</summary>
        static GameObject Dropper(string name, int index, TY_Item item, Material steel, Material dark, Material body) {
            bool mega = item.value >= 100;
            float s = mega ? 1.35f : 1f;
            var root = new GameObject(name);
            Cube("Tower", root.transform, new Vector3(0f, 2f * s, -2.6f), new Vector3(1.4f * s, 4f * s, 1.4f * s), body);
            Cube("Arm", root.transform, new Vector3(0f, 3.6f * s, -1.3f), new Vector3(0.9f * s, 0.7f * s, 2.8f), body);
            Cube("Spout", root.transform, new Vector3(0f, 3.0f * s, 0f), new Vector3(1f * s, 0.6f, 1f * s), dark, false);
            Cube("Cap", root.transform, new Vector3(0f, 4.15f * s, -2.6f), new Vector3(1.7f * s, 0.3f, 1.7f * s), steel);
            var label = WorldText("Label", root.transform, new Vector3(0f, 4.9f * s, -2.6f), $"${item.value / item.interval:0.#}/s", 3.2f, Color.white, new Vector2(4f, 1.2f));
            label.gameObject.AddComponent<Billboard>();
            return Kit.Save(root, "Items", name);
        }

        static GameObject Upgrader(string name, Material field, Material steel, Material stripes, bool washer) {
            var root = new GameObject(name);
            for (int side = -1; side <= 1; side += 2)
                Cube($"Post{side}", root.transform, new Vector3(0f, 1.6f, side * 1.4f), new Vector3(0.5f, 3.2f, 0.4f), stripes);
            Cube("Beam", root.transform, new Vector3(0f, 3.3f, 0f), new Vector3(0.7f, 0.4f, 3.4f), steel);
            var glow = Cube("Field", root.transform, new Vector3(0f, 1.6f, 0f), new Vector3(0.15f, 2.8f, 2.4f), field, false);
            NoShadows(glow);
            if (washer) {
                var ring = Empty("Ring", root.transform, new Vector3(0f, 3.9f, 0f));
                ring.AddComponent<Spinner>().degreesPerSecond = new Vector3(0f, 0f, 160f);
                for (int k = 0; k < 4; k++) {
                    var blade = Cube($"Blade{k}", ring.transform, Vector3.zero, new Vector3(0.15f, 0.25f, 2.2f), steel, false);
                    blade.transform.localRotation = Quaternion.Euler(k * 45f, 0f, 0f);
                }
            }
            WorldText("Label", root.transform, new Vector3(0f, 4.8f, 0f), washer ? "WASHER x2" : "UPGRADER x1.5", 3f, washer ? new Color(0.6f, 0.85f, 1f) : new Color(0.6f, 1f, 0.7f), new Vector2(5f, 1.2f))
                .gameObject.AddComponent<Billboard>();
            return Kit.Save(root, "Items", name);
        }

        static GameObject Walls(Material brick, Material dark) {
            var root = new GameObject("Walls");
            float h = 3f, half = TY_Catalog.PlotSize * 0.5f - 0.3f;
            Cube("Back", root.transform, new Vector3(0f, h * 0.5f, -half), new Vector3(TY_Catalog.PlotSize, h, 0.6f), brick);
            Cube("Left", root.transform, new Vector3(-half, h * 0.5f, 0f), new Vector3(0.6f, h, TY_Catalog.PlotSize), brick);
            Cube("Right", root.transform, new Vector3(half, h * 0.5f, 0f), new Vector3(0.6f, h, TY_Catalog.PlotSize), brick);
            float gap = 5f, seg = (TY_Catalog.PlotSize - gap * 2f) * 0.5f;
            for (int side = -1; side <= 1; side += 2)
                Cube($"Front{side}", root.transform, new Vector3(side * (gap + seg * 0.5f), h * 0.5f, half), new Vector3(seg, h, 0.6f), brick);
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                Cube("Cap", root.transform, new Vector3(c.x * half, h + 0.3f, c.y * half), new Vector3(1.2f, 0.6f, 1.2f), dark);
            return Kit.Save(root, "Items", "Walls");
        }

        static GameObject Lamps(Material post, Material glow) {
            var root = new GameObject("LampPosts");
            float r = TY_Catalog.PlotSize * 0.5f - 2f;
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(-0.3f, 1), new Vector2(0.3f, 1) }) {
                var lamp = Empty("Lamp", root.transform, new Vector3(c.x * r, 0f, c.y * r));
                Primitive(PrimitiveType.Cylinder, "Post", lamp.transform, new Vector3(0f, 2.2f, 0f), new Vector3(0.25f, 2.2f, 0.25f), post);
                NoShadows(Primitive(PrimitiveType.Sphere, "Bulb", lamp.transform, new Vector3(0f, 4.7f, 0f), Vector3.one * 0.8f, glow, false));
            }
            return Kit.Save(root, "Items", "LampPosts");
        }

        static GameObject Gate(Material gold, Material brick) {
            var root = new GameObject("FrontGate");
            for (int side = -1; side <= 1; side += 2)
                Cube($"Pillar{side}", root.transform, new Vector3(side * 5f, 3f, 0f), new Vector3(1.2f, 6f, 1.2f), brick);
            Cube("Arch", root.transform, new Vector3(0f, 6.4f, 0f), new Vector3(11.2f, 0.9f, 1.4f), gold);
            return Kit.Save(root, "Items", "FrontGate");
        }

        static GameObject Garden(Material wood, Material leaves) {
            var root = new GameObject("Garden");
            Vector3[] spots = { new(0f, 0f, 0f), new(4f, 0f, 1f), new(1.5f, 0f, 4f), new(-2f, 0f, 3f), new(3f, 0f, -2.5f) };
            for (int i = 0; i < spots.Length; i++) {
                float h = 2.5f + i % 3 * 0.8f;
                var tree = Empty($"Tree{i}", root.transform, spots[i]);
                Primitive(PrimitiveType.Cylinder, "Trunk", tree.transform, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.45f, h * 0.5f, 0.45f), wood);
                Primitive(PrimitiveType.Sphere, "Crown", tree.transform, new Vector3(0f, h + 1f, 0f), Vector3.one * (2.6f + i % 2 * 0.6f), leaves, false);
            }
            return Kit.Save(root, "Items", "Garden");
        }

        static GameObject Tower(Material wood, Material brick, Material roof) {
            var root = new GameObject("LookoutTower");
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Cube("Leg", root.transform, new Vector3(x * 2f, 4f, z * 2f), new Vector3(0.6f, 8f, 0.6f), wood);
            Cube("Deck", root.transform, new Vector3(0f, 8.2f, 0f), new Vector3(5.6f, 0.4f, 5.6f), wood);
            Cube("Hut", root.transform, new Vector3(0f, 9.6f, 0f), new Vector3(4.6f, 2.4f, 4.6f), brick);
            var top = Cube("Roof", root.transform, new Vector3(0f, 11.3f, 0f), new Vector3(4.3f, 1.2f, 4.3f), roof);
            top.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            for (int k = 0; k < 6; k++)
                Cube($"Step{k}", root.transform, new Vector3(-2.9f, 0.6f + k * 1.3f, -2.4f + k * 0.9f), new Vector3(1.4f, 0.25f, 0.9f), wood);
            return Kit.Save(root, "Items", "LookoutTower");
        }

        static GameObject Statue(Material gold, Material stone) {
            var root = new GameObject("GoldenStatue");
            Cube("Plinth", root.transform, new Vector3(0f, 0.75f, 0f), new Vector3(4f, 1.5f, 4f), stone);
            var figure = Model("Assets/UnityTechnologies/SpaceRobotKyle/Models/KyleRobot.fbx", Empty("Figure", root.transform, new Vector3(0f, 1.5f, 0f)).transform, 4.5f, yaw: 180f);
            if (figure != null) foreach (var r in figure.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => gold).ToArray();
            else Primitive(PrimitiveType.Capsule, "Figure", root.transform, new Vector3(0f, 3.5f, 0f), new Vector3(1.4f, 2f, 1.4f), gold);
            return Kit.Save(root, "Items", "GoldenStatue");
        }

        // ---------------------------------------------------------------- HUD
        static GameObject BuildHud() {
#if UNITY_SERVER
            return AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/TycoonHUD.prefab");
#else
            var root = Canvas("TycoonHUD");
            var hud = root.AddComponent<TY_ClientController>();
            Color white = new(0.97f, 0.98f, 1f), money = new(0.45f, 1f, 0.55f), yellow = new(1f, 0.85f, 0.25f);
            var panel = Panel("WalletPanel", root.transform, new Vector2(0.012f, 0.4f), new Vector2(0.24f, 0.58f), Ink);
            hud.plotLabel = Text("Plot", panel.transform, new Vector2(0.06f, 0.7f), new Vector2(0.96f, 0.97f), 26, yellow, bold: true);
            hud.cashLabel = Text("Cash", panel.transform, new Vector2(0.06f, 0.3f), new Vector2(0.96f, 0.74f), 56, money, TextAlignmentOptions.MidlineLeft, true);
            hud.incomeLabel = Text("Income", panel.transform, new Vector2(0.06f, 0.03f), new Vector2(0.96f, 0.32f), 21, white);
            var hint = Panel("HintPanel", root.transform, new Vector2(0.28f, 0.03f), new Vector2(0.72f, 0.09f), Ink);
            hud.hintLabel = Text("Hint", hint.transform, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), 24, white, TextAlignmentOptions.Center);
            hud.toastLabel = Text("Toast", root.transform, new Vector2(0.2f, 0.66f), new Vector2(0.8f, 0.76f), 40, yellow, TextAlignmentOptions.Center, true);
            hud.sfx = Sfx(root.transform);
            hud.buyClip = hud.collectClip = hud.rebirthClip = Clip(CoinClip);
            hud.deniedClip = Clip(LandClip);
            return Kit.Save(root, "", "TycoonHUD");
#endif
        }

        // ---------------------------------------------------------------- scene
        static void BuildStartScene(Prefabs p) {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var hud = BuildHud();
            Kit.NewLitScene(new Color(0.45f, 0.68f, 1f), new Color(0.72f, 0.84f, 0.98f), 140f, 420f, new Vector3(50f, -30f, 0f));
            var world = new GameObject("World");
            Cube("Baseplate", world.transform, new Vector3(0f, -0.6f, 0f), new Vector3(260f, 1f, 260f), p.grass);
            Primitive(PrimitiveType.Cylinder, "Plaza", world.transform, new Vector3(0f, -0.08f, 0f), new Vector3(30f, 0.1f, 30f), p.plaza);
            for (int i = 0; i < PlotCount; i++) {
                float angle = i * 360f / PlotCount;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                var path = Cube($"Path{i}", world.transform, dir * (Radius * 0.5f - 2f) + new Vector3(0f, -0.07f, 0f), new Vector3(6f, 0.1f, Radius - 34f), p.stone);
                path.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            }
            var sign = Empty("PlazaSign", world.transform, new Vector3(0f, 0f, 0f));
            Cube("Pillar", sign.transform, new Vector3(0f, 2.5f, 0f), new Vector3(1.4f, 5f, 1.4f), p.stone);
            var title = WorldText("Title", sign.transform, new Vector3(0f, 6.8f, 0f), "BRICK TYCOON\n<size=45%>Claim a plot. Build droppers. Get rich. Rebirth.</size>", 7f, new Color(1f, 0.85f, 0.25f), new Vector2(18f, 5f));
            title.gameObject.AddComponent<Billboard>();

            var mapGo = new GameObject("TycoonMap");
            var map = mapGo.AddComponent<TY_Map>();
            var spawn = Empty("PlazaSpawn", mapGo.transform, new Vector3(0f, 0f, -6f));
            map.plazaSpawn = spawn.transform;
            map.plots = new TY_PlotView[PlotCount];
            for (int i = 0; i < PlotCount; i++) {
                float angle = i * 360f / PlotCount;
                Vector3 pos = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * Radius;
                var plot = (GameObject)PrefabUtility.InstantiatePrefab(p.plotBase, mapGo.scene);
                plot.name = $"Plot{i + 1}";
                plot.transform.SetParent(mapGo.transform, false);
                plot.transform.position = pos;
                plot.transform.rotation = Quaternion.LookRotation(-pos.normalized, Vector3.up);
                // Each plot gets its own colour on the trim and owner board (prefab overrides).
                foreach (var r in plot.GetComponentsInChildren<Renderer>())
                    if (r.name is "Trim" or "Board") r.sharedMaterial = p.plotMats[i % p.plotMats.Length];
                    else if (r.name == "Floor") r.sharedMaterial = p.floorMats[i % p.floorMats.Length];
                map.plots[i] = plot.GetComponent<TY_PlotView>();
            }
            map.itemPrefabs = p.items;
            map.buttonPrefab = p.button;
            map.orePrefab = p.ore;
            map.oreMaterials = p.oreMats;
            map.clientPrefab = hud;
            EditorUtility.SetDirty(map);
            Kit.SaveActiveScene(Kit.StartScene(Universe));
        }

        [MenuItem("Ryan/Tycoon/Register Scenes")]
        public static void RegisterScenesMenu() => RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));

        // ---------------------------------------------------------------- previews
        /// <summary>Renders an overview and a fully built plot (thumbnail) in a temporary copy of the start scene.</summary>
        [MenuItem("Ryan/Tycoon/Render Previews")]
        public static void RenderPreviews() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
            var map = Object.FindAnyObjectByType<TY_Map>();
            string dir = PreviewDir(Universe);
            // Fill plot 1 completely and plot 2 halfway, like a server would after a few minutes.
            for (int plot = 0; plot < 2; plot++) {
                var view = map.plots[plot];
                view.claimPad.SetActive(false);
                view.ownerLabel.text = plot == 0 ? "Ryan's Tycoon" : "Guest's Tycoon";
                view.bankLabel.text = "$1.2K";
                for (int i = 0; i < TY_Catalog.Count; i++) {
                    if (map.itemPrefabs[i] == null || plot == 1 && i > 7) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(map.itemPrefabs[i], view.itemRoot);
                    go.transform.localPosition = TY_Catalog.Items[i].itemPosition;
                    go.transform.localRotation = Quaternion.identity;
                }
                for (int k = 0; k < 14; k++) {
                    var ore = (GameObject)PrefabUtility.InstantiatePrefab(map.orePrefab, view.itemRoot);
                    ore.transform.localPosition = new Vector3(TY_Catalog.BeltStartX + 1f + k * 2.2f, TY_Catalog.BeltTop + 0.3f, TY_Catalog.BeltZ);
                    ore.transform.localScale = Vector3.one * 0.6f;
                    ore.GetComponent<Renderer>().sharedMaterial = map.oreMaterials[k < 9 ? 0 : k < 11 ? 1 : 2];
                }
                if (plot == 1) {
                    var button = (GameObject)PrefabUtility.InstantiatePrefab(map.buttonPrefab, view.itemRoot);
                    button.transform.localPosition = TY_Catalog.Items[8].buttonPosition;
                    button.GetComponent<TY_ButtonView>().Show(TY_Catalog.Items[8].name, TY_Catalog.Items[8].price, 0);
                }
            }
            var cam = PreviewCamera();
            Shoot(cam, new Vector3(-60f, 70f, -95f), new Vector3(0f, 0f, 10f), Path.Combine(dir, "overview.png"), 1600, 900);
            var plotT = map.plots[0].transform;
            Shoot(cam, plotT.TransformPoint(new Vector3(-24f, 16f, 30f)), plotT.TransformPoint(new Vector3(0f, 2f, -3f)), Path.Combine(dir, "plot.png"), 1600, 900);
            Shoot(cam, plotT.TransformPoint(new Vector3(-16f, 9f, 22f)), plotT.TransformPoint(new Vector3(2f, 2f, -5f)), Path.Combine(dir, "thumbnail.png"), 640, 360);
            Object.DestroyImmediate(cam.gameObject);
            Kit.PublishThumbnail(Universe, Path.Combine(dir, "thumbnail.png"));
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
        }
    }
}
