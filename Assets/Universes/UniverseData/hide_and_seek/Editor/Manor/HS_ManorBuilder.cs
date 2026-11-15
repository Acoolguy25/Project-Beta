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
    /// plaster inside, rooms get floors, ceilings and a lamp, and props get simple box colliders.
    /// </summary>
    public sealed class HS_ManorBuilder {
        readonly HS_ManorMaterials materials;
        readonly Transform root;
        readonly float scale;
        Transform structure, glazing, furniture, lighting, garden;

        /// <param name="scale">World units per plan metre.</param>
        public HS_ManorBuilder(HS_ManorMaterials materials, float scale, string name = "Manor") {
            this.materials = materials;
            this.scale = scale;
            root = new GameObject(name).transform;
        }

        public Transform Root => root;

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

        static float FloorY(int level) => level * HS_ManorPlan.StoreyHeight;

        // ---------------------------------------------------------------- ground and rooms
        void BuildGround() {
            Rect plot = HS_ManorPlan.Plot;
            const float margin = 40f;
            Box("Lawn", garden, new Vector3(plot.center.x, -0.5f, plot.center.y), new Vector3(plot.width + margin * 2f, 1f, plot.height + margin * 2f), Surface.Grass);
            Rect house = HS_ManorPlan.House;
            // Front path from the gate to the door, a drive to the garage, and a back path to the lawn.
            Strip("FrontPath", Surface.Path, Rect.MinMaxRect(-1.3f, plot.yMin, 1.3f, house.yMin));
            Strip("Driveway", Surface.Driveway, Rect.MinMaxRect(18.2f, plot.yMin, 23.8f, -14f));
            Strip("BackPath", Surface.Path, Rect.MinMaxRect(8.8f, house.yMax, 11.2f, 14f));
            Strip("Patio", Surface.Path, Rect.MinMaxRect(-7f, 12f, 1f, 20f));
        }

        void Strip(string name, Surface surface, Rect area) =>
            Box(name, garden, new Vector3(area.center.x, 0.01f, area.center.y), new Vector3(area.width, 0.04f, area.height), surface);

        void BuildRoom(RoomSpec room) {
            var group = new GameObject(room.name).transform;
            group.SetParent(structure, false);
            Rect a = room.area;
            float floor = FloorY(room.level) + (room.level == 0 ? 0.04f : 0f);
            Box("Floor", group, new Vector3(a.center.x, floor - HS_ManorPlan.SlabThickness * 0.5f, a.center.y),
                new Vector3(a.width, HS_ManorPlan.SlabThickness, a.height), room.floor);
            if (!room.indoor) return;
            bool openAbove = room.name == "Stairs";
            float wallTop = WallHeight(room) + FloorY(room.level);
            float ceiling = room.level == 0 && !IsOutbuilding(room) ? FloorY(1) - HS_ManorPlan.SlabThickness : wallTop;
            if (!openAbove)
                Box("Ceiling", group, new Vector3(a.center.x, ceiling - 0.02f, a.center.y), new Vector3(a.width, 0.04f, a.height), Surface.Plaster, collider: room.level > 0 || IsOutbuilding(room), shadows: false);
            AddLight(room, ceiling);
        }

        static bool IsOutbuilding(RoomSpec room) => !HS_ManorPlan.House.Overlaps(room.area);

        static float WallHeight(RoomSpec room) => IsOutbuilding(room) ? room.name.Contains("Shed") ? 2.7f : 3.3f : HS_ManorPlan.StoreyHeight;

        void AddLight(RoomSpec room, float ceiling) {
            var go = new GameObject(room.name + " Light", typeof(Light));
            go.transform.SetParent(lighting, false);
            go.transform.localPosition = new Vector3(room.area.center.x, ceiling - 0.6f, room.area.center.y);
            var light = go.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.87f, 0.7f);
            float reach = Mathf.Max(room.area.width, room.area.height);
            light.range = Mathf.Clamp(reach * 0.85f, 4f, 11f);
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
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
                var plane = Box(side < 0 ? "SouthSlope" : "NorthSlope", group, mid, new Vector3(width, thickness, slope), Surface.Roof);
                plane.transform.localRotation = rotation;
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
