using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Editor.Manor {
    /// <summary>Which way a hide-in spot's open side faces (+z is north).</summary>
    public enum Facing { North, South, East, West }

    /// <summary>
    /// The detail pass over the manor: walk-in hiding spots (closets, lockers, a shower, an outhouse,
    /// a mausoleum) and a dense layer of clutter in every room and yard, so there is always
    /// something to duck behind or squeeze into. Runs after the base furnishing in HS_ManorPlan.cs.
    /// <para>
    /// Hide-in spots are built from blocks, not models: models only get a solid box collider, so
    /// nobody could step inside one. Each spot is sized for the robot (about 0.62 m wide, 1.8 m tall
    /// in plan metres) with a gap of at least 0.85 m to slip in through.
    /// </para>
    /// </summary>
    public static partial class HS_ManorPlan {
        // ---- hide-in spot sizes (plan metres)
        const float SpotHeight = 2.3f;
        const float PanelThickness = 0.08f;
        /// <summary>How much of a spot's open side a half-closed door leaf covers.</summary>
        const float LeafShare = 0.35f;
        /// <summary>Scale for dungeon bottles and candles set on tables and shelves.</summary>
        const float SmallDungeonScale = 0.5f;

        static void AddDetail() {
            HideInSpots();
            DetailGroundFloor();
            DetailUpstairs();
            DetailOutbuildings();
            DetailGrounds();
            DetailBackyard();
        }

        // ---------------------------------------------------------------- building blocks

        static Vector2 Normal(Facing facing) => facing switch {
            Facing.North => Vector2.up,
            Facing.South => Vector2.down,
            Facing.East => Vector2.right,
            _ => Vector2.left,
        };

        /// <summary>An axis-aligned panel given in a spot's frame (tangent t, normal n).</summary>
        static void Panel(string name, int level, Vector2 centre, Facing facing, float alongT, float alongN, float height, Surface surface, float bottom = 0f) {
            bool normalIsX = facing is Facing.East or Facing.West;
            float sx = normalIsX ? alongN : alongT, sz = normalIsX ? alongT : alongN;
            Block(name, level, centre.x, centre.y, sx, height, sz, surface, bottom);
        }

        /// <summary>
        /// A walk-in hiding spot: back, two sides, a roof and a half-closed door leaf, open towards
        /// <paramref name="facing"/>. Used for closets, lockers, outhouses and the like.
        /// </summary>
        static void HideIn(string name, int level, float x, float z, Facing facing, float width = 1.5f, float depth = 1.1f,
            Surface surface = Surface.DarkPlanks, float height = SpotHeight) {
            Vector2 c = new(x, z), n = Normal(facing), t = new(n.y, -n.x);
            float th = PanelThickness;
            Panel(name + "Back", level, c - n * (depth * 0.5f - th * 0.5f), facing, width, th, height, surface);
            Panel(name + "Side", level, c + t * (width * 0.5f - th * 0.5f), facing, th, depth, height, surface);
            Panel(name + "Side", level, c - t * (width * 0.5f - th * 0.5f), facing, th, depth, height, surface);
            Panel(name + "Roof", level, c, facing, width, depth, th, surface, bottom: height - th);
            float leaf = width * LeafShare;
            Panel(name + "Door", level, c + n * (depth * 0.5f - th * 0.5f) + t * (width * 0.5f - leaf * 0.5f), facing, leaf, th, height - 0.15f, surface);
        }

        /// <summary>A few small items scattered on a surface (non-solid, purely for detail).</summary>
        static void Clutter(int level, float x, float height, float z, float spread, int seed, params string[] models) {
            var random = new System.Random(seed);
            for (int i = 0; i < models.Length; i++) {
                float ox = ((float)random.NextDouble() - 0.5f) * spread, oz = ((float)random.NextDouble() - 0.5f) * spread;
                // Dungeon props are tabletop-sized only at about half their modelled size.
                float scale = models[i].StartsWith("dungeon/") ? SmallDungeonScale : 1f;
                On(models[i], level, x + ox, height, z + oz, random.Next(360), scale);
            }
        }

        /// <summary>A picture on a wall; <paramref name="yaw"/> 0 faces north, 180 south.</summary>
        static void Picture(string model, int level, float x, float z, float yaw, float lift = 1.6f) =>
            P("furniture/" + model, level, x, z, yaw, solid: false, lift: lift);

        // ---------------------------------------------------------------- hide-in spots

        static void HideInSpots() {
            // Ground floor
            HideIn("CoatCloset", 0, -2.37f, -1.6f, Facing.East);
            HideIn("LivingCupboard", 0, -3.67f, -0.2f, Facing.West);
            HideIn("ChinaCabinet", 0, 11.3f, -0.2f, Facing.West);
            HideIn("Larder", 0, 4.85f, 4.0f, Facing.North, depth: 1.0f);
            // Upstairs
            HideIn("BedroomCloset", 1, -9.6f, 0.38f, Facing.South, depth: 1.0f);
            HideIn("BathCupboard", 1, 1.35f, -2.5f, Facing.West, width: 1.4f, surface: Surface.Trim);
            HideIn("PlayTent", 1, 11.3f, -3.2f, Facing.West, surface: Surface.Trim);
            HideIn("GuestCloset", 1, -11.3f, 4.45f, Facing.East);
            // Outbuildings and grounds
            HideIn("ToolLocker", 0, 25.3f, -5.4f, Facing.West, surface: Surface.Trim);
            HideIn("Outhouse", 0, -10.5f, 26.5f, Facing.South, 1.7f, 1.6f, Surface.Planks, 2.6f);
            HideIn("Mausoleum", 0, 19.2f, 27.4f, Facing.South, 2.2f, 1.8f, Surface.Brick, 2.8f);
            HideIn("PottingHut", 0, 28.8f, 9.5f, Facing.West, 1.8f, 1.5f, Surface.Planks, 2.5f);
            HideIn("Woodshed", 0, 7.2f, 26.6f, Facing.South, 2.4f, 1.4f, Surface.Planks, 2.4f);
            HideIn("Playhouse", 0, -12.5f, 12.5f, Facing.East, 1.8f, 1.6f, Surface.Trim, 2.2f);
            HideIn("GardenLocker", 0, 25.2f, 2.6f, Facing.West, 1.6f, 1.2f, Surface.DarkPlanks);
        }

        // ---------------------------------------------------------------- rooms

        static void DetailGroundFloor() {
            // Foyer
            P("dungeon/chest", 0, 1.5f, -7.4f, 0f, 0.8f);
            Picture("pictureframe_medium", 0, -1.6f, -7.86f, 0f);
            Picture("pictureframe_small_A", 0, 1.6f, -7.86f, 0f, 1.75f);
            P("furniture/cactus_small_B", 0, -1.4f, 0.55f);

            // Living room
            P("furniture/armchair", 0, -10.6f, -6.6f, 45f);
            P("furniture/table_small", 0, -9.8f, -1.6f);
            On("furniture/lamp_table", 0, -9.8f, 0.75f, -1.6f, 0f, 0.8f);
            P("furniture/shelf_B_large_decorated", 0, -10.2f, 0.6f, 180f);
            P("furniture/cactus_medium_A", 0, -3.5f, -7.4f);
            P("dungeon/trunk_medium_A", 0, -5.6f, -7.45f, 0f);
            P("furniture/pillow_A", 0, -11.2f, -2.8f, 80f, solid: false);
            Picture("pictureframe_large_A", 0, -11.88f, -3.8f, 90f, 1.7f);
            Clutter(0, -7.5f, 0.38f, -4.2f, 0.6f, 11, "furniture/book_single", "dungeon/candle_triple");
            P("furniture/armchair_pillows", 0, -4.4f, -6.5f, 300f);
            P("furniture/lamp_standing", 0, -5.4f, -7.5f, lit: true);
            P("furniture/rug_rectangle_stripes_B", 0, -9.4f, -1.4f, 0f, 0.9f, solid: false);
            P("furniture/cactus_medium_B", 0, -11.4f, -5.2f);

            // Dining room
            Clutter(0, 7.5f, 0.78f, -3.6f, 1.4f, 21, "restaurant/plate", "restaurant/plate", "restaurant/plate", "restaurant/plate",
                "dungeon/candle_triple", "dungeon/bottle_A_labeled_green", "restaurant/bowl");
            P("dungeon/keg_decorated", 0, 3.7f, -7.3f, 90f);
            P("dungeon/barrel_small_stack", 0, 4.7f, -7.4f, 20f);
            P("furniture/shelf_B_small_decorated", 0, 4.1f, 0.6f, 180f);
            Picture("pictureframe_large_B", 0, 7.5f, -7.86f, 0f, 1.8f);
            P("furniture/cabinet_medium_decorated", 0, 7.5f, -7.55f, 0f);
            P("furniture/shelf_B_large_decorated", 0, 9.6f, 0.6f, 180f);
            P("dungeon/table_small_decorated_A", 0, 11.3f, -3.4f, 90f, 0.8f);
            P("furniture/cactus_medium_A", 0, 5.8f, -7.5f);
            P("furniture/lamp_standing", 0, 9.6f, -7.5f, lit: true);

            // Hallway
            Picture("pictureframe_medium", 0, -4.6f, 1.12f, 0f);
            Picture("pictureframe_small_B", 0, 4.4f, 3.28f, 180f);
            Picture("pictureframe_small_C", 0, -10.4f, 3.28f, 180f, 1.9f);
            P("furniture/cactus_small_A", 0, 11.5f, 3.0f);
            On("furniture/lamp_table", 0, -10.5f, 0.75f, 3.0f, 180f, 0.7f, lit: true);
            P("halloween/bench", 0, -10f, 1.35f, 0f, 0.9f);
            P("furniture/cactus_medium_B", 0, -5.4f, 1.4f);
            P("dungeon/barrel_small", 0, 2.6f, 1.35f, 0f, 0.7f);
            P("furniture/chair_A_wood", 0, 5.2f, 3.0f, 180f);
            P("furniture/shelf_A_small", 0, 10.2f, 3.15f, 180f);
            P("furniture/rug_rectangle_A", 0, 4.6f, 2.2f, 0f, 1f, solid: false);

            // Library
            P("dungeon/chest_gold", 0, -5.2f, 7.4f, 180f);
            P("furniture/chair_C", 0, -7f, 5.4f, 270f);
            Clutter(0, -8.2f, 0.78f, 5.4f, 0.8f, 31, "furniture/book_single", "furniture/book_single", "dungeon/candle_melted", "dungeon/bottle_C_brown");
            P("dungeon/shelf_small_candles", 0, -10.9f, 7.6f, 180f, solid: false, lift: 1.4f);
            P("furniture/rug_rectangle_B", 0, -10.6f, 4.6f, 90f, 0.8f, solid: false);
            P("dungeon/table_small_decorated_B", 0, -5.4f, 4.3f, 0f, 0.8f);
            P("furniture/armchair_pillows", 0, -6.4f, 4.1f, 200f);

            // Pantry
            P("dungeon/barrel_large", 0, -0.45f, 5.95f, 0f, 0.8f);
            P("dungeon/keg", 0, -0.5f, 7.4f, 90f, 0.8f);
            P("dungeon/box_small", 0, -2.1f, 6.1f, 15f);

            // Bathroom
            P("dungeon/barrel_small", 0, 3.5f, 7.4f, 0f, 0.8f);
            On("dungeon/bottle_A_green", 0, 3.25f, 0.8f, 4.5f, 0f, 0.8f);

            // Kitchen
            On("restaurant/pot_A_stew", 0, 8.0f, 0.95f, 7.2f, 0f);
            On("restaurant/pan_A", 0, 8.6f, 0.95f, 7.3f, 30f);
            P("restaurant/extractorhood", 0, 8.3f, 7.55f, 180f, solid: false, lift: 1.9f);
            Clutter(0, 5.3f, 0.95f, 7.3f, 0.5f, 41, "restaurant/jar_A_large", "restaurant/jar_B_medium", "restaurant/jar_C_small");
            On("restaurant/dishrack_plates", 0, 6.4f, 0.95f, 7.3f, 180f);
            On("restaurant/cuttingboard", 0, 11.25f, 0.95f, 4.4f, 90f);
            Clutter(0, 7.2f, 0.8f, 4.9f, 1f, 43, "restaurant/food_dinner", "restaurant/bowl", "restaurant/ketchup", "restaurant/mustard");
            P("restaurant/crate_cheese", 0, 11.2f, 3.9f, -20f, 0.65f);
            P("dungeon/barrel_small", 0, 9.6f, 5.9f, 0f, 0.8f);
            P("restaurant/kitchencounter_straight_B", 0, 11.25f, 5.8f, 270f);
            Clutter(0, 11.25f, 0.95f, 5.8f, 0.4f, 45, "restaurant/pot_B", "restaurant/jar_D_medium");
            P("city/trash_B", 0, 4.45f, 7.5f, 0f, 0.9f);
        }

        static void DetailUpstairs() {
            // Master bedroom
            P("dungeon/trunk_large_B", 1, -5.6f, -6.3f, 0f);
            P("furniture/shelf_A_big", 1, -2.45f, -1.6f, 270f);
            P("furniture/table_small", 1, -8.2f, -3.2f);
            On("furniture/pictureframe_standing_B", 1, -8.2f, 0.75f, -3.2f, 30f);
            P("furniture/cactus_medium_B", 1, -2.6f, -7.4f);
            Clutter(1, -11.55f, 1.15f, -1.5f, 0.4f, 51, "furniture/book_single", "dungeon/candle");
            P("furniture/table_small", 1, -11.4f, -3.9f);
            P("furniture/chair_B", 1, -10.6f, -3.9f, 270f);
            On("furniture/pictureframe_standing_A", 1, -11.5f, 0.75f, -4.2f, 90f);
            P("furniture/armchair", 1, -10.2f, -2.2f, 120f);
            P("furniture/rug_oval_A", 1, -9.6f, -2.6f, 90f, 1f, solid: false);
            P("furniture/lamp_standing", 1, -11.4f, 0.5f, lit: true);

            // Balcony
            P("halloween/lantern_standing", 1, -3.5f, -10.5f, 0f, 0.9f, lit: true);
            P("halloween/pumpkin_yellow_small", 1, -9.5f, -9.7f, 40f);

            // Upstairs bathroom
            P("dungeon/barrel_small", 1, -1.45f, -0.25f, 0f, 0.8f);
            Clutter(1, 0f, 0.78f, -6.9f, 1.4f, 61, "dungeon/bottle_A_brown", "dungeon/bottle_B_green", "dungeon/candle_thin");

            // Kids' room
            P("dungeon/chest", 1, 5f, -7.45f, 0f);
            P("dungeon/box_small_decorated", 1, 6.1f, -7.4f, 20f);
            P("dungeon/box_stacked", 1, 4.4f, -0.4f, 10f, 0.55f);
            P("furniture/cactus_small_B", 1, 8.6f, -7.5f);
            Picture("pictureframe_small_A", 1, 7f, -7.86f, 0f, 1.7f);
            Picture("pictureframe_small_B", 1, 9f, -7.86f, 0f, 1.5f);
            Clutter(1, 7f, 0.75f, -6.8f, 0.5f, 71, "furniture/book_single", "dungeon/bottle_B_brown");
            P("furniture/lamp_standing", 1, 2.5f, -7.5f, lit: true);
            P("furniture/cabinet_small", 1, 9.6f, -7.5f, 0f);
            On("furniture/lamp_table", 1, 9.6f, 0.75f, -7.5f, 0f, 0.7f, lit: true);
            P("furniture/shelf_B_small_decorated", 1, 8f, 0.7f, 180f);
            P("furniture/table_small", 1, 8.6f, -3.4f);
            P("furniture/chair_stool_wood", 1, 8.6f, -2.6f);
            P("furniture/chair_stool_wood", 1, 9.5f, -3.4f);
            P("dungeon/box_small", 1, 5.6f, -3.0f, 25f, 0.7f);
            P("dungeon/box_small_decorated", 1, 6.2f, -2.2f, -10f, 0.7f);

            // Upstairs hall
            Picture("pictureframe_large_A", 1, -9.2f, 3.28f, 180f, 1.7f);
            Picture("pictureframe_medium", 1, 3f, 1.12f, 0f);
            P("halloween/bench", 1, 10f, 3.0f, 180f, 0.9f);
            P("furniture/shelf_B_large_decorated", 1, -10.5f, 3.1f, 180f);
            P("furniture/cabinet_medium_decorated", 1, -5.6f, 3.1f, 180f);
            P("dungeon/shelf_small", 1, 3.6f, 3.2f, 180f, solid: false, lift: 1.3f);
            P("furniture/cactus_medium_A", 1, -9f, 1.35f);
            P("furniture/shelf_A_small", 1, -3.2f, 1.25f, 0f);
            P("dungeon/chest", 1, 2.9f, 1.4f, 0f, 0.7f);
            P("furniture/cabinet_small_decorated", 1, 8.5f, 1.3f, 0f);
            P("furniture/cactus_medium_B", 1, 11.3f, 1.35f);
            P("furniture/rug_rectangle_A", 1, -4f, 2.2f, 0f, 1f, solid: false);

            // Guest room
            P("dungeon/trunk_medium_B", 1, -9.3f, 6.7f, 90f);
            P("furniture/lamp_standing", 1, -5.2f, 7.5f, lit: true);
            P("furniture/rug_oval_A", 1, -8f, 5.6f, 0f, 0.9f, solid: false);
            Clutter(1, -6.4f, 0.75f, 4.1f, 0.5f, 81, "furniture/book_single", "dungeon/candle_melted");
            P("furniture/armchair", 1, -6.2f, 6.2f, 200f);
            On("furniture/lamp_table", 1, -7.6f, 0.75f, 7.55f, 180f, 0.7f, lit: true);

            // Linen closet
            P("dungeon/box_stacked", 1, -3.3f, 6.2f, 5f, 0.55f);
            P("dungeon/box_large", 1, -0.5f, 7.3f, -10f);
            P("dungeon/barrel_small", 1, 1.5f, 7.4f, 0f, 0.8f);

            // Attic: old junk between the crate stacks
            P("dungeon/trunk_large_C", 1, 3.1f, 6.5f, 90f);
            P("dungeon/barrel_large_decorated", 1, 10.6f, 7.4f, 0f, 0.85f);
            P("dungeon/shelves", 1, 8.1f, 5.5f, 90f, 0.85f);
            Clutter(1, 6.5f, 0.75f, 6f, 0.6f, 91, "dungeon/candle_triple", "halloween/skull_candle");
            P("dungeon/candle_melted", 1, 5f, 4.3f, 0f, SmallDungeonScale, solid: false);
            P("halloween/lantern_hanging", 1, 7f, 5.5f, 0f, solid: false, lift: 2.2f, lit: true);
        }

        static void DetailOutbuildings() {
            // Garage: a parked car, drums, bins and boxes
            P("city/car_stationwagon", 0, 21f, -9.8f, 0f, 0.95f);
            P("dungeon/barrel_large", 0, 25.3f, -12.8f, 0f, 0.85f);
            P("dungeon/barrel_large", 0, 24.4f, -13.2f, 40f, 0.85f);
            P("dungeon/barrel_small", 0, 25.4f, -11.6f, 0f, 0.85f);
            P("city/box_A", 0, 17.2f, -6.2f, 10f);
            P("city/box_B", 0, 17.4f, -12.9f, -15f);
            P("city/trash_A", 0, 24.6f, -4.6f, 180f);
            Clutter(0, 25.3f, 0.95f, -8.2f, 0.8f, 101, "dungeon/bottle_A_brown", "restaurant/pan_B", "dungeon/candle");

            // Shed
            P("dungeon/barrel_small_stack", 0, -21f, 11.2f, 0f);
            P("dungeon/box_stacked", 0, -27f, 11.2f, 20f, 0.6f);
            Clutter(0, -27.4f, 0.95f, 12f, 1.6f, 111, "halloween/pumpkin_orange_small", "dungeon/bottle_C_green", "restaurant/pot_B");
        }

        static void DetailBackyard() {
            // Vegetable beds with the harvest still in them
            var random = new System.Random(131);
            string[] produce = { "halloween/pumpkin_orange_small", "halloween/pumpkin_yellow_small", "restaurant/crate_carrots", "restaurant/crate_lettuce" };
            foreach (var (x, z) in new[] { (-3.5f, 22f), (-3.5f, 24.2f), (-0.2f, 22f), (-0.2f, 24.2f) }) {
                Block("VegetableBed", 0, x, z, 2.6f, 0.45f, 1.3f, Surface.Planks);
                for (int i = 0; i < 3; i++)
                    P(produce[random.Next(produce.Length)], 0, x - 0.8f + i * 0.8f, z, random.Next(360), 0.6f, solid: false, lift: 0.45f);
            }

            // Hedge walls and shrubs that break up the open lawn
            Hedge(1f, 13.5f, 1f, 19.5f);
            Hedge(1f, 19.5f, 5f, 19.5f);
            Hedge(9.5f, 16.5f, 9.5f, 21f);
            for (float x = -10.5f; x <= -1.5f; x += 1.75f) P("city/bush", 0, x, 9.1f, x * 61f % 360f, 1.2f);
            for (float x = 2f; x <= 6f; x += 2f) P("city/bush", 0, x, 9.1f, x * 61f % 360f, 1.2f);

            // Front-west woods: crate stacks and shrubs among the trees
            P("dungeon/crates_stacked", 0, -27f, -20f, 20f);
            P("dungeon/crates_stacked", 0, -22f, -23.5f, -35f);
            P("dungeon/box_large", 0, -20f, -14.5f, 10f);
            foreach (var (x, z) in new[] { (-25f, -17f), (-19f, -21f), (-28f, -28f), (-21.5f, -27.5f), (14f, -18f), (13.8f, -24f), (14.2f, -27.5f) })
                P("city/bush", 0, x, z, x * z % 360f, 1.3f);

            // Behind the garage: a woodpile, drums and boxes
            Block("GarageWoodpile", 0, 20f, -1.6f, 3.2f, 1.2f, 1.1f, Surface.DarkPlanks);
            Block("GarageWoodpileTop", 0, 20f, -1.6f, 2.6f, 0.4f, 0.9f, Surface.DarkPlanks, bottom: 1.2f);
            P("dungeon/barrel_large", 0, 23.4f, 0.8f, 0f, 0.9f);
            P("dungeon/barrel_small_stack", 0, 17.6f, 1.6f, 30f);
            P("city/box_A", 0, 21.6f, 4.4f, 15f);
            P("city/box_B", 0, 22.4f, 4.9f, -20f);
            P("city/bush", 0, 18f, 6.5f, 40f, 1.3f);
        }

        static void DetailGrounds() {
            // Shrubs along the front of the house: low cover right under the windows
            for (float x = -10.5f; x <= -3.5f; x += 1.75f) P("city/bush", 0, x, -9.1f, x * 53f % 360f, 1.25f);
            for (float x = 3.5f; x <= 10.5f; x += 1.75f) P("city/bush", 0, x, -9.1f, x * 53f % 360f, 1.25f);
            for (float z = -6.5f; z <= 6.5f; z += 2.2f) P("city/bush", 0, 13.1f, z, z * 31f % 360f, 1.15f);
            for (float z = -6.5f; z <= 6.5f; z += 2.2f) P("city/bush", 0, -13.1f, z, z * 29f % 360f, 1.15f);

            // Bins and a skip between the house and the garage
            P("city/dumpster", 0, 14.3f, -10.5f, 90f);
            P("city/trash_A", 0, 14.1f, -7.6f, 90f);
            P("city/trash_B", 0, 14.4f, -13.4f, 90f);

            // A second car on the drive and a street lamp by the gate
            P("city/car_sedan", 0, 21f, -21f, 180f, 0.95f);
            P("city/streetlight", 0, 3f, -28.6f, 180f, lit: true);
            P("city/firehydrant", 0, -3f, -28.8f, 0f);

            // Picnic lawn and backyard
            P("city/bench", 0, 6f, 12f, 180f);
            P("dungeon/barrel_large", 0, 11.3f, 9.4f, 0f, 0.9f);
            P("dungeon/barrel_large_decorated", 0, 11.4f, 10.7f, 30f, 0.9f);
            Clutter(0, -3f, 0.78f, 16f, 1.2f, 121, "restaurant/plate", "restaurant/food_burger", "dungeon/bottle_A_labeled_brown", "halloween/candle_triple");

            // A woodpile by the shed and stacks of firewood boxes: cover in the west yard
            Block("Woodpile", 0, -21.5f, 19.2f, 3f, 1.2f, 1.1f, Surface.DarkPlanks);
            Block("WoodpileTop", 0, -21.5f, 19.2f, 2.4f, 0.4f, 0.9f, Surface.DarkPlanks, bottom: 1.2f);
            P("dungeon/crates_stacked", 0, -24.5f, 8.6f, 15f);
            P("dungeon/barrel_small_stack", 0, -18.6f, 9.4f, 0f);

            // Tree fort: barrels under the deck
            P("dungeon/barrel_large", 0, -25f, 23.4f, 0f, 0.8f);
            P("dungeon/keg", 0, -26.8f, 24.6f, 70f, 0.8f);

            // Pumpkin patch: hay-bale-sized boxes and a scarecrow post
            P("dungeon/box_large", 0, -12.6f, -18.2f, 25f);
            P("dungeon/box_large", 0, -6.6f, -22.6f, -15f);
            P("halloween/post_skull", 0, -9.5f, -20.5f, 30f, 1.2f);

            // Graveyard: broken graves, markers and bones among the rows
            (float x, float z, string model)[] graveyard = {
                (14f, 23.5f, "grave_A_destroyed"), (19f, 24.5f, "gravemarker_A"), (23f, 23.2f, "gravemarker_B"),
                (27f, 21.8f, "grave_A_destroyed"), (31.5f, 23.5f, "gravemarker_A"), (24.5f, 16.8f, "post_skull"),
                (13.5f, 16.5f, "gravemarker_B"), (30f, 27.5f, "grave_B"),
            };
            foreach (var g in graveyard) P("halloween/" + g.model, 0, g.x, g.z, 180f + g.x * 7f % 30f, 1.05f);
            foreach (var (x, z, bone) in new[] { (17f, 20.5f, "bone_A"), (21.5f, 18.6f, "ribcage"), (26f, 18.9f, "skull"), (28.6f, 25.6f, "bone_C") })
                P("halloween/" + bone, 0, x, z, x * 41f % 360f, 1f, solid: false);
            P("halloween/skull_candle", 0, 18.4f, 26.1f, 180f, solid: false);
            P("halloween/candle_triple", 0, 20.1f, 26.1f, 0f, solid: false);
        }
    }
}
