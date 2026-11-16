using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Editor.Manor {
    /// <summary>The finishes a slab, wall or path can be built from (see <see cref="HS_ManorMaterials"/>).</summary>
    public enum Surface { Grass, Path, Driveway, Parquet, Laminate, Tiles, Checker, Planks, DarkPlanks, Plaster, Brick, Roof, Glass, Trim }

    /// <summary>What a wall is: it decides thickness, height and finish.</summary>
    public enum WallKind {
        /// <summary>Brick outside, plaster inside, the full height of the house.</summary>
        Exterior,
        /// <summary>Plaster both sides, one storey tall.</summary>
        Interior,
        /// <summary>Waist-high balustrade (stair well, balcony).</summary>
        Railing,
        /// <summary>Wooden plank wall for the shed.</summary>
        Shed,
        /// <summary>Brick wall one storey tall (garage).</summary>
        Garage,
    }

    /// <summary>A hole in a wall, measured along the wall from its start point.</summary>
    public readonly struct Opening {
        public readonly float at, width, bottom, height;
        public readonly bool glazed, door;

        Opening(float at, float width, float bottom, float height, bool glazed, bool door) {
            this.at = at; this.width = width; this.bottom = bottom; this.height = height; this.glazed = glazed; this.door = door;
        }

        /// <summary>A doorway with an open door leaf swung back against the wall (good cover).</summary>
        public static Opening Door(float at, float width = HS_ManorPlan.DoorWidth) => new(at, width, 0f, HS_ManorPlan.DoorHeight, false, true);
        /// <summary>A doorway with no door.</summary>
        public static Opening Arch(float at, float width = 2.2f) => new(at, width, 0f, HS_ManorPlan.DoorHeight + 0.2f, false, false);
        /// <summary>A glazed window. Glass blocks players but not the view, so a window is a way to be spotted.</summary>
        public static Opening Window(float at, float width = 1.6f, float bottom = 0.95f, float height = 1.4f) => new(at, width, bottom, height, true, false);
        /// <summary>An opening on the upper storey of an exterior wall.</summary>
        public Opening Upstairs => new(at, width, bottom + HS_ManorPlan.StoreyHeight, height, glazed, door);
    }

    public readonly struct WallSpec {
        public readonly Vector2 a, b;
        public readonly int level;
        public readonly WallKind kind;
        public readonly Opening[] openings;

        public WallSpec(WallKind kind, int level, Vector2 a, Vector2 b, params Opening[] openings) {
            this.kind = kind; this.level = level; this.a = a; this.b = b; this.openings = openings;
        }

        public float Length => Vector2.Distance(a, b);
    }

    /// <summary>
    /// A named floor area. Rooms get a slab and a ceiling and appear in the HUD as "where am I". Most
    /// rooms are lit by their lamps and candles (lit props); a room with no light of its own can have a
    /// dim ceiling <see cref="fill"/> light instead, and a room with neither stays dark (good hiding).
    /// </summary>
    public readonly struct RoomSpec {
        public readonly string name;
        public readonly Rect area;
        public readonly int level;
        public readonly Surface floor;
        public readonly bool indoor;
        /// <summary>Ceiling fill light strength, as a multiple of <see cref="HS_ManorLighting.FillIntensity"/>; 0 = none.</summary>
        public readonly float fill;

        public RoomSpec(string name, int level, float x0, float z0, float x1, float z1, Surface floor, bool indoor = true, float fill = 0f) {
            this.name = name; this.level = level; this.floor = floor; this.indoor = indoor; this.fill = fill;
            area = Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(z0, z1), Mathf.Max(x0, x1), Mathf.Max(z0, z1));
        }
    }

    /// <summary>One model placed in the plan. <see cref="model"/> is "pack/name" (see <see cref="HS_PropCatalog"/>).</summary>
    public readonly struct PropSpec {
        public readonly string model;
        public readonly Vector3 at;
        public readonly float yaw, scale;
        public readonly bool solid;
        /// <summary>The prop gives off light (a lamp, candle or lantern; see <see cref="HS_ManorLighting.PropLightFor"/>).</summary>
        public readonly bool lit;

        public PropSpec(string model, Vector3 at, float yaw, float scale, bool solid, bool lit = false) {
            this.model = model; this.at = at; this.yaw = yaw; this.scale = scale; this.solid = solid; this.lit = lit;
        }
    }

    /// <summary>Built-in joinery (wardrobes, bookcases, counters): a wooden box players can hide behind.</summary>
    public readonly struct BlockSpec {
        public readonly string name;
        public readonly Vector3 center, size;
        public readonly Surface surface;

        public BlockSpec(string name, Vector3 center, Vector3 size, Surface surface) {
            this.name = name; this.center = center; this.size = size; this.surface = surface;
        }
    }

    /// <summary>
    /// "Pumpkin Hollow Manor": the Hide and Seek map, as data. Everything is in plan metres (a person is
    /// about 1.8 m tall; <see cref="HS_ManorBuilder"/> scales the whole plan to the game's character),
    /// x runs west to east, z south to north, and the front door faces south.
    /// <para>
    /// Ground floor: foyer, living room, dining room, hall, kitchen, library, bathroom, stairs and a
    /// pantry closet. Upstairs: master bedroom with a walk-in closet and balcony, kids' room, bathroom,
    /// guest room, linen closet and a junk-filled attic. Outside: front garden with a pumpkin patch,
    /// garage, garden shed, picnic lawn and an old family graveyard with a crypt.
    /// </para>
    /// Change the layout here and run Ryan > Hide and Seek > Build Client Assets.
    /// </summary>
    public static partial class HS_ManorPlan {
        public const string MapName = "Pumpkin Hollow Manor";

        // ---- dimensions (plan metres)
        public const float StoreyHeight = 3.4f;
        public const float SlabThickness = 0.2f;
        public const float DoorWidth = 1.4f;
        public const float DoorHeight = 2.4f;
        public const float ExteriorThickness = 0.3f;
        public const float InteriorThickness = 0.16f;
        public const float RailingHeight = 1.05f;
        /// <summary>The house footprint (exterior wall centre lines).</summary>
        public static readonly Rect House = Rect.MinMaxRect(-12f, -8f, 12f, 8f);
        /// <summary>The fenced plot.</summary>
        public static readonly Rect Plot = Rect.MinMaxRect(-34f, -30f, 34f, 30f);
        public const float RoofPitch = 32f;
        public const float RoofOverhang = 0.7f;

        // ---- stairs: a straight flight in the stair well, rising east
        public static readonly Rect StairWell = Rect.MinMaxRect(-4f, 3.4f, 0.2f, 5.4f);
        public const int StairSteps = 14;

        // ---- gameplay anchors
        public static readonly Vector3 SeekerSpawn = new(0f, 0f, -4.2f);
        public static readonly Vector3 LobbySpawn = new(0f, 0f, -22f);
        /// <summary>Where the grandfather clock ticks: the tall case by the foyer stairs.</summary>
        public static readonly Vector3 ClockAt = new(2.55f, 1.8f, -6.1f);

        static Vector2 V(float x, float z) => new(x, z);

        public static readonly RoomSpec[] Rooms = {
            // ground floor
            new("Foyer", 0, -3f, -8f, 3f, 1f, Surface.Checker),
            new("Living Room", 0, -12f, -8f, -3f, 1f, Surface.Laminate),
            new("Dining Room", 0, 3f, -8f, 12f, 1f, Surface.Parquet),
            new("Hallway", 0, -12f, 1f, 12f, 3.4f, Surface.Laminate),
            new("Library", 0, -12f, 3.4f, -4f, 8f, Surface.Parquet),
            new("Stairs", 0, -4f, 3.4f, 0.2f, 5.4f, Surface.Laminate),
            new("Pantry", 0, -4f, 5.4f, 0.2f, 8f, Surface.DarkPlanks),
            new("Bathroom", 0, 0.2f, 3.4f, 4f, 8f, Surface.Tiles, fill: 0.35f),
            new("Kitchen", 0, 4f, 3.4f, 12f, 8f, Surface.Tiles, fill: 0.45f),
            // upstairs
            new("Master Bedroom", 1, -12f, -8f, -2f, 1f, Surface.Laminate),
            new("Upstairs Bathroom", 1, -2f, -8f, 2f, 1f, Surface.Tiles),
            new("Kids' Room", 1, 2f, -8f, 12f, 1f, Surface.Laminate),
            new("Upstairs Hall", 1, -12f, 1f, 12f, 3.4f, Surface.Laminate),
            new("Landing", 1, 0.2f, 3.4f, 2.2f, 5.4f, Surface.Laminate),
            new("Guest Room", 1, -12f, 3.4f, -4f, 8f, Surface.Parquet),
            new("Linen Closet", 1, -4f, 5.4f, 2.2f, 8f, Surface.DarkPlanks),
            new("Attic Storage", 1, 2.2f, 3.4f, 12f, 8f, Surface.DarkPlanks),
            new("Balcony", 1, -11f, -10.2f, -3f, -8f, Surface.DarkPlanks, indoor: false),
            // outbuildings
            new("Garage", 0, 16f, -14f, 26f, -4f, Surface.Driveway),
            new("Garden Shed", 0, -28f, 10f, -20f, 17f, Surface.Planks),
        };

        /// <summary>Outdoor zones, for the HUD only (checked after the rooms).</summary>
        public static readonly RoomSpec[] Yards = {
            new("Graveyard", 0, 12f, 12f, 34f, 30f, Surface.Grass, indoor: false),
            new("Front Garden", 0, -34f, -30f, 34f, -8f, Surface.Grass, indoor: false),
            new("Backyard", 0, -34f, 8f, 34f, 30f, Surface.Grass, indoor: false),
            new("Side Yard", 0, -34f, -30f, 34f, 30f, Surface.Grass, indoor: false),
        };

        public static readonly WallSpec[] Walls = {
            // ---- exterior (both storeys; upstairs openings use .Upstairs)
            new(WallKind.Exterior, 0, V(-12f, -8f), V(12f, -8f),
                Opening.Window(3f), Opening.Window(6.5f), Opening.Door(12f, 1.8f), Opening.Window(17.5f), Opening.Window(21f),
                Opening.Window(2f).Upstairs, Opening.Door(6f).Upstairs, Opening.Window(12f, 0.9f, 1.5f, 0.8f).Upstairs,
                Opening.Window(17f).Upstairs, Opening.Window(21f).Upstairs),
            new(WallKind.Exterior, 0, V(-12f, 8f), V(12f, 8f),
                Opening.Window(2f), Opening.Window(6f), Opening.Window(14f, 0.9f, 1.5f, 0.8f), Opening.Window(18f), Opening.Door(22f),
                Opening.Window(2f).Upstairs, Opening.Window(6f).Upstairs, Opening.Window(17f).Upstairs, Opening.Window(21f).Upstairs),
            new(WallKind.Exterior, 0, V(-12f, -8f), V(-12f, 8f),
                Opening.Window(3f), Opening.Window(6.5f), Opening.Window(13.5f),
                Opening.Window(5f).Upstairs, Opening.Window(13.5f).Upstairs),
            new(WallKind.Exterior, 0, V(12f, -8f), V(12f, 8f),
                Opening.Window(3f), Opening.Window(6.5f), Opening.Window(13.5f),
                Opening.Window(5f).Upstairs, Opening.Window(13.5f).Upstairs),

            // ---- ground floor
            new(WallKind.Interior, 0, V(-3f, -8f), V(-3f, 1f), Opening.Arch(4.5f)),
            new(WallKind.Interior, 0, V(3f, -8f), V(3f, 1f), Opening.Arch(4.5f)),
            new(WallKind.Interior, 0, V(-12f, 1f), V(-3f, 1f), Opening.Door(4.5f)),
            new(WallKind.Interior, 0, V(-3f, 1f), V(3f, 1f), Opening.Arch(3f, 3.6f)),
            new(WallKind.Interior, 0, V(3f, 1f), V(12f, 1f), Opening.Door(4.5f)),
            new(WallKind.Interior, 0, V(-12f, 3.4f), V(-4f, 3.4f), Opening.Door(4f)),
            new(WallKind.Interior, 0, V(0.2f, 3.4f), V(4f, 3.4f), Opening.Door(1.9f, 1.2f)),
            new(WallKind.Interior, 0, V(4f, 3.4f), V(12f, 3.4f), Opening.Arch(4f)),
            new(WallKind.Interior, 0, V(-4f, 3.4f), V(-4f, 8f), Opening.Door(3.3f, 1.2f)),
            new(WallKind.Interior, 0, V(-4f, 5.4f), V(0.2f, 5.4f)),
            new(WallKind.Interior, 0, V(0.2f, 3.4f), V(0.2f, 8f)),
            new(WallKind.Interior, 0, V(4f, 3.4f), V(4f, 8f)),

            // ---- upstairs
            new(WallKind.Interior, 1, V(-12f, 1f), V(-2f, 1f), Opening.Door(6f)),
            new(WallKind.Interior, 1, V(-2f, 1f), V(2f, 1f), Opening.Door(2f, 1.2f)),
            new(WallKind.Interior, 1, V(2f, 1f), V(12f, 1f), Opening.Door(4f)),
            new(WallKind.Interior, 1, V(-2f, -8f), V(-2f, 1f)),
            new(WallKind.Interior, 1, V(2f, -8f), V(2f, 1f)),
            new(WallKind.Interior, 1, V(-9f, -8f), V(-9f, -5f)),
            new(WallKind.Interior, 1, V(-12f, -5f), V(-9f, -5f), Opening.Door(1.5f, 1.2f)),
            new(WallKind.Interior, 1, V(-12f, 3.4f), V(-4f, 3.4f), Opening.Door(4f)),
            new(WallKind.Railing, 1, V(-4f, 3.4f), V(0.2f, 3.4f)),
            new(WallKind.Interior, 1, V(2.2f, 3.4f), V(12f, 3.4f), Opening.Arch(4.8f, 2f)),
            new(WallKind.Interior, 1, V(-4f, 3.4f), V(-4f, 8f)),
            new(WallKind.Interior, 1, V(-4f, 5.4f), V(2.2f, 5.4f), Opening.Door(5.2f, 1.2f)),
            new(WallKind.Interior, 1, V(2.2f, 3.4f), V(2.2f, 8f)),
            new(WallKind.Railing, 1, V(-11f, -10.2f), V(-3f, -10.2f)),
            new(WallKind.Railing, 1, V(-11f, -10.2f), V(-11f, -8f)),
            new(WallKind.Railing, 1, V(-3f, -10.2f), V(-3f, -8f)),

            // ---- garage (big door to the drive, side door to the garden)
            new(WallKind.Garage, 0, V(16f, -14f), V(26f, -14f), Opening.Arch(5f, 5.5f)),
            new(WallKind.Garage, 0, V(16f, -4f), V(26f, -4f), Opening.Door(7.5f, 1.2f)),
            new(WallKind.Garage, 0, V(16f, -14f), V(16f, -4f), Opening.Window(5f)),
            new(WallKind.Garage, 0, V(26f, -14f), V(26f, -4f), Opening.Window(5f)),

            // ---- garden shed
            new(WallKind.Shed, 0, V(-28f, 10f), V(-20f, 10f), Opening.Window(4f, 1.2f)),
            new(WallKind.Shed, 0, V(-28f, 17f), V(-20f, 17f)),
            new(WallKind.Shed, 0, V(-28f, 10f), V(-28f, 17f)),
            new(WallKind.Shed, 0, V(-20f, 10f), V(-20f, 17f), Opening.Door(3.5f, 1.6f)),
        };

        // ---------------------------------------------------------------- props
        static readonly List<PropSpec> props = new();
        static readonly List<BlockSpec> blocks = new();

        public static IReadOnlyList<PropSpec> Props { get { EnsureFurnished(); return props; } }
        public static IReadOnlyList<BlockSpec> Blocks { get { EnsureFurnished(); return blocks; } }

        static float Floor(int level) => level * StoreyHeight;

        /// <summary>Places a model with its base on the floor of <paramref name="level"/>.</summary>
        static void P(string model, int level, float x, float z, float yaw = 0f, float scale = 1f, bool solid = true, float lift = 0f, bool lit = false) =>
            props.Add(new PropSpec(model, new Vector3(x, Floor(level) + lift, z), yaw, scale, solid, lit));

        /// <summary>A model that sits on top of a block or another piece of furniture.</summary>
        static void On(string model, int level, float x, float height, float z, float yaw = 0f, float scale = 1f, bool lit = false) =>
            props.Add(new PropSpec(model, new Vector3(x, Floor(level) + height, z), yaw, scale, false, lit));

        /// <summary>A box of joinery; <paramref name="bottom"/> raises it off the floor (decks, shelves).</summary>
        static void Block(string name, int level, float x, float z, float sx, float sy, float sz, Surface surface = Surface.Trim, float bottom = 0f) =>
            blocks.Add(new BlockSpec(name, new Vector3(x, Floor(level) + bottom + sy * 0.5f, z), new Vector3(sx, sy, sz), surface));

        /// <summary>A tall wardrobe against a wall: the best indoor cover there is.</summary>
        static void Wardrobe(int level, float x, float z, bool alongX, float width = 1.8f) =>
            Block("Wardrobe", level, x, z, alongX ? width : 0.7f, 2.2f, alongX ? 0.7f : width);

        /// <summary>A floor-to-ceiling bookcase with books on its shelves.</summary>
        static void Bookcase(int level, float x, float z, float yaw, float width = 2f) {
            bool alongX = Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) < 1f;
            Block("Bookcase", level, x, z, alongX ? width : 0.45f, 2.6f, alongX ? 0.45f : width, Surface.DarkPlanks);
            Vector3 front = Quaternion.Euler(0f, yaw, 0f) * Vector3.back * 0.3f;
            for (int shelf = 0; shelf < 3; shelf++)
                On("furniture/book_set", level, x + front.x, 0.55f + shelf * 0.7f, z + front.z, yaw, 0.9f);
        }

        /// <summary>A wardrobe-high stack of lidded crates (each crate 0.6 m, lid 0.15 m at this scale).</summary>
        static void CrateStack(int level, float x, float z, float yaw, int count, float scale = 0.75f) {
            float crate = 0.8f * HS_PropCatalog.KayKitToPlan * scale, lid = 0.2f * HS_PropCatalog.KayKitToPlan * scale;
            for (int i = 0; i < count; i++) {
                float lift = i * (crate + lid - 0.02f), turn = yaw + i * 9f;
                P("restaurant/crate", level, x, z, turn, scale, lift: lift);
                P("restaurant/crate_lid", level, x, z, turn, scale, solid: false, lift: lift + crate - 0.04f);
            }
        }

        /// <summary>A row of small pines planted close together: a hedge you can hide behind.</summary>
        static void Hedge(float x0, float z0, float x1, float z1, float spacing = 1.5f) {
            float length = Vector2.Distance(new Vector2(x0, z0), new Vector2(x1, z1));
            int count = Mathf.Max(1, Mathf.RoundToInt(length / spacing));
            for (int i = 0; i <= count; i++) {
                float t = i / (float)count;
                string tree = i % 2 == 0 ? "halloween/tree_pine_yellow_small" : "halloween/tree_pine_orange_small";
                P(tree, 0, Mathf.Lerp(x0, x1, t), Mathf.Lerp(z0, z1, t), i * 47f % 360f, 1.05f + (i % 3) * 0.08f);
            }
        }

        static void EnsureFurnished() {
            if (props.Count > 0) return;
            FurnishGroundFloor();
            FurnishUpstairs();
            FurnishOutbuildings();
            FurnishGarden();
            AddDetail();
        }

        static void FurnishGroundFloor() {
            // Foyer: rug, coat cupboard, plants, a grandfather-tall cabinet by the stairs.
            P("furniture/rug_rectangle_stripes_A", 0, 0f, -4.5f, 90f, solid: false);
            P("furniture/cabinet_medium_decorated", 0, -2.2f, -6.8f, 90f);
            P("furniture/cactus_medium_A", 0, 2.3f, -7.55f, scale: 0.9f);
            P("furniture/cactus_medium_B", 0, 2.3f, -1.5f);
            P("furniture/lamp_standing", 0, -2.5f, 0.45f, lit: true);
            // The tall case clock (its tick comes from ClockAt).
            Wardrobe(0, 2.55f, -6.1f, alongX: false, width: 1.6f);

            // Living room: sofas around a low table, TV cabinet, reading corner, bookcase.
            P("furniture/rug_oval_A", 0, -7.5f, -4f, solid: false);
            P("furniture/couch_pillows", 0, -7.5f, -6.7f, 0f);
            P("furniture/couch", 0, -10.9f, -3.8f, 90f);
            P("furniture/armchair_pillows", 0, -4.6f, -2.2f, 225f);
            P("furniture/table_low", 0, -7.5f, -4.2f);
            On("furniture/cactus_small_A", 0, -7.2f, 0.38f, -4.1f);
            P("furniture/cabinet_medium", 0, -7.5f, 0.4f, 180f);
            On("furniture/lamp_table", 0, -6.6f, 0.75f, 0.4f, 180f, 0.8f, lit: true);
            P("furniture/lamp_standing", 0, -11.3f, -7.3f, lit: true);
            Bookcase(0, -11.65f, 0f, 90f, 1.8f);
            P("furniture/pictureframe_large_B", 0, -7.5f, -7.78f, 0f, solid: false, lift: 1.9f);

            // Dining room: long table, chairs, sideboards, tall dresser.
            P("furniture/rug_rectangle_A", 0, 7.5f, -3.6f, 0f, 1.2f, solid: false);
            P("furniture/table_medium_long", 0, 7.5f, -3.6f);
            foreach (float x in new[] { 6.6f, 8.4f }) {
                P("furniture/chair_A_wood", 0, x, -2.4f, 180f);
                P("furniture/chair_A_wood", 0, x, -4.8f, 0f);
            }
            P("furniture/chair_B_wood", 0, 5.4f, -3.6f, 90f);
            P("furniture/chair_B_wood", 0, 9.6f, -3.6f, 270f);
            On("halloween/candle_triple", 0, 7.5f, 0.75f, -3.6f, 0f, 0.8f, lit: true);
            P("furniture/cabinet_medium_decorated", 0, 7.5f, 0.45f, 180f);
            Wardrobe(0, 11.55f, -6.8f, alongX: false, width: 1.6f);
            P("furniture/cactus_medium_B", 0, 11.4f, -1.2f);

            // Hallway: console tables and coat racks along the walls.
            P("furniture/cabinet_small_decorated", 0, -10.5f, 3.0f, 180f);
            P("furniture/cabinet_small_decorated", 0, 10.6f, 1.4f, 0f);
            P("furniture/lamp_standing", 0, 4.8f, 1.5f, lit: true);
            P("furniture/rug_rectangle_B", 0, -7.5f, 2.2f, 0f, 1f, solid: false);

            // Library: bookcases on every wall, desk, armchairs.
            Bookcase(0, -8f, 7.65f, 180f, 3.6f);
            Bookcase(0, -11.65f, 5.7f, 90f, 3f);
            Bookcase(0, -4.4f, 5.2f, 270f, 2f);
            P("furniture/table_medium", 0, -8.2f, 5.4f, 90f);
            P("furniture/chair_C", 0, -9.4f, 5.4f, 90f);
            On("furniture/lamp_table", 0, -8f, 0.75f, 5.9f, 0f, 0.7f, lit: true);
            P("furniture/armchair", 0, -10.8f, 3.9f, 135f);
            P("furniture/rug_oval_B", 0, -8f, 5.4f, 90f, solid: false);

            // Pantry closet: shelves of food crates.
            P("restaurant/crate_potatoes", 0, -3f, 7.1f, 0f, 0.7f);
            P("restaurant/crate_onions", 0, -1f, 7.1f, 0f, 0.7f);
            P("restaurant/crate_carrots", 0, -3f, 7.1f, 15f, 0.6f, lift: 0.62f);
            P("restaurant/shelf_papertowel_decorated", 0, -1.6f, 5.75f, 180f, lift: 1.2f, solid: false);

            // Bathroom: shower cubicle (cover), sink counter.
            HideIn("Shower", 0, 1.3f, 7.0f, Facing.South, 1.8f, 1.8f, Surface.Tiles);
            P("restaurant/kitchentable_sink", 0, 3.25f, 4.8f, 270f, 0.75f);
            P("restaurant/towelrail", 0, 3.85f, 6.6f, 270f, solid: false, lift: 1.1f);

            // Kitchen: counters along the north and east walls, island, fridge, table.
            P("restaurant/kitchencounter_straight_A_backsplash", 0, 5.3f, 7.25f, 180f);
            P("restaurant/kitchencounter_sink_backsplash", 0, 6.8f, 7.25f, 180f);
            P("restaurant/stove_multi_decorated", 0, 8.3f, 7.2f, 180f);
            P("restaurant/fridge_A_decorated", 0, 11f, 6.9f, 270f);
            P("restaurant/kitchencounter_straight_decorated", 0, 11.25f, 4.7f, 270f);
            P("restaurant/kitchentable_A_large_decorated", 0, 7.2f, 4.9f, 0f);
            P("restaurant/chair_A", 0, 5.6f, 4.9f, 90f);
            P("restaurant/kitchencabinet", 0, 5.3f, 7.6f, 180f, solid: false, lift: -0.2f);
            P("restaurant/kitchencabinet", 0, 6.8f, 7.6f, 180f, solid: false, lift: -0.2f);
            P("restaurant/crate_tomatoes", 0, 9.8f, 4.0f, 30f, 0.65f);
        }

        static void FurnishUpstairs() {
            // Master bedroom: double bed, wardrobes, dresser, walk-in closet full of clothes rails.
            P("furniture/rug_rectangle_stripes_B", 1, -5.8f, -4.2f, 90f, 1.1f, solid: false);
            P("furniture/bed_double_A", 1, -5.6f, -2.6f, 180f);
            P("furniture/cabinet_small", 1, -7.6f, 0.35f, 180f);
            On("furniture/lamp_table", 1, -7.6f, 0.75f, 0.35f, 180f, 0.8f, lit: true);
            P("furniture/cabinet_small", 1, -3.6f, 0.35f, 180f);
            Wardrobe(1, -2.45f, -5.2f, alongX: false, width: 2f);
            P("furniture/cabinet_medium_decorated", 1, -11.55f, -1.5f, 90f);
            P("furniture/armchair_pillows", 1, -8f, -6.9f, 30f);
            Wardrobe(1, -11.6f, -6.5f, alongX: false, width: 2.6f);
            Wardrobe(1, -10.5f, -7.6f, alongX: true, width: 1.4f);
            P("furniture/pictureframe_large_A", 1, -5.6f, 0.86f, 180f, solid: false, lift: 1.7f);

            // Balcony: pots and a bench.
            P("halloween/bench", 1, -7f, -9.7f, 0f);
            P("furniture/cactus_medium_A", 1, -10.4f, -9.6f);
            P("halloween/pumpkin_orange_jackolantern", 1, -3.6f, -9.6f, 200f, 0.7f);

            // Upstairs bathroom: bath tub cover (block), cabinet.
            Block("BathTub", 1, 0f, -6.9f, 3.4f, 0.75f, 1.8f, Surface.Trim);
            Block("Cupboard", 1, -1.45f, -1.5f, 0.8f, 2.1f, 1.6f);
            P("restaurant/towelrail", 1, 1.85f, -3.5f, 270f, solid: false, lift: 1.1f);

            // Kids' room: bunk-ish single beds, toy shelves, a fort of cushions and crates.
            P("furniture/bed_single_B", 1, 3.4f, -5.6f, 0f);
            P("furniture/bed_single_A", 1, 11f, -5.6f, 0f);
            P("furniture/rug_oval_B", 1, 7f, -3.6f, solid: false);
            P("furniture/table_small", 1, 7f, -6.8f);
            P("furniture/chair_stool", 1, 7f, -5.8f);
            Wardrobe(1, 11.55f, -0.9f, alongX: false, width: 1.6f);
            Bookcase(1, 2.45f, -1.6f, 270f, 1.8f);
            P("restaurant/crate", 1, 9.2f, -1.6f, 20f, 0.7f);
            P("restaurant/crate", 1, 9.6f, -0.2f, -10f, 0.6f);
            P("restaurant/crate_lid", 1, 9.2f, -1.6f, 40f, 0.7f, lift: 0.6f, solid: false);
            P("furniture/pillow_A", 1, 7.4f, -3.2f, 30f, 1.5f, solid: false);
            P("furniture/pillow_B", 1, 6.6f, -4f, 70f, 1.5f, solid: false);

            // Upstairs hall.
            P("furniture/cabinet_small_decorated", 1, -10.6f, 1.4f, 0f);
            P("furniture/rug_rectangle_A", 1, 7f, 2.2f, 0f, 1f, solid: false);
            P("furniture/lamp_standing", 1, 11.4f, 2.9f, lit: true);

            // Guest room: single bed, dresser, wardrobe, desk.
            P("furniture/bed_single_A", 1, -10.9f, 6.7f, 270f);
            Wardrobe(1, -4.45f, 6.9f, alongX: false, width: 1.8f);
            P("furniture/cabinet_medium", 1, -8f, 7.55f, 180f);
            On("furniture/pictureframe_standing_A", 1, -8.4f, 0.75f, 7.55f, 180f);
            P("furniture/table_small", 1, -6.4f, 4.1f);
            P("furniture/chair_B", 1, -6.4f, 4.9f, 180f);

            // Linen closet: tall shelves and laundry baskets.
            Block("LinenShelves", 1, -2.2f, 7.65f, 3.4f, 2.4f, 0.6f, Surface.Trim);
            P("restaurant/crate_lid", 1, -0.6f, 6.4f, 15f, 0.6f, solid: false);

            // Attic storage: a maze of crates, boxes and old furniture.
            (float x, float z, float yaw, int stack)[] crates = {
                (4f, 7.1f, 5f, 3), (5.7f, 7.2f, -8f, 2), (4.1f, 5.3f, 20f, 1), (8.2f, 7.1f, 0f, 3), (9.9f, 7.1f, 12f, 2),
                (11.1f, 5.5f, -15f, 3), (7f, 4.2f, 40f, 2), (9.6f, 4.1f, 10f, 1), (11.1f, 3.9f, 0f, 2),
            };
            foreach (var c in crates) CrateStack(1, c.x, c.z, c.yaw, c.stack);
            P("halloween/coffin", 1, 6.5f, 6f, 80f, 0.85f);
            // The attic's only light: a guttering candle by the coffin.
            P("halloween/candle_melted", 1, 7.7f, 5.1f, 0f, 1f, solid: false, lit: true);
            P("furniture/armchair", 1, 3.2f, 3.9f, 160f);
            Wardrobe(1, 6.2f, 7.6f, alongX: true, width: 1.6f);
        }

        static void FurnishOutbuildings() {
            // Garage: shelving, crates, workbench, oil barrels.
            Block("Workbench", 0, 25.3f, -9f, 1.1f, 0.95f, 3.2f, Surface.DarkPlanks);
            Block("ShelvingNorth", 0, 19.5f, -4.45f, 5f, 2.3f, 0.7f, Surface.Trim);
            Block("ShelvingWest", 0, 16.45f, -9.5f, 0.7f, 2.3f, 4f, Surface.Trim);
            CrateStack(0, 22.5f, -5.4f, 10f, 3);
            CrateStack(0, 18.4f, -12.6f, -10f, 2);
            On("restaurant/pot_large", 0, 25.3f, 0.95f, -10f, 90f, 0.6f);
            On("halloween/lantern_standing", 0, 25.3f, 0.95f, -7.6f, 0f, 0.8f, lit: true);

            // Shed: crates, tools, pumpkins in storage.
            CrateStack(0, -26.8f, 15.8f, 10f, 3);
            P("restaurant/crate_potatoes", 0, -24.8f, 16f, -5f, 0.75f);
            Block("PottingBench", 0, -27.4f, 12f, 0.9f, 0.95f, 3f, Surface.Planks);
            On("halloween/lantern_standing", 0, -27.4f, 0.95f, 13f, 0f, 0.8f, lit: true);
            P("halloween/pumpkin_yellow", 0, -22.6f, 16.1f);
            P("halloween/pumpkin_orange_small", 0, -21.8f, 16.3f);
            Block("ToolCupboard", 0, -22.3f, 10.45f, 2.4f, 2.2f, 0.7f, Surface.DarkPlanks);
        }

        static void FurnishGarden() {
            // Front path lanterns and a pumpkin patch.
            // Only the pair by the door is lit: a handful of lights keeps the frame rate up.
            for (float z = -26f; z <= -12f; z += 4.6f) {
                bool byTheDoor = z > -14f;
                P("halloween/lantern_standing", 0, -1.7f, z, 0f, 1.2f, lit: byTheDoor);
                P("halloween/lantern_standing", 0, 1.7f, z, 0f, 1.2f, lit: byTheDoor);
            }
            var random = new System.Random(7);
            string[] pumpkins = { "pumpkin_orange", "pumpkin_yellow", "pumpkin_orange_small", "pumpkin_yellow_small", "pumpkin_orange_jackolantern", "pumpkin_yellow_jackolantern" };
            for (int i = 0; i < 26; i++) {
                float x = -14f + (float)random.NextDouble() * 9f, z = -25f + (float)random.NextDouble() * 9f;
                P("halloween/" + pumpkins[random.Next(pumpkins.Length)], 0, x, z, random.Next(360), 0.9f + (float)random.NextDouble() * 0.5f, solid: i % 3 == 0);
            }
            P("halloween/post_lantern", 0, -9.5f, -15.5f, 90f, 1.2f, lit: true);
            P("halloween/pumpkin_orange_jackolantern", 0, -9.6f, -20.2f, 160f, 1.3f, lit: true);

            // Trees: rows along the fence and clumps that make hiding pockets.
            (float x, float z, string tree, float scale)[] trees = {
                (-30f, -26f, "tree_pine_orange_large", 1.5f), (-24f, -27f, "tree_pine_yellow_medium", 1.4f), (-18f, -26.5f, "tree_pine_orange_medium", 1.5f),
                (18f, -26f, "tree_pine_yellow_large", 1.5f), (25f, -27f, "tree_pine_orange_medium", 1.4f), (31f, -25f, "tree_pine_yellow_medium", 1.5f),
                (-31f, -16f, "tree_pine_yellow_large", 1.6f), (-29f, -9f, "tree_pine_orange_large", 1.5f), (-31f, -1f, "tree_pine_yellow_medium", 1.5f),
                (-24f, -14f, "tree_pine_orange_small", 1.6f), (-22f, -4f, "tree_pine_yellow_small", 1.6f), (-17f, -18f, "tree_pine_orange_medium", 1.3f),
                (31f, -14f, "tree_pine_orange_large", 1.5f), (30f, -5f, "tree_pine_yellow_large", 1.4f), (31f, 5f, "tree_pine_orange_medium", 1.5f),
                (-31f, 8f, "tree_pine_orange_large", 1.5f), (-31f, 21f, "tree_pine_yellow_large", 1.5f), (-25f, 27f, "tree_pine_orange_medium", 1.5f),
                (-16f, 27f, "tree_pine_yellow_medium", 1.4f), (-6f, 27.5f, "tree_pine_orange_large", 1.5f), (4f, 27f, "tree_pine_yellow_medium", 1.4f),
                (-15f, 14f, "tree_pine_orange_small", 1.7f), (-12f, 20f, "tree_pine_yellow_small", 1.7f), (8f, 15f, "tree_pine_orange_small", 1.6f),
                (16f, 3f, "tree_pine_yellow_medium", 1.4f), (20f, 7f, "tree_pine_orange_small", 1.6f), (-17f, 3f, "tree_pine_orange_medium", 1.3f),
            };
            foreach (var t in trees) P("halloween/" + t.tree, 0, t.x, t.z, t.x * 37f % 360f, t.scale);

            // Picnic lawn behind the house.
            P("furniture/table_medium_long", 0, -3f, 16f, 90f);
            P("halloween/bench", 0, -4.6f, 16f, 90f, 1.1f);
            P("halloween/bench", 0, -1.4f, 16f, 270f, 1.1f);
            P("halloween/post_lantern", 0, 1.5f, 12f, 180f, 1.2f, lit: true);
            CrateStack(0, -9f, 22f, 15f, 2);
            CrateStack(0, -7.3f, 22.6f, -5f, 1);

            // Hedges: a hedged pumpkin garden, a hedge square with a bench, and rows along the drive.
            Hedge(-30f, -11.5f, -15.5f, -11.5f);
            Hedge(-15.5f, -11.5f, -15.5f, -20f);
            Hedge(6f, -25f, 6f, -17f);
            Hedge(6f, -17f, 12f, -17f);
            Hedge(12f, -17f, 12f, -22f);
            Hedge(6f, -25f, 10f, -25f);
            P("halloween/bench_decorated", 0, 9f, -21f, 90f);
            P("halloween/shrine_candles", 0, 10.8f, -23.6f, 270f, lit: true);
            Hedge(16.5f, -16f, 16.5f, -28f, 1.7f);
            Hedge(25.5f, -16f, 25.5f, -28f, 1.7f);
            Hedge(-30f, 3f, -22f, 3f);
            Hedge(-18f, 8f, -18f, 20f);

            // Tree fort: a deck on stilts in the west yard, reached by climbing the crates.
            Block("FortDeck", 0, -26f, 24f, 5f, 0.25f, 5f, Surface.DarkPlanks, bottom: 1.975f);
            foreach (var (px, pz) in new[] { (-28.3f, 21.7f), (-23.7f, 21.7f), (-28.3f, 26.3f), (-23.7f, 26.3f) })
                Block("FortPost", 0, px, pz, 0.3f, 2.1f, 0.3f, Surface.DarkPlanks);
            Block("FortWallNorth", 0, -26f, 26.4f, 5f, 1.1f, 0.15f, Surface.Planks, bottom: 2.225f);
            Block("FortWallWest", 0, -28.4f, 24f, 0.15f, 1.1f, 5f, Surface.Planks, bottom: 2.225f);
            CrateStack(0, -22.4f, 21.6f, 20f, 1);
            P("halloween/pumpkin_orange_jackolantern", 0, -27.2f, 25.2f, 150f, 0.8f, lift: 2.22f, lit: true);
            P("halloween/tree_dead_large", 0, 3f, 21f, 30f, 1.5f);

            // Graveyard: rows of graves, a crypt and dead trees behind an old iron fence.
            P("halloween/crypt", 0, 27.5f, 25.5f, 180f, 1.2f);
            for (int row = 0; row < 3; row++)
            for (int col = 0; col < 4; col++) {
                float x = 15.5f + col * 3.4f, z = 15f + row * 4f;
                string grave = (row + col) % 3 == 0 ? "grave_A" : (row + col) % 3 == 1 ? "grave_B" : "gravestone";
                P("halloween/" + grave, 0, x, z, 180f + (col - 1.5f) * 4f, 1.05f);
            }
            P("halloween/tree_dead_large_decorated", 0, 31f, 14f, 120f, 1.5f);
            P("halloween/tree_dead_medium", 0, 15f, 27f, 10f, 1.6f);
            P("halloween/shrine_candles", 0, 22f, 27.5f, 180f, lit: true);
            P("halloween/coffin_decorated", 0, 30.5f, 19f, 90f);
            P("halloween/bench_decorated", 0, 21f, 13.6f, 180f);
            for (float x = 12.5f; x < 34f; x += 3f) P("halloween/fence_seperate", 0, x + 1.5f, 12f, 0f, 1f);
            for (float z = 12f; z < 30f; z += 3f)
                if (z < 18f || z > 22f) P("halloween/fence_seperate", 0, 12f, z + 1.5f, 90f, 1f);
            P("halloween/arch_gate", 0, 12f, 20f, 90f, 1f, solid: false);
        }
    }
}
