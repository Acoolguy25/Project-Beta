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

namespace Universes.UniverseData.tycoon.Editor {
    /// <summary>
    /// Rebuilds Brick Tycoon's materials, item/pad prefabs, plot bases, item pictures, HUD and start
    /// scene. Safe to re-run (assets are overwritten in place). Models come from the CC0 KayKit packs
    /// under Assets/ThirdParty/KayKit (see CREDITS.md). Build the server runner from an Editor in the
    /// Linux Server profile: <c>-executeMethod Universes.UniverseData.tycoon.Editor.TY_Authoring.BuildServerAssets</c>.
    /// </summary>
    public static class TY_Authoring {
        public const string Universe = "tycoon";
        public const string Root = "Assets/Universes/UniverseData/tycoon";
        internal static readonly UniverseAuthoringKit Kit = new(Root);
        const string ServerPrefab = Root + "/Server/TY_ServerRunner.prefab";
        const string KayKit = "Assets/ThirdParty/KayKit";
        const float Radius = 66f;
        const int PlotCount = 6;

        static readonly Color[] PlotColors = {
            new(0.31f, 0.62f, 1f), new(0.98f, 0.42f, 0.36f), new(0.36f, 0.84f, 0.45f),
            new(1f, 0.78f, 0.25f), new(0.72f, 0.5f, 1f), new(0.25f, 0.85f, 0.82f)
        };

        [MenuItem("Ryan/Tycoon/Build Client Assets")]
        public static void BuildClientAssets() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Kit.EnsureFolders("Prefabs", "Prefabs/Items", "Prefabs/Pads", "Materials", "Textures", "Server", "UI", "UI/Pictures");
            TY_HudAuthoring.ImportIcons();
            BuildTextures();
            var p = BuildPrefabs();
            TY_PictureRenderer.RenderAll(p.items);
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
            // Brick face: soft bevel so a single cube reads as a brick.
            Kit.SaveTexture("BrickFace", (u, v) => {
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float shade = Mathf.Lerp(0.72f, 1f, Mathf.SmoothStep(0f, 0.12f, edge));
                float grain = 0.94f + 0.06f * Hash(Mathf.Floor(u * 24f), Mathf.Floor(v * 24f));
                return new Color(shade * grain, shade * grain, shade * grain);
            }, 64);
        }

        internal sealed class Prefabs {
            public GameObject plotBase, ore, coin;
            public GameObject[] items, pads;
            public Material[] plotMats, oreMats, floorMats;
            public Material grass, plaza, stone, city, space, proto;
        }

        // ---------------------------------------------------------------- KayKit models
        enum Pack { City, Space, Proto }

        static string PackPath(Pack pack) => pack switch {
            Pack.City => KayKit + "/CityBuilderBits/Models",
            Pack.Space => KayKit + "/SpaceBaseBits/Models",
            _ => KayKit + "/PrototypeBits/Models",
        };

