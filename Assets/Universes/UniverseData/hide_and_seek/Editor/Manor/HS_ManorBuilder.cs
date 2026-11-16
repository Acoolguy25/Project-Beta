using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Universes.UniverseData.hide_and_seek.Editor.Manor {
    /// <summary>
    /// Builds <see cref="HS_ManorPlan"/> into the open scene. Everything is built in plan metres under
    /// one root that is then scaled up to the game's character size, so the plan reads like a real
    /// floor plan. Walls are split around their doors and windows, exterior walls get brick outside and
    /// plaster inside, rooms get floors and ceilings, props get simple box colliders, and lit props
    /// (lamps, candles, lanterns) and fill-lit rooms get shadowless point lights (see <see cref="HS_ManorLighting"/>).
    /// </summary>
    public sealed class HS_ManorBuilder {
        readonly HS_ManorMaterials materials;
        readonly Transform root;
        readonly float scale;
        Transform structure, glazing, furniture, lighting, garden;
        readonly List<Light> flickerLights = new();
        readonly List<Renderer> flickerGlows = new();
        readonly List<Renderer> glows = new();

        /// <param name="scale">World units per plan metre.</param>
        public HS_ManorBuilder(HS_ManorMaterials materials, float scale, string name = "Manor") {
            this.materials = materials;
            this.scale = scale;
            root = new GameObject(name).transform;
        }

        public Transform Root => root;
        /// <summary>Candle and lantern lights, for <see cref="HS_Map.flickerLights"/> (valid after <see cref="Build"/>).</summary>
        public IReadOnlyList<Light> FlickerLights => flickerLights;
        /// <summary>The glow sprite of each flicker light, in the same order (valid after <see cref="Build"/>).</summary>
        public IReadOnlyList<Renderer> FlickerGlows => flickerGlows;
        /// <summary>Every light the manor got (valid after <see cref="Build"/>).</summary>
        public int LightCount => lighting.childCount;

        public Transform Build() {
            structure = Group("Structure");
            glazing = Group("Windows");
            furniture = Group("Furniture");
            lighting = Group("Lights");
            garden = Group("Garden");
            BuildGround();
            foreach (var room in HS_ManorPlan.Rooms) BuildRoom(room);
            foreach (var wall in HS_ManorPlan.Walls) BuildWall(wall);
            BuildStairs();
            BuildHouseRoof();
            BuildOutbuildingRoofs();
            BuildFence();
            foreach (var block in HS_ManorPlan.Blocks) Box(block.name, furniture, block.center, block.size, block.surface);
            foreach (var prop in HS_ManorPlan.Props) PlaceProp(prop);
            root.localScale = Vector3.one * scale;
            foreach (var light in lighting.GetComponentsInChildren<Light>()) light.range *= scale;
            return root;
        }

        Transform Group(string name) {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        // ---------------------------------------------------------------- primitives
        /// <summary>A textured box in plan metres (centre and size), with a collider unless told otherwise.</summary>
        GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Surface surface, bool collider = true, bool shadows = true) {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = materials.For(surface, size);
            if (!shadows) renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        /// <summary>
        /// A flat <see cref="Box"/> cut into equal tiles no longer than <paramref name="maxTile"/>
        /// along x and z, so that no single renderer spans more lamps than URP's per-object light limit
        /// (see <see cref="CheckLightReach"/>). One tile keeps the plain name.
        /// </summary>
        Transform Tiled(string name, Transform parent, Vector3 center, Vector3 size, Surface surface, bool collider = true, bool shadows = true, float maxTile = HS_ManorLighting.MaxTile) {
            int nx = Mathf.Max(1, Mathf.CeilToInt(size.x / maxTile - 0.01f));
            int nz = Mathf.Max(1, Mathf.CeilToInt(size.z / maxTile - 0.01f));
            if (nx * nz == 1) return Box(name, parent, center, size, surface, collider, shadows).transform;
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.localPosition = center;
            Vector3 tile = new(size.x / nx, size.y, size.z / nz);
            for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
                Box($"{name}_{ix}_{iz}", group, new Vector3(tile.x * (ix + 0.5f - nx * 0.5f), 0f, tile.z * (iz + 0.5f - nz * 0.5f)), tile, surface, collider, shadows);
            return group;
        }

        static float FloorY(int level) => level * HS_ManorPlan.StoreyHeight;

        // ---------------------------------------------------------------- ground and rooms
        void BuildGround() {
            Rect plot = HS_ManorPlan.Plot;
            const float margin = 40f;
            Rect house = HS_ManorPlan.House;
            // The lawn stops at the house (whose ground floor has its own slab), so the lamps inside
            // do not count against the grass's per-object light limit.
            Rect lawn = Rect.MinMaxRect(plot.xMin - margin, plot.yMin - margin, plot.xMax + margin, plot.yMax + margin);
            LawnPart("LawnSouth", Rect.MinMaxRect(lawn.xMin, lawn.yMin, lawn.xMax, house.yMin));
            LawnPart("LawnNorth", Rect.MinMaxRect(lawn.xMin, house.yMax, lawn.xMax, lawn.yMax));
            LawnPart("LawnWest", Rect.MinMaxRect(lawn.xMin, house.yMin, house.xMin, house.yMax));
            LawnPart("LawnEast", Rect.MinMaxRect(house.xMax, house.yMin, lawn.xMax, house.yMax));
            // Front path from the gate to the door, a drive to the garage, and a back path to the lawn.
            Strip("FrontPath", Surface.Path, Rect.MinMaxRect(-1.3f, plot.yMin, 1.3f, house.yMin));
            Strip("Driveway", Surface.Driveway, Rect.MinMaxRect(18.2f, plot.yMin, 23.8f, -14f));
            Strip("BackPath", Surface.Path, Rect.MinMaxRect(8.8f, house.yMax, 11.2f, 14f));
            Strip("Patio", Surface.Path, Rect.MinMaxRect(-7f, 12f, 1f, 20f));
        }

        void LawnPart(string name, Rect area) =>
            Tiled(name, garden, new Vector3(area.center.x, -0.5f, area.center.y), new Vector3(area.width, 1f, area.height), Surface.Grass, maxTile: HS_ManorLighting.MaxLawnTile);

        void Strip(string name, Surface surface, Rect area) =>
            Box(name, garden, new Vector3(area.center.x, 0.01f, area.center.y), new Vector3(area.width, 0.04f, area.height), surface);

        void BuildRoom(RoomSpec room) {
            var group = new GameObject(room.name).transform;
            group.SetParent(structure, false);
            Rect a = room.area;
            float floor = FloorY(room.level) + (room.level == 0 ? 0.04f : 0f);
            Tiled("Floor", group, new Vector3(a.center.x, floor - HS_ManorPlan.SlabThickness * 0.5f, a.center.y),
                new Vector3(a.width, HS_ManorPlan.SlabThickness, a.height), room.floor);
            if (!room.indoor) return;
            bool openAbove = room.name == "Stairs";
            float wallTop = WallHeight(room) + FloorY(room.level);
            float ceiling = room.level == 0 && !IsOutbuilding(room) ? FloorY(1) - HS_ManorPlan.SlabThickness : wallTop;
            if (!openAbove)
                Tiled("Ceiling", group, new Vector3(a.center.x, ceiling - 0.02f, a.center.y), new Vector3(a.width, 0.04f, a.height), Surface.Plaster, collider: room.level > 0 || IsOutbuilding(room), shadows: false);
            if (room.fill > 0f) AddFill(room, ceiling);
        }

        static bool IsOutbuilding(RoomSpec room) => !HS_ManorPlan.House.Overlaps(room.area);

        static float WallHeight(RoomSpec room) => IsOutbuilding(room) ? room.name.Contains("Shed") ? 2.7f : 3.3f : HS_ManorPlan.StoreyHeight;

        /// <summary>A dim ceiling light for a room that has no lamp of its own.</summary>
        void AddFill(RoomSpec room, float ceiling) {
            float reach = Mathf.Max(room.area.width, room.area.height) * HS_ManorLighting.FillRangeFactor;
            PointLight(room.name + " Fill", new Vector3(room.area.center.x, ceiling - HS_ManorLighting.FillDrop, room.area.center.y),
                HS_ManorLighting.FillColor, Mathf.Clamp(reach, HS_ManorLighting.FillRange.x, HS_ManorLighting.FillRange.y),
                HS_ManorLighting.FillIntensity * room.fill);
        }

        /// <summary>A shadowless point light in plan metres (its range is scaled with the map at the end of <see cref="Build"/>).</summary>
        Light PointLight(string name, Vector3 at, Color color, float range, float intensity) {
            var go = new GameObject(name, typeof(Light));
            go.transform.SetParent(lighting, false);
            go.transform.localPosition = at;
            var light = go.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return light;
        }

        /// <summary>The glow of a lamp, candle or lantern, placed inside the model.</summary>
        void AddPropLight(PropSpec spec, GameObject model, Transform parent) {
            PropLight preset = HS_ManorLighting.PropLightFor(spec.model);
            Bounds b = LocalBounds(model.transform, parent);
            Vector3 at = new(b.center.x, b.min.y + b.size.y * preset.height, b.center.z);
            // Props parented to the garden or fence groups share the root's plan space with the lights.
            at = lighting.InverseTransformPoint(parent.TransformPoint(at));
            string name = spec.model[(spec.model.IndexOf('/') + 1)..];
            Light light = PointLight(name + " Light", at, preset.color, preset.range, preset.intensity);
            // Pull the glow out past the model's own radius, so the shade, glass or pumpkin skin does not cover it.
            float pull = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) * HS_ManorLighting.GlowPullFactor, HS_ManorLighting.GlowPull.x, HS_ManorLighting.GlowPull.y);
            Renderer glow = AddGlow(name + " Glow", light.transform, preset, pull);
            if (preset.flicker) { flickerLights.Add(light); flickerGlows.Add(glow); }
        }

        /// <summary>
        /// A soft additive HDR sprite at the light that always faces the camera: the shade, flame or
        /// pumpkin face reads as lit, and bloom gives it a halo. The sprite sits <paramref name="pull"/>
        /// towards the camera from the light (a turning pivot holds it there), so the lamp's own shade or
        /// glass does not hide it; it is still drawn with depth, so walls do. Up close it is capped to a
        /// share of the screen, so a lamp next to the camera does not turn into a white ball.
        /// </summary>
        Renderer AddGlow(string name, Transform light, PropLight preset, float pull) {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(light, false);
            // Look at the camera (not the camera plane), so the pull runs along the line of sight
            // and the sprite stays over the lamp anywhere on screen.
            var turn = pivot.gameObject.AddComponent<RyanAssets.Rounds.Billboard>();
            turn.facing = RyanAssets.Rounds.Billboard.Facing.LookAtCamera;
            turn.yawOnly = false;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Sprite";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(pivot, false);
            go.transform.localPosition = new Vector3(0f, 0f, -pull);
            go.transform.localScale = Vector3.one * preset.glow;
            // The screen cap sits on the sprite itself, so shrinking it never shortens the pull.
            var cap = go.AddComponent<RyanAssets.Rounds.Billboard>();
            cap.facing = RyanAssets.Rounds.Billboard.Facing.LookAtCamera;
            cap.yawOnly = false;
            cap.maxScreenHeight = HS_ManorLighting.GlowMaxScreenHeight;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = materials.Glow(preset.color, HS_ManorLighting.GlowIntensity);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            glows.Add(renderer);
            return renderer;
        }

        /// <summary>
        /// The most point lights whose range reaches any one renderer of the manor, and which renderer
        /// that is. URP's forward renderer drops lights past its per-object limit, which shows as lamp
        /// pools popping in and out as the camera moves, so the authoring script fails above
        /// <see cref="HS_ManorLighting.MaxLightsPerObject"/>.
        /// </summary>
        public (int count, string where) CheckLightReach() {
            var lights = lighting.GetComponentsInChildren<Light>();
            int most = 0;
            string where = "";
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>()) {
                if (glows.Contains(renderer)) continue;
                Bounds b = renderer.bounds;
                int n = 0;
                foreach (var light in lights)
                    if (b.SqrDistance(light.transform.position) <= light.range * light.range) n++;
                if (n > most) { most = n; where = renderer.transform.parent != null ? $"{renderer.transform.parent.name}/{renderer.name}" : renderer.name; }
            }
            return (most, where);
        }

        // ---------------------------------------------------------------- walls
        void BuildWall(WallSpec wall) {
            (float thickness, float height) = wall.kind switch {
                WallKind.Exterior => (HS_ManorPlan.ExteriorThickness, HS_ManorPlan.StoreyHeight * 2f),
                WallKind.Interior => (HS_ManorPlan.InteriorThickness, HS_ManorPlan.StoreyHeight),
                WallKind.Railing => (0.1f, HS_ManorPlan.RailingHeight),
                WallKind.Shed => (0.16f, 2.7f),
                WallKind.Garage => (0.25f, 3.3f),
                _ => (0.2f, HS_ManorPlan.StoreyHeight),
            };
            float baseY = FloorY(wall.level);
            Vector2 dir = (wall.b - wall.a).normalized;
            float length = wall.Length;
            var group = new GameObject($"{wall.kind}Wall").transform;
            group.SetParent(structure, false);

            // Split the wall into vertical strips at every opening edge, then fill each strip
            // around the openings that cross it.
            var cuts = new List<float> { 0f, length };
            foreach (var o in wall.openings) { cuts.Add(Mathf.Clamp(o.at - o.width * 0.5f, 0f, length)); cuts.Add(Mathf.Clamp(o.at + o.width * 0.5f, 0f, length)); }
            cuts = cuts.Distinct().OrderBy(c => c).ToList();
            for (int i = 0; i < cuts.Count - 1; i++) {
                float u0 = cuts[i], u1 = cuts[i + 1];
                if (u1 - u0 < 0.001f) continue;
                var holes = wall.openings.Where(o => o.at - o.width * 0.5f <= u0 + 0.001f && o.at + o.width * 0.5f >= u1 - 0.001f)
                    .Select(o => (o.bottom, top: o.bottom + o.height)).OrderBy(h => h.bottom).ToList();
                float y = 0f;
                foreach (var (bottom, top) in holes) {
                    if (bottom > y) WallPiece(wall, group, dir, u0, u1, y, bottom, thickness, baseY);
                    y = Mathf.Max(y, top);
                }
                if (y < height) WallPiece(wall, group, dir, u0, u1, y, height, thickness, baseY);
            }
            foreach (var o in wall.openings) {
                if (o.glazed) Window(wall, group, dir, o, thickness, baseY);
            }
        }

        /// <summary>One solid piece of wall between two points along it and two heights.</summary>
        void WallPiece(WallSpec wall, Transform group, Vector2 dir, float u0, float u1, float y0, float y1, float thickness, float baseY) {
            Vector2 mid = wall.a + dir * ((u0 + u1) * 0.5f);
            float length = u1 - u0;
            bool alongX = Mathf.Abs(dir.x) > 0.5f;
            Vector3 center = new(mid.x, baseY + (y0 + y1) * 0.5f, mid.y);
            float h = y1 - y0;
            switch (wall.kind) {
                case WallKind.Exterior: {
                    // Brick skin outside, plaster skin inside.
                    Vector2 outward = Outward(wall, dir);
                    float brick = thickness * 0.6f, plaster = thickness - brick;
                    Vector3 o = new(outward.x, 0f, outward.y);
                    Box("Brick", group, center + o * (thickness * 0.5f - brick * 0.5f), Size(alongX, length, h, brick), Surface.Brick);
                    Box("Plaster", group, center - o * (thickness * 0.5f - plaster * 0.5f), Size(alongX, length, h, plaster), Surface.Plaster);
                    break;
                }
                case WallKind.Garage: Box("Brick", group, center, Size(alongX, length, h, thickness), Surface.Brick); break;
                case WallKind.Shed: Box("Planks", group, center, Size(alongX, length, h, thickness), Surface.Planks); break;
                case WallKind.Railing: Box("Railing", group, center, Size(alongX, length, h, thickness), Surface.Trim); break;
                default: Box("Plaster", group, center, Size(alongX, length, h, thickness), Surface.Plaster); break;
            }
        }

        static Vector3 Size(bool alongX, float length, float height, float thickness) =>
            alongX ? new Vector3(length, height, thickness) : new Vector3(thickness, height, length);

        /// <summary>The side of an exterior wall facing away from the house.</summary>
        static Vector2 Outward(WallSpec wall, Vector2 dir) {
            Vector2 normal = new(-dir.y, dir.x);
            Vector2 mid = (wall.a + wall.b) * 0.5f;
            return Vector2.Dot(normal, mid - HS_ManorPlan.House.center) >= 0f ? normal : -normal;
        }

        void Window(WallSpec wall, Transform group, Vector2 dir, Opening o, float thickness, float baseY) {
            Vector2 at = wall.a + dir * o.at;
            bool alongX = Mathf.Abs(dir.x) > 0.5f;
            Vector3 center = new(at.x, baseY + o.bottom + o.height * 0.5f, at.y);
            Box("Glass", glazing, center, Size(alongX, o.width, o.height, 0.04f), Surface.Glass, shadows: false);
            Box("Sill", group, new Vector3(at.x, baseY + o.bottom - 0.03f, at.y), Size(alongX, o.width + 0.2f, 0.06f, thickness + 0.14f), Surface.Trim);
            Box("Lintel", group, new Vector3(at.x, baseY + o.bottom + o.height + 0.04f, at.y), Size(alongX, o.width + 0.2f, 0.08f, thickness + 0.06f), Surface.Trim, collider: false);
            // Mullion: the cross bars make the glass read as a window from far away.
            Box("Mullion", glazing, center, Size(alongX, 0.06f, o.height, 0.08f), Surface.Trim, collider: false, shadows: false);
            Box("Transom", glazing, center, Size(alongX, o.width, 0.06f, 0.08f), Surface.Trim, collider: false, shadows: false);
        }

        // ---------------------------------------------------------------- stairs
        void BuildStairs() {
            Rect well = HS_ManorPlan.StairWell;
            int steps = HS_ManorPlan.StairSteps;
            float rise = HS_ManorPlan.StoreyHeight / steps, run = well.width / steps;
            var group = new GameObject("Staircase").transform;
            group.SetParent(structure, false);
            float z = well.center.y + 0.04f, width = well.height - 0.25f;
            for (int i = 0; i < steps; i++) {
                float top = rise * (i + 1);
                // Steps are for the eye; the ramp below is what players walk on, so tall characters
                // never snag on a riser.
                Box($"Step{i + 1}", group, new Vector3(well.xMin + run * (i + 0.5f), top * 0.5f, z), new Vector3(run, top, width), Surface.Laminate, collider: false);
            }
            float length = Mathf.Sqrt(well.width * well.width + HS_ManorPlan.StoreyHeight * HS_ManorPlan.StoreyHeight);
            var ramp = new GameObject("StairRamp", typeof(BoxCollider)).transform;
            ramp.SetParent(group, false);
            ramp.localPosition = new Vector3(well.center.x, HS_ManorPlan.StoreyHeight * 0.5f - 0.05f, z);
            ramp.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(HS_ManorPlan.StoreyHeight, well.width) * Mathf.Rad2Deg);
            ramp.localScale = new Vector3(length, 0.1f, width);
            // Handrail on the open (hall) side.
            Box("Handrail", group, new Vector3(well.center.x, HS_ManorPlan.StoreyHeight * 0.5f + 0.9f, well.yMin + 0.05f), new Vector3(length, 0.08f, 0.08f), Surface.Trim, collider: false)
                .transform.localRotation = ramp.localRotation;
        }

        // ---------------------------------------------------------------- roofs
        void BuildHouseRoof() {
            Rect house = HS_ManorPlan.House;
            var group = new GameObject("Roof").transform;
            group.SetParent(structure, false);
            float wallTop = HS_ManorPlan.StoreyHeight * 2f;
            float pitch = HS_ManorPlan.RoofPitch, tan = Mathf.Tan(pitch * Mathf.Deg2Rad);
            float halfDepth = house.height * 0.5f, overhang = HS_ManorPlan.RoofOverhang;
            float slope = (halfDepth + overhang) / Mathf.Cos(pitch * Mathf.Deg2Rad);
            float width = house.width + overhang * 2f, thickness = 0.25f;
            float ridgeY = wallTop + halfDepth * tan;
            foreach (int side in new[] { -1, 1 }) {
                // Each slope passes through the top of its eave wall and meets the other at the ridge.
                float zLow = house.center.y + side * (halfDepth + overhang), yLow = wallTop - overhang * tan;
                Vector3 mid = new(house.center.x, (yLow + ridgeY) * 0.5f, (zLow + house.center.y) * 0.5f);
                var rotation = Quaternion.Euler(side * pitch, 0f, 0f);
                mid += rotation * Vector3.up * (thickness * 0.5f);
                var plane = new GameObject(side < 0 ? "SouthSlope" : "NorthSlope").transform;
                plane.SetParent(group, false);
                plane.localPosition = mid;
                plane.localRotation = rotation;
                Tiled("Tiles", plane, Vector3.zero, new Vector3(width, thickness, slope), Surface.Roof);
            }
            Box("Ridge", group, new Vector3(house.center.x, ridgeY + thickness, house.center.y), new Vector3(width, 0.2f, 0.3f), Surface.Trim, collider: false);
            // Gable ends: brick triangles filling the end walls up to the roof line.
            Mesh prism = GablePrism();
            foreach (float x in new[] { house.xMin, house.xMax }) {
                var gable = Box("Gable", group, new Vector3(x, wallTop, house.center.y), new Vector3(HS_ManorPlan.ExteriorThickness, halfDepth * tan, house.height), Surface.Brick, collider: false);
                gable.GetComponent<MeshFilter>().sharedMesh = prism;
                gable.GetComponent<MeshRenderer>().sharedMaterial = materials.For(Surface.Brick, new Vector2(house.height, halfDepth * tan));
            }
            // A chimney stack on the west slope.
            Box("Chimney", group, new Vector3(-7f, ridgeY - 0.4f, 2.4f), new Vector3(1.2f, 4.4f, 1.2f), Surface.Brick);
        }

        const string PrismPath = HS_Authoring.Root + "/Materials/Manor/GablePrism.asset";

        /// <summary>
        /// A unit triangular prism (base 1 wide along z, apex 1 up, 1 thick along x, origin at the base
        /// centre), saved once as an asset so gable walls can be plain scaled objects.
        /// </summary>
        static Mesh GablePrism() {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(PrismPath);
            if (mesh != null) return mesh;
            Vector3 l0 = new(-0.5f, 0f, -0.5f), l1 = new(-0.5f, 0f, 0.5f), lt = new(-0.5f, 1f, 0f);
            Vector3 r0 = new(0.5f, 0f, -0.5f), r1 = new(0.5f, 0f, 0.5f), rt = new(0.5f, 1f, 0f);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            void Face(Vector3[] corners, Vector2[] uv) {
                int start = vertices.Count;
                vertices.AddRange(corners);
                uvs.AddRange(uv);
                for (int i = 1; i < corners.Length - 1; i++) triangles.AddRange(new[] { start, start + i, start + i + 1 });
            }
            Vector2[] tri = { new(0f, 0f), new(0.5f, 1f), new(1f, 0f) };
            Face(new[] { l0, lt, l1 }, tri);                       // west face
            Face(new[] { r1, rt, r0 }, tri);                       // east face
            Vector2[] quad = { new(0f, 0f), new(0f, 1f), new(1f, 1f), new(1f, 0f) };
            Face(new[] { l0, r0, rt, lt }, quad);                  // south slope
            Face(new[] { r1, l1, lt, rt }, quad);                  // north slope
            Face(new[] { l0, l1, r1, r0 }, quad);                  // base
            mesh = new Mesh { name = "GablePrism" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, PrismPath);
            return mesh;
        }

        void BuildOutbuildingRoofs() {
            foreach (var room in HS_ManorPlan.Rooms.Where(r => r.indoor && IsOutbuilding(r))) {
                float top = WallHeight(room);
                Rect a = room.area;
                Box(room.name + " Roof", structure, new Vector3(a.center.x, top + 0.15f, a.center.y), new Vector3(a.width + 0.8f, 0.3f, a.height + 0.8f), Surface.Roof);
            }
        }

        // ---------------------------------------------------------------- fence and boundary
        void BuildFence() {
            Rect plot = HS_ManorPlan.Plot;
            var group = new GameObject("Fence").transform;
            group.SetParent(garden, false);
            const float panel = 3f;   // fence model length in plan metres
            void Run(Vector2 from, Vector2 to, float yaw, float gateAt = float.NaN) {
                float length = Vector2.Distance(from, to);
                Vector2 dir = (to - from) / length;
                int count = Mathf.RoundToInt(length / panel);
                for (int i = 0; i < count; i++) {
                    Vector2 p = from + dir * (panel * (i + 0.5f));
                    bool gate = !float.IsNaN(gateAt) && Mathf.Abs(panel * (i + 0.5f) - gateAt) < panel * 0.5f;
                    PlaceProp(new PropSpec(gate ? "halloween/fence_gate" : "halloween/fence", new Vector3(p.x, 0f, p.y), yaw, 1f, !gate), group);
                    PlaceProp(new PropSpec("halloween/fence_pillar", new Vector3(p.x - dir.x * panel * 0.5f, 0f, p.y - dir.y * panel * 0.5f), yaw, 1f, true), group);
                }
            }
            Run(new Vector2(plot.xMin, plot.yMin), new Vector2(plot.xMax, plot.yMin), 0f, plot.width * 0.5f);
            Run(new Vector2(plot.xMin, plot.yMax), new Vector2(plot.xMax, plot.yMax), 180f);
            Run(new Vector2(plot.xMin, plot.yMin), new Vector2(plot.xMin, plot.yMax), 90f);
            Run(new Vector2(plot.xMax, plot.yMin), new Vector2(plot.xMax, plot.yMax), 270f);
            // Tall invisible walls stop anyone climbing or mantling out of the plot.
            const float height = 30f, t = 1f;
            foreach (var (c, s) in new[] {
                (new Vector3(plot.center.x, height * 0.5f, plot.yMin - t), new Vector3(plot.width + 4f, height, t)),
                (new Vector3(plot.center.x, height * 0.5f, plot.yMax + t), new Vector3(plot.width + 4f, height, t)),
                (new Vector3(plot.xMin - t, height * 0.5f, plot.center.y), new Vector3(t, height, plot.height + 4f)),
                (new Vector3(plot.xMax + t, height * 0.5f, plot.center.y), new Vector3(t, height, plot.height + 4f)),
            }) {
                var wall = new GameObject("Boundary", typeof(BoxCollider)).transform;
                wall.SetParent(garden, false);
                wall.localPosition = c;
                wall.localScale = s;
            }
            // A ring of trees outside the fence, so the plot sits in a wood instead of a void.
            var random = new System.Random(11);
            string[] trees = { "tree_pine_orange_large", "tree_pine_yellow_large", "tree_pine_orange_medium", "tree_pine_yellow_medium" };
            for (int i = 0; i < 90; i++) {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector2 edge = new(Mathf.Cos(angle), Mathf.Sin(angle));
                float k = 1f / Mathf.Max(Mathf.Abs(edge.x) / (plot.width * 0.5f), Mathf.Abs(edge.y) / (plot.height * 0.5f));
                Vector2 p = plot.center + edge * (k + 4f + (float)random.NextDouble() * 16f);
                PlaceProp(new PropSpec("halloween/" + trees[random.Next(trees.Length)], new Vector3(p.x, 0f, p.y), random.Next(360), 1.5f + (float)random.NextDouble() * 0.8f, false), group);
            }
        }

        // ---------------------------------------------------------------- props
        void PlaceProp(PropSpec spec) => PlaceProp(spec, spec.model.StartsWith("halloween/tree") || spec.model.StartsWith("halloween/pumpkin") ? garden : furniture);

        void PlaceProp(PropSpec spec, Transform parent) {
            var model = HS_PropCatalog.Load(spec.model);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            go.transform.localRotation = Quaternion.Euler(0f, spec.yaw, 0f) * go.transform.localRotation;
            go.transform.localScale *= HS_PropCatalog.PackScale(spec.model) * spec.scale;
            go.transform.localPosition = spec.at;
            // Seat the model on its plan point: base on the floor, footprint centred.
            Bounds local = LocalBounds(go.transform, parent);
            go.transform.localPosition += spec.at - new Vector3(local.center.x, local.min.y, local.center.z);
            if (spec.solid) AddBoxCollider(go);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            if (spec.lit) AddPropLight(spec, go, parent);
        }

        /// <summary>Bounds of a model's meshes in <paramref name="space"/>'s local coordinates.</summary>
        static Bounds LocalBounds(Transform model, Transform space) {
            bool any = false;
            var bounds = new Bounds();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>()) {
                if (filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++) {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = space.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }

        static void AddBoxCollider(GameObject go) {
            Bounds b = LocalBounds(go.transform, go.transform);
            var box = go.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = b.size;
        }

        // ---------------------------------------------------------------- map data
        /// <summary>World-space areas for the HUD's "where am I" label: rooms first, then the yards.</summary>
        public HS_MapArea[] Areas() {
            var areas = new List<HS_MapArea>();
            foreach (var room in HS_ManorPlan.Rooms.Concat(HS_ManorPlan.Yards)) {
                float y0 = FloorY(room.level) - (room.level == 0 ? 5f : 0.3f);
                float y1 = room.level == 0 && !room.indoor ? 40f : FloorY(room.level) + HS_ManorPlan.StoreyHeight - 0.3f;
                var b = new Bounds();
                b.SetMinMax(new Vector3(room.area.xMin, y0, room.area.yMin) * scale, new Vector3(room.area.xMax, y1, room.area.yMax) * scale);
                areas.Add(new HS_MapArea { name = room.name, bounds = b, indoor = room.indoor });
            }
            return areas.ToArray();
        }
    }
}