        /// <summary>One URP material per KayKit pack: every model in a pack shares its palette texture.</summary>
        static Material PackMaterial(Pack pack) {
            string texture = pack switch { Pack.City => "citybits_texture", Pack.Space => "spacebits_texture", _ => "prototypebits_texture" };
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PackPath(pack)}/{texture}.png");
            if (tex == null) throw new FileNotFoundException($"KayKit texture missing: {PackPath(pack)}/{texture}.png");
            return Kit.Mat($"KayKit{pack}", Color.white, tex, smoothness: 0.3f);
        }

        static Material Pal(Prefabs p, Pack pack) => pack switch { Pack.City => p.city, Pack.Space => p.space, _ => p.proto };

        /// <summary>A KayKit model under <paramref name="parent"/>, largest side <paramref name="size"/> m, base on the parent origin + offset.</summary>
        static GameObject Kay(Prefabs p, Pack pack, string model, Transform parent, float size, Vector3 offset = default, float yaw = 0f, Material overrideMaterial = null) {
            string path = $"{PackPath(pack)}/{model}.fbx";
            var go = Model(path, parent, size, offset, yaw);
            if (go == null) throw new FileNotFoundException("KayKit model missing: " + path);
            var mat = overrideMaterial != null ? overrideMaterial : Pal(p, pack);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => mat).ToArray();
            return go;
        }

        // ---------------------------------------------------------------- prefabs
        static Prefabs BuildPrefabs() {
            var tile = Kit.Tex("Tile");
            var p = new Prefabs {
                plotMats = PlotColors.Select((c, i) => Kit.Mat($"Plot{i}", c, tile, smoothness: 0.35f)).ToArray(),
                // Raw clay -> polished -> washed -> glazed gold, one stage per upgrader; each stage glows brighter.
                oreMats = new[] {
                    Kit.Mat("Ore0", new Color(0.62f, 0.33f, 0.24f), Kit.Tex("BrickFace"), smoothness: 0.15f),
                    Kit.Mat("Ore1", new Color(0.92f, 0.32f, 0.24f), Kit.Tex("BrickFace"), 0.6f, smoothness: 0.55f),
                    Kit.Mat("Ore2", new Color(0.55f, 0.85f, 1f), Kit.Tex("BrickFace"), 1.1f, smoothness: 0.8f),
                    Kit.Mat("Ore3", new Color(1f, 0.8f, 0.2f), Kit.Tex("BrickFace"), 1.8f, smoothness: 0.9f, metallic: 0.7f),
                },
                grass = Tiled(Kit.Mat("Grass", Color.white, Kit.Tex("Grass"), smoothness: 0.1f), 8f, 8f),
                plaza = Tiled(Kit.Mat("Plaza", new Color(0.86f, 0.88f, 0.92f), tile, smoothness: 0.3f), 8f, 8f),
                stone = Tiled(Kit.Mat("Stone", new Color(0.55f, 0.57f, 0.62f), tile, smoothness: 0.2f), 2f, 8f),
                city = PackMaterial(Pack.City),
                space = PackMaterial(Pack.Space),
                proto = PackMaterial(Pack.Proto),
            };
            p.floorMats = PlotColors.Select((c, i) => Tiled(Kit.Mat($"PlotFloor{i}", Color.Lerp(c, new Color(0.9f, 0.92f, 0.95f), 0.82f), tile, smoothness: 0.3f), 10f, 10f)).ToArray();
            var mats = new Mats {
                belt = Tiled(Kit.Mat("Belt", Color.white, Kit.Tex("Arrows"), 0.15f, smoothness: 0.2f), 17f, 1f),
                brickWall = Tiled(Kit.Mat("BrickWall", Color.white, Kit.Tex("Brick")), 10f, 1f),
                steel = Kit.Mat("Steel", new Color(0.55f, 0.58f, 0.64f), smoothness: 0.7f, metallic: 0.8f),
                dark = Kit.Mat("Dark", new Color(0.15f, 0.16f, 0.19f), smoothness: 0.4f),
                lava = Kit.Mat("FurnaceGlow", new Color(1f, 0.45f, 0.1f), emission: 2.2f),
                gold = Kit.Mat("Gold", new Color(1f, 0.78f, 0.2f), emission: 0.35f, smoothness: 0.85f, metallic: 0.95f),
                claim = Kit.Mat("Claim", new Color(0.3f, 1f, 0.6f), tile, 1.4f),
                collect = Kit.Mat("Collect", new Color(0.3f, 0.75f, 1f), tile, 1.1f),
                padReady = Kit.Mat("PadReady", new Color(0.25f, 0.9f, 0.35f), tile, 0.9f),
                padExpensive = Kit.Mat("PadExpensive", new Color(0.95f, 0.3f, 0.25f), tile, 0.4f),
                padLocked = Kit.Mat("PadLocked", new Color(0.45f, 0.47f, 0.52f), tile, 0.05f),
                padForeign = Kit.Mat("PadForeign", new Color(0.62f, 0.64f, 0.68f), tile, 0.1f),
                upgradeFields = new[] {
                    Kit.Mat("FieldPolish", new Color(1f, 0.45f, 0.35f), emission: 1.4f, alpha: 0.35f),
                    Kit.Mat("FieldWash", new Color(0.35f, 0.75f, 1f), emission: 1.4f, alpha: 0.35f),
                    Kit.Mat("FieldGlaze", new Color(1f, 0.8f, 0.25f), emission: 1.6f, alpha: 0.35f),
                },
            };

            p.plotBase = PlotBase(p, mats);
            p.pads = System.Enum.GetValues(typeof(TY_ItemKind)).Cast<TY_ItemKind>().Select(kind => Pad(kind, mats)).ToArray();
            p.ore = Brick(p.oreMats[0]);
            p.coin = Coin(p);

            p.items = new GameObject[TY_Catalog.Count];
            int dropperTier = 0, upgraderTier = 0;
            for (int i = 0; i < TY_Catalog.Count; i++) {
                var item = TY_Catalog.Items[i];
                if (item.kind == TY_ItemKind.Dropper) p.items[i] = Dropper(p, item, dropperTier++, mats);
                else if (item.kind == TY_ItemKind.Upgrader) p.items[i] = Upgrader(p, item, upgraderTier++, mats);
            }
            p.items[Index("Brick Walls")] = Walls(mats);
            p.items[Index("Street Lamps")] = Lamps(p);
            p.items[Index("Office")] = Building(p, "Office", "building_C");
            p.items[Index("Corner Shop")] = Building(p, "CornerShop", "building_A");
            p.items[Index("Apartments")] = Building(p, "Apartments", "building_H");
            p.items[Index("Wind Farm")] = WindFarm(p);
            p.items[Index("Golden Statue")] = Statue(mats.gold, p.stone);
            p.items[Index("Vault")] = Props(p, "Vault", Pack.Space, "cargo_A_stacked", 2.4f, "VAULT");
            p.items[Index("Big Vault")] = Props(p, "BigVault", Pack.Space, "cargodepot_A", 3.4f, "BIG VAULT");
            p.items[Index("Mega Vault")] = Props(p, "MegaVault", Pack.Space, "basemodule_A", 3.6f, "MEGA VAULT");
            p.items[Index("Auto-Collector")] = AutoCollector(p);
            p.items[Index("Bank Tower")] = Building(p, "BankTower", "building_E");
            return p;
        }

        sealed class Mats {
            public Material belt, brickWall, steel, dark, lava, gold, claim, collect, padReady, padExpensive, padLocked, padForeign;
            public Material[] upgradeFields;
        }

        static int Index(string name) {
            int i = System.Array.FindIndex(TY_Catalog.Items, item => item.name == name);
            if (i < 0) throw new System.ArgumentException($"No catalog item named {name}");
            return i;
        }

        /// <summary>Plot base: floor, belt, furnace, collector, owner sign, claim pad.</summary>
        static GameObject PlotBase(Prefabs p, Mats m) {
            var root = new GameObject("PlotBase");
            var view = root.AddComponent<TY_PlotView>();
            Cube("Floor", root.transform, new Vector3(0f, -0.25f, 0f), new Vector3(TY_Catalog.PlotSize, 0.5f, TY_Catalog.PlotSize), p.floorMats[0]);
            Cube("Trim", root.transform, new Vector3(0f, -0.3f, 0f), new Vector3(TY_Catalog.PlotSize + 1.2f, 0.5f, TY_Catalog.PlotSize + 1.2f), p.plotMats[0]);

            float len = TY_Catalog.BeltEndX - TY_Catalog.BeltStartX + 2f;
            float mid = (TY_Catalog.BeltEndX + TY_Catalog.BeltStartX) * 0.5f + 1f;
            view.belt = Cube("Belt", root.transform, new Vector3(mid, TY_Catalog.BeltTop * 0.5f, TY_Catalog.BeltZ), new Vector3(len, TY_Catalog.BeltTop, 1.6f), m.belt).GetComponent<Renderer>();
            for (int s = -1; s <= 1; s += 2)
                Cube($"Rail{s}", root.transform, new Vector3(mid, TY_Catalog.BeltTop + 0.15f, TY_Catalog.BeltZ + s * 0.9f), new Vector3(len, 0.3f, 0.2f), m.steel);

            // Furnace: a KayKit garage module the belt runs into, with a glowing mouth.
            var furnace = Empty("Furnace", root.transform, TY_Catalog.FurnacePosition);
            Kay(p, Pack.Space, "basemodule_garage", furnace.transform, 4.2f, new Vector3(1.1f, 0f, 0f), 90f);
            NoShadows(Cube("Mouth", furnace.transform, new Vector3(-0.95f, 0.95f, 0f), new Vector3(0.1f, 1.3f, 1.8f), m.lava, false));
            var fire = new GameObject("FireLight", typeof(Light));
            fire.transform.SetParent(furnace.transform, false);
            fire.transform.localPosition = new Vector3(-1.6f, 1.2f, 0f);
            var light = fire.GetComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.55f, 0.2f); light.range = 6f; light.intensity = 2.5f;

            Cube("CollectPad", root.transform, TY_Catalog.CollectPosition + new Vector3(0f, 0.1f, 0f), new Vector3(TY_Catalog.PadSize.x, 0.2f, TY_Catalog.PadSize.z), m.collect);
            Kay(p, Pack.Proto, "Coin_A", Empty("CollectCoin", root.transform, TY_Catalog.CollectPosition + new Vector3(0f, 0.4f, 0f)).transform, 1.2f)
                .transform.parent.gameObject.AddComponent<Spinner>().degreesPerSecond = new Vector3(0f, 90f, 0f);
            view.bankLabel = WorldText("Bank", root.transform, TY_Catalog.CollectPosition + new Vector3(0f, 3f, 0f), "$0", 5f, new Color(0.6f, 0.95f, 1f), new Vector2(9f, 3f));
            view.bankLabel.gameObject.AddComponent<Billboard>();

            var sign = Empty("OwnerSign", root.transform, new Vector3(0f, 0f, TY_Catalog.PlotSize * 0.5f + 1.2f));
            Cube("PostL", sign.transform, new Vector3(-5.5f, 2.5f, 0f), new Vector3(0.4f, 5f, 0.4f), m.dark);
            Cube("PostR", sign.transform, new Vector3(5.5f, 2.5f, 0f), new Vector3(0.4f, 5f, 0.4f), m.dark);
            Cube("Board", sign.transform, new Vector3(0f, 5.4f, 0f), new Vector3(11.8f, 2.2f, 0.3f), p.plotMats[0], false);
            view.ownerLabel = WorldText("Owner", sign.transform, new Vector3(0f, 5.4f, 0.2f), "FREE PLOT", 6f, Color.white, new Vector2(11f, 2f), 180f);

            var claim = Empty("ClaimPad", root.transform, TY_Catalog.ClaimPosition);
            Cube("Pad", claim.transform, new Vector3(0f, 0.1f, 0f), new Vector3(TY_Catalog.PadSize.x, 0.2f, TY_Catalog.PadSize.z), m.claim);
            WorldText("Label", claim.transform, new Vector3(0f, 2.4f, 0f), "CLAIM\n<size=60%>step here</size>", 5f, new Color(0.6f, 1f, 0.7f), new Vector2(6f, 3f)).gameObject.AddComponent<Billboard>();
            view.claimPad = claim;
            view.itemRoot = Empty("Items", root.transform).transform;
            return Kit.Save(root, "", "PlotBase");
        }

        /// <summary>A lane's buy pad with its floating sign (picture, name, effect, price).</summary>
        static GameObject Pad(TY_ItemKind kind, Mats m) {
            Color laneColor = TY_Lanes.Color(kind);
            var laneMat = Kit.Mat($"Lane{kind}", laneColor, Kit.Tex("Tile"), 0.6f, smoothness: 0.4f);
            var root = new GameObject($"Pad{kind}");
            var view = root.AddComponent<TY_ButtonView>();
            var pulse = Empty("Pulse", root.transform);
            view.pulse = pulse.transform;
            view.pad = Cube("Pad", pulse.transform, new Vector3(0f, 0.15f, 0f), new Vector3(TY_Catalog.ButtonSize.x - 0.4f, 0.3f, TY_Catalog.ButtonSize.z - 0.4f), m.padReady, false).GetComponent<Renderer>();
            Cube("Rim", root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(TY_Catalog.ButtonSize.x + 0.2f, 0.16f, TY_Catalog.ButtonSize.z + 0.2f), laneMat, false);
            view.affordable = m.padReady; view.expensive = m.padExpensive; view.locked = m.padLocked; view.foreign = m.padForeign;

            var sign = Empty("Sign", root.transform, new Vector3(0f, 2.7f, 0f));
            sign.transform.localScale = Vector3.one * 0.85f;
            sign.AddComponent<Billboard>();
            var back = SpriteChild("Back", sign.transform, Vector3.zero, TY_HudAuthoring.SignSprite(), new Color(0.06f, 0.08f, 0.12f, 0.94f), 0);
            back.transform.localScale = new Vector3(4.4f, 2.3f, 1f);
            var strip = SpriteChild("LaneStrip", sign.transform, new Vector3(0f, 1.08f, -0.01f), TY_HudAuthoring.WhiteSprite(), laneColor, 1);
            strip.transform.localScale = new Vector3(4.1f, 0.12f, 1f);
            view.picture = SpriteChild("Picture", sign.transform, new Vector3(-1.25f, 0.05f, -0.01f), null, Color.white, 1);
            view.picture.transform.localScale = Vector3.one * 1.55f;
            SpriteChild("LaneIcon", sign.transform, new Vector3(1.9f, 0.82f, -0.01f), TY_HudAuthoring.IconSprite(TY_Lanes.Icon(kind)), laneColor, 2).transform.localScale = Vector3.one * 0.32f;
            view.title = SignText("Title", sign.transform, new Vector3(0.75f, 0.52f, -0.02f), 2.6f, Color.white, FontStyles.Bold, new Vector2(2.6f, 0.6f));
            view.effect = SignText("Effect", sign.transform, new Vector3(0.75f, 0.02f, -0.02f), 1.9f, laneColor, FontStyles.Normal, new Vector2(2.6f, 0.5f));
            view.price = SignText("Price", sign.transform, new Vector3(0.75f, -0.58f, -0.02f), 3.2f, view.affordablePrice, FontStyles.Bold, new Vector2(2.6f, 0.7f));
            SaveBar(view, sign.transform, laneColor);
            return Kit.Save(root, "Pads", $"Pad{kind}");
        }

        /// <summary>A thin bar along the sign's bottom edge that fills as the owner saves up for the item.</summary>
        static void SaveBar(TY_ButtonView view, Transform sign, Color laneColor) {
            const float width = 3.9f, height = 0.16f, y = -0.97f;
            var bar = Empty("SaveBar", sign, new Vector3(0f, y, -0.015f));
            SpriteChild("Track", bar.transform, Vector3.zero, TY_HudAuthoring.WhiteSprite(), new Color(1f, 1f, 1f, 0.22f), 2).transform.localScale = new Vector3(width, height, 1f);
            // The fill's parent sits on the bar's left end, so scaling it along X grows the bar to the right.
            var fill = Empty("Fill", bar.transform, new Vector3(-width * 0.5f, 0f, -0.005f));
            SpriteChild("Bar", fill.transform, new Vector3(width * 0.5f, 0f, 0f), TY_HudAuthoring.WhiteSprite(), laneColor, 3).transform.localScale = new Vector3(width, height, 1f);
            fill.transform.localScale = new Vector3(0.4f, 1f, 1f);
            view.saveBar = bar;
            view.saveFill = fill.transform;
        }

        static SpriteRenderer SpriteChild(string name, Transform parent, Vector3 position, Sprite sprite, Color color, int order) {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return sr;
        }

        static TextMeshPro SignText(string name, Transform parent, Vector3 position, float size, Color color, FontStyles style, Vector2 box) {
            var text = WorldText(name, parent, position, "", size, color, box);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontStyle = style;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.sortingOrder = 3;
            var font = TY_HudAuthoring.Font(style == FontStyles.Bold);
            if (font != null) text.font = font;
            return text;
        }

        static GameObject Brick(Material mat) {
            var root = new GameObject("Brick");
            var body = Cube("Body", root.transform, Vector3.zero, new Vector3(0.9f, 0.42f, 0.5f), mat, false);
            body.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return Kit.Save(root, "", "Ore");
        }

        static GameObject Coin(Prefabs p) {
            var root = new GameObject("GoldCoin");
            var spin = Empty("Spin", root.transform, new Vector3(0f, 1.4f, 0f));
            var spinner = spin.AddComponent<Spinner>();
            spinner.degreesPerSecond = new Vector3(0f, 150f, 0f);
            spinner.bobHeight = 0.35f;
            Kay(p, Pack.Proto, "Coin_A", spin.transform, 1.8f, new Vector3(0f, -0.9f, 0f), 0f, Kit.Mat("Gold", new Color(1f, 0.78f, 0.2f), emission: 0.35f, smoothness: 0.85f, metallic: 0.95f));
            var glow = new GameObject("Glow", typeof(Light));
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var light = glow.GetComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.82f, 0.3f); light.range = 5f; light.intensity = 3f;
            WorldText("Label", root.transform, new Vector3(0f, 3.3f, 0f), "GOLD COIN", 3.2f, new Color(1f, 0.85f, 0.3f), new Vector2(5f, 1f)).gameObject.AddComponent<Billboard>();
            return Kit.Save(root, "", "GoldCoin");
        }

        static readonly (string model, float size)[] DropperModels = {
            ("structure_low", 3.3f), ("structure_low", 3.4f), ("structure_tall", 4.6f), ("structure_tall", 4.8f),
            ("drill_structure", 5f), ("drill_structure", 5.4f), ("drill_structure", 6.2f),
        };

        static readonly Color[] TierColors = {
            new(0.85f, 0.87f, 0.9f), new(0.45f, 0.7f, 1f), new(0.45f, 0.9f, 0.5f), new(1f, 0.7f, 0.3f),
            new(0.95f, 0.4f, 0.4f), new(0.75f, 0.45f, 1f), new(1f, 0.85f, 0.25f)
        };

        /// <summary>A KayKit tripod straddling the belt, with a tier-coloured crate hopper on top. Origin on the belt line.</summary>
        static GameObject Dropper(Prefabs p, in TY_Item item, int tier, Mats m) {
            string name = item.name.Replace(" ", "");
            var (model, size) = DropperModels[Mathf.Min(tier, DropperModels.Length - 1)];
            var root = new GameObject(name);
            var frame = Kay(p, Pack.Space, model, root.transform, size);
            float top = RendererBounds(frame).max.y;
            var tint = Kit.Mat($"DropperTier{tier}", TierColors[tier % TierColors.Length], p.space.GetTexture("_BaseMap") as Texture2D, tier >= 6 ? 0.3f : 0f, smoothness: 0.45f, metallic: tier >= 6 ? 0.5f : 0f);
            Kay(p, Pack.Space, tier >= 4 ? "cargo_A_stacked" : "cargo_A", root.transform, tier >= 4 ? 1.9f : 1.4f, new Vector3(0f, top - 0.15f, 0f), 0f, tint);
            var label = WorldText("Label", root.transform, new Vector3(0f, top + (tier >= 4 ? 2.6f : 2f), 0f),
                $"<size=70%>{item.name}</size>\n${item.value / item.interval:0.#}/s", 3f, Color.white, new Vector2(5f, 1.6f));
            label.gameObject.AddComponent<Billboard>();
            return Kit.Save(root, "Items", name);
        }

        /// <summary>A KayKit tunnel over the belt with a glowing field; bricks change look as they pass.</summary>
        static GameObject Upgrader(Prefabs p, in TY_Item item, int tier, Mats m) {
            string name = item.name.Replace(" ", "");
            var root = new GameObject(name);
            var tunnel = Kay(p, Pack.Space, tier % 2 == 0 ? "tunnel_straight_A" : "tunnel_straight_B", root.transform, 2.6f, Vector3.zero, 0f);
            tunnel.transform.localScale = Vector3.Scale(tunnel.transform.localScale, new Vector3(1f, 2.6f, 2.4f));
            var b = RendererBounds(tunnel);
            tunnel.transform.position += Vector3.down * b.min.y;
            var field = Cube("Field", root.transform, new Vector3(0f, 1.3f, 0f), new Vector3(0.12f, 1.9f, 1.9f), m.upgradeFields[tier % m.upgradeFields.Length], false);
            NoShadows(field);
            WorldText("Label", root.transform, new Vector3(0f, RendererBounds(tunnel).max.y + 1.1f, 0f), $"{item.name.ToUpperInvariant()}\n<size=70%>x{item.value:0.##}</size>", 3f,
                m.upgradeFields[tier % m.upgradeFields.Length].GetColor("_BaseColor") * 1.2f + new Color(0.2f, 0.2f, 0.2f), new Vector2(5f, 1.6f)).gameObject.AddComponent<Billboard>();
            return Kit.Save(root, "Items", name);
        }

        static GameObject Walls(Mats m) {
            var root = new GameObject("BrickWalls");
            float h = 3f, half = TY_Catalog.PlotSize * 0.5f - 0.3f;
            Cube("Back", root.transform, new Vector3(0f, h * 0.5f, -half), new Vector3(TY_Catalog.PlotSize, h, 0.6f), m.brickWall);
            Cube("Left", root.transform, new Vector3(-half, h * 0.5f, 0f), new Vector3(0.6f, h, TY_Catalog.PlotSize), m.brickWall);
            Cube("Right", root.transform, new Vector3(half, h * 0.5f, 0f), new Vector3(0.6f, h, TY_Catalog.PlotSize), m.brickWall);
            float gap = 5f, seg = (TY_Catalog.PlotSize - gap * 2f) * 0.5f;
            for (int side = -1; side <= 1; side += 2)
                Cube($"Front{side}", root.transform, new Vector3(side * (gap + seg * 0.5f), h * 0.5f, half), new Vector3(seg, h, 0.6f), m.brickWall);
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(-gap / half, 1), new Vector2(gap / half, 1) })
                Cube("Cap", root.transform, new Vector3(c.x * half, h + 0.3f, c.y * half), new Vector3(1.2f, 0.6f, 1.2f), m.dark);
            return Kit.Save(root, "Items", "BrickWalls");
        }

        static GameObject Lamps(Prefabs p) {
            var root = new GameObject("StreetLamps");
            float r = TY_Catalog.PlotSize * 0.5f - 1.6f;
            (Vector2 spot, float yaw)[] lamps = {
                (new(-1f, -0.35f), 0f), (new(1f, -0.35f), 180f), (new(-1f, 0.45f), 0f), (new(1f, 0.45f), 180f),
                (new(-0.35f, 1f), 90f), (new(0.35f, 1f), 90f),
            };
            for (int i = 0; i < lamps.Length; i++) {
                var lamp = Empty(i == 0 ? TY_PictureRenderer.FocusName : $"Lamp{i}", root.transform, new Vector3(lamps[i].spot.x * r, 0f, lamps[i].spot.y * r));
                Kay(p, Pack.City, "streetlight", lamp.transform, 5.2f, Vector3.zero, lamps[i].yaw);
                var glow = new GameObject("Light", typeof(Light));
                glow.transform.SetParent(lamp.transform, false);
                glow.transform.localPosition = new Vector3(0f, 4.6f, 0f);
                var light = glow.GetComponent<Light>();
                light.type = LightType.Point; light.color = new Color(1f, 0.9f, 0.7f); light.range = 9f; light.intensity = 1.2f;
                light.shadows = LightShadows.None;
            }
            return Kit.Save(root, "Items", "StreetLamps");
        }

        /// <summary>A KayKit city building facing the plot centre (+Z), with a bush and bench out front.</summary>
        static GameObject Building(Prefabs p, string name, string model) {
            var root = new GameObject(name);
            Kay(p, Pack.City, model, root.transform, 8.2f, Vector3.zero, 180f);
            return Kit.Save(root, "Items", name);
        }

        static GameObject WindFarm(Prefabs p) {
            var root = new GameObject("WindFarm");
            for (int k = -1; k <= 1; k++)
                Kay(p, Pack.Space, "windturbine_tall", Empty($"Turbine{k + 1}", root.transform, new Vector3(0f, 0f, k * 4.5f)).transform, 7f, Vector3.zero, 90f);
            return Kit.Save(root, "Items", "WindFarm");
        }

        static GameObject Props(Prefabs p, string name, Pack pack, string model, float size, string label) {
            var root = new GameObject(name);
            var go = Kay(p, pack, model, root.transform, size, Vector3.zero, 270f);
            WorldText("Label", root.transform, new Vector3(0f, RendererBounds(go).max.y + 0.9f, 0f), label, 2.6f, TY_Lanes.Color(TY_ItemKind.Utility) * 1.3f, new Vector2(5f, 1f))
                .gameObject.AddComponent<Billboard>();
            return Kit.Save(root, "Items", name);
        }

        static GameObject AutoCollector(Prefabs p) {
            var root = new GameObject("AutoCollector");
            Kay(p, Pack.Space, "lights", root.transform, 3.2f, Vector3.zero, 270f);
            WorldText("Label", root.transform, new Vector3(0f, 4f, 0f), "AUTO", 2.6f, new Color(0.35f, 1f, 0.6f), new Vector2(4f, 1f)).gameObject.AddComponent<Billboard>();
            return Kit.Save(root, "Items", "AutoCollector");
        }

        static GameObject Statue(Material gold, Material stone) {
            var root = new GameObject("GoldenStatue");
            Cube("Plinth", root.transform, new Vector3(0f, 0.75f, 0f), new Vector3(4f, 1.5f, 4f), stone);
            var figure = Model("Assets/UnityTechnologies/SpaceRobotKyle/Models/KyleRobot.fbx", Empty("Figure", root.transform, new Vector3(0f, 1.5f, 0f)).transform, 4.5f, yaw: 180f);
            if (figure == null) throw new FileNotFoundException("KyleRobot model missing for the Golden Statue.");
            foreach (var r in figure.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => gold).ToArray();
            return Kit.Save(root, "Items", "GoldenStatue");
        }

        // ---------------------------------------------------------------- scene
        static void BuildStartScene(Prefabs p) {
            var hud = TY_HudAuthoring.Build();
            Kit.NewLitScene(new Color(0.45f, 0.68f, 1f), new Color(0.72f, 0.84f, 0.98f), 160f, 460f, new Vector3(50f, -30f, 0f));
            var world = new GameObject("World");
            Cube("Baseplate", world.transform, new Vector3(0f, -0.6f, 0f), new Vector3(280f, 1f, 280f), p.grass);
            Primitive(PrimitiveType.Cylinder, "Plaza", world.transform, new Vector3(0f, -0.08f, 0f), new Vector3(30f, 0.1f, 30f), p.plaza);
            for (int i = 0; i < PlotCount; i++) {
                float angle = i * 360f / PlotCount;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                var path = Cube($"Path{i}", world.transform, dir * (Radius * 0.5f - 2f) + new Vector3(0f, -0.07f, 0f), new Vector3(6f, 0.1f, Radius - 34f), p.stone);
                path.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                // Lamps and bushes line each path between plots and plaza.
                for (int side = -1; side <= 1; side += 2) {
                    Vector3 across = Quaternion.Euler(0f, angle, 0f) * Vector3.right * side * 4.2f;
                    var lamp = Empty($"PathLamp{i}{side}", world.transform, dir * 22f + across);
                    Kay(p, Pack.City, "streetlight", lamp.transform, 5.2f, Vector3.zero, angle + (side < 0 ? 0f : 180f));
                    Kay(p, Pack.City, "bush", Empty($"Bush{i}{side}", world.transform, dir * 18f + across).transform, 1.8f);
                }
            }
            // Plaza furniture between the paths.
            for (int i = 0; i < PlotCount; i++) {
                float angle = (i + 0.5f) * 360f / PlotCount;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Kay(p, Pack.City, "bench", Empty($"Bench{i}", world.transform, dir * 12.5f).transform, 2.4f, Vector3.zero, angle + 180f);
                Kay(p, Pack.City, "bush", Empty($"PlazaBush{i}", world.transform, dir * 14.5f).transform, 2.2f);
                Kay(p, Pack.City, "watertower", Empty($"Tower{i}", world.transform, dir * 40f).transform, 7f, Vector3.zero, angle);
            }
            var sign = Empty("PlazaSign", world.transform, Vector3.zero);
            Kay(p, Pack.Proto, "Pillar_A", sign.transform, 5f);
            var coin = Empty("Coin", sign.transform, new Vector3(0f, 5.6f, 0f));
            coin.AddComponent<Spinner>().degreesPerSecond = new Vector3(0f, 60f, 0f);
            Kay(p, Pack.Proto, "Coin_A", coin.transform, 2.4f, new Vector3(0f, -1.2f, 0f), 0f, Kit.Mat("Gold", new Color(1f, 0.78f, 0.2f), emission: 0.35f, smoothness: 0.85f, metallic: 0.95f));
            var title = WorldText("Title", sign.transform, new Vector3(0f, 9.2f, 0f), "BRICK TYCOON\n<size=45%>Claim a plot. Build droppers. Get rich. Rebirth.</size>", 7f, new Color(1f, 0.85f, 0.25f), new Vector2(18f, 5f));
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
            map.itemPictures = TY_PictureRenderer.LoadAll();
            map.padPrefabs = p.pads;
            map.orePrefab = p.ore;
            map.goldCoinPrefab = p.coin;
            map.oreMaterials = p.oreMats;
            map.clientPrefab = hud;
            EditorUtility.SetDirty(map);
            Kit.SaveActiveScene(Kit.StartScene(Universe));
        }

        /// <summary>Like <see cref="Shoot"/>, but first turns every <see cref="Billboard"/> to the camera, as it would at runtime.</summary>
        static void Shot(Camera cam, Vector3 position, Vector3 target, string path, int width, int height) {
            foreach (var billboard in Object.FindObjectsByType<Billboard>(FindObjectsInactive.Exclude)) {
                Vector3 away = billboard.transform.position - position;
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f) billboard.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
            Shoot(cam, position, target, path, width, height);
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
                view.bankLabel.text = "$1.2K<size=55%> / $6K</size>";
                for (int i = 0; i < TY_Catalog.Count; i++) {
                    if (map.itemPrefabs[i] == null || plot == 1 && TY_Catalog.Items[i].price > 2500) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(map.itemPrefabs[i], view.itemRoot);
                    go.transform.localPosition = TY_Catalog.Items[i].itemPosition;
                    go.transform.localRotation = Quaternion.Euler(0f, TY_Catalog.Items[i].itemYaw, 0f);
                }
                for (int k = 0; k < 14; k++) {
                    var ore = (GameObject)PrefabUtility.InstantiatePrefab(map.orePrefab, view.itemRoot);
                    float x = TY_Catalog.BeltStartX + 1f + k * 2.2f;
                    ore.transform.localPosition = new Vector3(x, TY_Catalog.BeltTop + 0.2f, TY_Catalog.BeltZ);
                    ore.transform.localRotation = Quaternion.identity;
                    int stage = x < 7.4f ? 0 : x < 10.2f ? 1 : x < 13f ? 2 : 3;
                    ore.GetComponentInChildren<Renderer>().sharedMaterial = map.oreMaterials[stage];
                }
                if (plot == 1) {
                    foreach (var kind in TY_Lanes.BuildLanes) {
                        int next = TY_Catalog.Items.Select((item, i) => (item, i)).First(x => x.item.kind == kind && x.item.price > 2500).i;
                        var pad = (GameObject)PrefabUtility.InstantiatePrefab(map.padPrefabs[(int)kind], view.itemRoot);
                        pad.transform.localPosition = TY_Catalog.LanePosition(kind);
                        bool affordable = kind == TY_ItemKind.Building;
                        pad.GetComponent<TY_ButtonView>().Show(TY_Catalog.Items[next], map.Picture(next), affordable ? TY_ButtonLook.Affordable : TY_ButtonLook.Expensive, "", affordable ? 1f : 0.45f);
                    }
                }
            }
            var coin = (GameObject)PrefabUtility.InstantiatePrefab(map.goldCoinPrefab, map.plots[1].itemRoot);
            coin.transform.localPosition = new Vector3(-4f, 0f, 3f);
            var cam = PreviewCamera();
            Shot(cam, new Vector3(-70f, 80f, -105f), new Vector3(0f, 0f, 10f), Path.Combine(dir, "overview.png"), 1600, 900);
            var plotT = map.plots[0].transform;
            Shot(cam, plotT.TransformPoint(new Vector3(-26f, 18f, 32f)), plotT.TransformPoint(new Vector3(0f, 2f, -4f)), Path.Combine(dir, "plot.png"), 1600, 900);
            var plot2 = map.plots[1].transform;
            Shot(cam, plot2.TransformPoint(new Vector3(-8f, 9f, 24f)), plot2.TransformPoint(new Vector3(-2f, 2f, 3f)), Path.Combine(dir, "pads.png"), 1600, 900);
            Shot(cam, plotT.TransformPoint(new Vector3(-17f, 10f, 23f)), plotT.TransformPoint(new Vector3(2f, 2f, -6f)), Path.Combine(dir, "thumbnail.png"), 640, 360);
            Object.DestroyImmediate(cam.gameObject);
            Kit.PublishThumbnail(Universe, Path.Combine(dir, "thumbnail.png"));
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
        }
    }
}
