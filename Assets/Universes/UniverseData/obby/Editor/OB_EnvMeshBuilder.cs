#if !UNITY_SERVER
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Universes.UniverseData.obby.Client;

namespace Universes.UniverseData.obby.Editor {
    /// <summary>
    /// Builds the chunky, flat-shaded low-poly meshes of the obby's world (see OB_Environment.shader):
    /// every face gets its own vertices and normal, colour alpha marks emissive parts, and
    /// UV0.x carries the sway weight. Shapes are added through a transform stack, so props compose from
    /// boxes, prisms, lathed rings, rocks, spheres and tori. Faces are wound outwards from a reference
    /// point inside each solid, so no shape can come out inside-out.
    /// </summary>
    public sealed class OB_EnvMeshBuilder {
        readonly List<Vector3> positions = new();
        readonly List<Vector3> normals = new();
        readonly List<Color32> colors = new();
        readonly List<Vector2> uvs = new();
        readonly List<int> indices = new();
        readonly List<Matrix4x4> stack = new();
        Matrix4x4 matrix = Matrix4x4.identity;

        /// <summary>Sway weight (UV0.x) of the vertices added next, unless <see cref="SwayOf"/> is set.</summary>
        public float Sway { get; set; }
        /// <summary>Optional sway weight from a vertex's position in the current shape's local space.</summary>
        public System.Func<Vector3, float> SwayOf { get; set; }
        public int VertexCount => positions.Count;

        // ---------------------------------------------------------------- transform stack
        public void Push(Vector3 position, Quaternion rotation, Vector3 scale) {
            stack.Add(matrix);
            matrix *= Matrix4x4.TRS(position, rotation, scale);
        }

        public void Push(Vector3 position, Quaternion rotation) => Push(position, rotation, Vector3.one);

        public void Push(Vector3 position) => Push(position, Quaternion.identity, Vector3.one);

        public void Pop() {
            matrix = stack[^1];
            stack.RemoveAt(stack.Count - 1);
        }

        // ---------------------------------------------------------------- triangles
        /// <summary>Adds a triangle facing away from <paramref name="inside"/> (all points in local space).</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color color, Vector3 inside) {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(normal, (a + b + c) / 3f - inside) < 0f) (b, c) = (c, b);
            Emit(a, b, c, color);
        }

        /// <summary>Adds a triangle wound as given (clockwise seen from its front).</summary>
        public void TriRaw(Vector3 a, Vector3 b, Vector3 c, Color color) => Emit(a, b, c, color);

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3 inside) {
            Tri(a, b, c, color, inside);
            Tri(a, c, d, color, inside);
        }

        void Emit(Vector3 a, Vector3 b, Vector3 c, Color color) {
            Vector3 wa = matrix.MultiplyPoint3x4(a), wb = matrix.MultiplyPoint3x4(b), wc = matrix.MultiplyPoint3x4(c);
            Vector3 normal = Vector3.Cross(wb - wa, wc - wa);
            if (normal.sqrMagnitude < 1e-12f) return;
            normal.Normalize();
            Color32 color32 = color;
            int start = positions.Count;
            Add(wa, a, normal, color32);
            Add(wb, b, normal, color32);
            Add(wc, c, normal, color32);
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
        }

        void Add(Vector3 world, Vector3 local, Vector3 normal, Color32 color) {
            positions.Add(world);
            normals.Add(normal);
            colors.Add(color);
            uvs.Add(new Vector2(SwayOf != null ? SwayOf(local) : Sway, 0f));
        }

        // ---------------------------------------------------------------- solids
        /// <summary>A box centred on <paramref name="centre"/>, with its own top colour.</summary>
        public void Box(Vector3 centre, Vector3 size, Color side, Color top) {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => centre + new Vector3(x * h.x, y * h.y, z * h.z);
            Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), top, centre);
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Shade(side, 0.8f), centre);
            Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), side, centre);
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), side, centre);
            Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), Shade(side, 0.92f), centre);
            Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Shade(side, 0.92f), centre);
        }

        public void Box(Vector3 centre, Vector3 size, Color color) => Box(centre, size, color, color);

        /// <summary>
        /// A frustum along +Y from <paramref name="baseCentre"/>: <paramref name="topRadius"/> 0 makes a cone,
        /// a negative height hangs it downwards (icicles, spires under rock).
        /// </summary>
        public void Prism(Vector3 baseCentre, float baseRadius, float topRadius, float height, int sides, Color side, Color top, Color bottom, float angle = 0f) {
            sides = Mathf.Max(3, sides);
            Vector3 inside = baseCentre + Vector3.up * (height * 0.5f);
            Vector3 topCentre = baseCentre + Vector3.up * height;
            for (int i = 0; i < sides; i++) {
                float a0 = angle + i * Mathf.PI * 2f / sides, a1 = angle + (i + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = new(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 b0 = baseCentre + d0 * baseRadius, b1 = baseCentre + d1 * baseRadius;
                Vector3 t0 = topCentre + d0 * topRadius, t1 = topCentre + d1 * topRadius;
                Color face = Shade(side, 0.9f + 0.1f * Mathf.Cos(a0 * 1.7f));
                if (topRadius > 0.0001f) Quad(b0, t0, t1, b1, face, inside);
                else Tri(b0, topCentre, b1, face, inside);
                if (baseRadius > 0.0001f) Tri(baseCentre, b1, b0, bottom, inside);
                if (topRadius > 0.0001f) Tri(topCentre, t0, t1, top, inside);
            }
        }

        public void Prism(Vector3 baseCentre, float baseRadius, float topRadius, float height, int sides, Color color, float angle = 0f) =>
            Prism(baseCentre, baseRadius, topRadius, height, sides, color, color, Shade(color, 0.8f), angle);

        /// <summary>
        /// Rings of jittered radius stacked along Y (heights <paramref name="ys"/>, radii <paramref name="radii"/>):
        /// floating island roots, mountains, keels. Band i (between ring i and i+1) takes <paramref name="bands"/>[i].
        /// Rings run from the top down; the first is capped with <paramref name="top"/> when its radius is positive.
        /// </summary>
        public void Lathe(Vector3 centre, float[] ys, float[] radii, int sides, Color[] bands, Color top, ref OB_EnvRandom random,
            float jitter = 0f, float angle = 0f, Vector2 squash = default) {
            if (squash == default) squash = Vector2.one;
            int rings = ys.Length;
            var ring = new Vector3[rings, sides];
            for (int r = 0; r < rings; r++)
                for (int i = 0; i < sides; i++) {
                    float a = angle + i * Mathf.PI * 2f / sides;
                    float radius = radii[r] * (1f + random.Range(-jitter, jitter));
                    ring[r, i] = centre + new Vector3(Mathf.Cos(a) * radius * squash.x, ys[r], Mathf.Sin(a) * radius * squash.y);
                }
            for (int r = 0; r + 1 < rings; r++) {
                Vector3 inside = centre + Vector3.up * ((ys[r] + ys[r + 1]) * 0.5f);
                Color band = bands[Mathf.Min(r, bands.Length - 1)];
                for (int i = 0; i < sides; i++) {
                    int j = (i + 1) % sides;
                    Color face = Shade(band, 0.88f + 0.12f * Mathf.Sin(i * 2.3f + r));
                    if (radii[r + 1] <= 0.0001f) Tri(ring[r, i], ring[r, j], ring[r + 1, 0], face, inside);
                    else if (radii[r] <= 0.0001f) Tri(ring[r, 0], ring[r + 1, j], ring[r + 1, i], face, inside);
                    else Quad(ring[r, i], ring[r, j], ring[r + 1, j], ring[r + 1, i], face, inside);
                }
            }
            if (radii[0] > 0.0001f) {
                Vector3 capCentre = centre + Vector3.up * ys[0];
                for (int i = 0; i < sides; i++)
                    Tri(capCentre, ring[0, (i + 1) % sides], ring[0, i], top, capCentre - Vector3.up);
            }
        }

        /// <summary>A lumpy low-poly rock: a once-subdivided icosahedron with jittered radii, lit tops coloured <paramref name="top"/>.</summary>
        public void Rock(Vector3 centre, Vector3 radii, ref OB_EnvRandom random, Color top, Color side, float jitter = 0.22f, float topThreshold = 0.55f) {
            var verts = Icosphere(out var faces);
            var scale = new float[verts.Count];
            for (int i = 0; i < verts.Count; i++) scale[i] = 1f + random.Range(-jitter, jitter);
            foreach (var f in faces) {
                Vector3 a = centre + Vector3.Scale(verts[f.x] * scale[f.x], radii);
                Vector3 b = centre + Vector3.Scale(verts[f.y] * scale[f.y], radii);
                Vector3 c = centre + Vector3.Scale(verts[f.z] * scale[f.z], radii);
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(n, (a + b + c) / 3f - centre) < 0f) n = -n;
                Color face = n.y > topThreshold ? Shade(top, 0.94f + 0.06f * random.Value()) : Shade(side, 0.82f + 0.18f * random.Value());
                Tri(a, b, c, face, centre);
            }
        }

        /// <summary>A low-poly sphere (crowns of trees, balloons, planets, cloud puffs).</summary>
        public void Sphere(Vector3 centre, Vector3 radii, int rings, int segments, Color color, Color? top = null) {
            Vector3 P(int r, int s) {
                float v = Mathf.PI * r / rings, u = Mathf.PI * 2f * s / segments;
                return centre + Vector3.Scale(new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(u)), radii);
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++) {
                    Color face = top.HasValue && r < rings / 2 ? top.Value : color;
                    face = Shade(face, 0.9f + 0.1f * Mathf.Cos(s * 1.3f + r));
                    Vector3 a = P(r, s), b = P(r, s + 1), c = P(r + 1, s + 1), d = P(r + 1, s);
                    if (r == 0) Tri(a, c, d, face, centre);
                    else if (r == rings - 1) Tri(a, b, c, face, centre);
                    else Quad(a, b, c, d, face, centre);
                }
        }

        /// <summary>A torus around local +Y (portal rings, planet rings, wreaths).</summary>
        public void Torus(Vector3 centre, float radius, float tube, int segments, int sides, Color color) {
            for (int s = 0; s < segments; s++) {
                float u0 = Mathf.PI * 2f * s / segments, u1 = Mathf.PI * 2f * (s + 1) / segments;
                Vector3 c0 = new(Mathf.Cos(u0), 0f, Mathf.Sin(u0)), c1 = new(Mathf.Cos(u1), 0f, Mathf.Sin(u1));
                Vector3 inside = centre + (c0 + c1).normalized * radius;
                for (int k = 0; k < sides; k++) {
                    float v0 = Mathf.PI * 2f * k / sides, v1 = Mathf.PI * 2f * (k + 1) / sides;
                    Vector3 P(Vector3 around, float v) => centre + around * (radius + Mathf.Cos(v) * tube) + Vector3.up * (Mathf.Sin(v) * tube);
                    Color face = Shade(color, 0.85f + 0.15f * Mathf.Sin(v0));
                    Quad(P(c0, v0), P(c1, v0), P(c1, v1), P(c0, v1), face, inside);
                }
            }
        }

        /// <summary>A flat two-sided quad in the local XY plane hanging down from <paramref name="top"/> (banners, pennants).</summary>
        public void Panel(Vector3 topLeft, Vector3 topRight, Vector3 bottomRight, Vector3 bottomLeft, Color color) {
            TriRaw(topLeft, topRight, bottomRight, color);
            TriRaw(topLeft, bottomRight, bottomLeft, color);
        }

        // ---------------------------------------------------------------- output
        /// <summary>
        /// The mesh, compact: corners shared by the triangles of one flat face are welded, and vertices
        /// are packed into 24 bytes (float position, 8-bit normal, 8-bit colour, half-float sway weight),
        /// which keeps the generated mesh assets small.
        /// </summary>
        public Mesh Build(string name) {
            var packed = new List<PackedVertex>(positions.Count);
            var lookup = new Dictionary<PackedVertex, int>(positions.Count);
            var welded = new int[indices.Count];
            var bounds = new Bounds(positions.Count > 0 ? positions[0] : Vector3.zero, Vector3.zero);
            for (int i = 0; i < indices.Count; i++) {
                int source = indices[i];
                var vertex = PackedVertex.From(positions[source], normals[source], colors[source], uvs[source].x);
                if (!lookup.TryGetValue(vertex, out int index)) {
                    index = packed.Count;
                    packed.Add(vertex);
                    lookup.Add(vertex, index);
                    bounds.Encapsulate(positions[source]);
                }
                welded[i] = index;
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertexBufferParams(packed.Count, PackedVertex.Layout);
            mesh.SetVertexBufferData(packed, 0, 0, packed.Count, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            var indexFormat = packed.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetIndexBufferParams(welded.Length, indexFormat);
            if (indexFormat == IndexFormat.UInt16) {
                var shortIndices = new ushort[welded.Length];
                for (int i = 0; i < welded.Length; i++) shortIndices[i] = (ushort)welded[i];
                mesh.SetIndexBufferData(shortIndices, 0, 0, shortIndices.Length, MeshUpdateFlags.DontRecalculateBounds);
            } else {
                mesh.SetIndexBufferData(welded, 0, 0, welded.Length, MeshUpdateFlags.DontRecalculateBounds);
            }
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, welded.Length));   // also works out its vertex range and bounds
            mesh.bounds = bounds;
            return mesh;
        }

        /// <summary>One vertex as the GPU reads it; also the weld key (equal packed vertices are one vertex).</summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct PackedVertex : System.IEquatable<PackedVertex> {
            public Vector3 position;
            public sbyte nx, ny, nz, nw;
            public Color32 color;
            public ushort sway, unused;

            public static readonly VertexAttributeDescriptor[] Layout = {
                new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
                new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2)
            };

            public static PackedVertex From(Vector3 position, Vector3 normal, Color32 color, float sway) => new() {
                position = position,
                nx = Snorm(normal.x), ny = Snorm(normal.y), nz = Snorm(normal.z),
                color = color,
                sway = Mathf.FloatToHalf(sway)
            };

            static sbyte Snorm(float value) => (sbyte)Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * 127f);

            public bool Equals(PackedVertex other) => position.Equals(other.position) && nx == other.nx && ny == other.ny && nz == other.nz
                && color.r == other.color.r && color.g == other.color.g && color.b == other.color.b && color.a == other.color.a && sway == other.sway;

            public override bool Equals(object obj) => obj is PackedVertex other && Equals(other);

            public override int GetHashCode() => System.HashCode.Combine(position, (byte)nx | ((byte)ny << 8) | ((byte)nz << 16), color.r | (color.g << 8) | (color.b << 16) | (color.a << 24), sway);
        }

        public void Clear() {
            positions.Clear();
            normals.Clear();
            colors.Clear();
            uvs.Clear();
            indices.Clear();
            stack.Clear();
            matrix = Matrix4x4.identity;
            Sway = 0f;
            SwayOf = null;
        }

        // ---------------------------------------------------------------- colour helpers
        /// <summary><paramref name="color"/> darkened (or lightened above 1) by <paramref name="factor"/>, keeping its emission mask.</summary>
        public static Color Shade(Color color, float factor) => new(color.r * factor, color.g * factor, color.b * factor, color.a);

        /// <summary><paramref name="color"/> marked emissive with strength <paramref name="amount"/> (0..1, scaled by the material).</summary>
        public static Color Glow(Color color, float amount = 1f) => new(color.r, color.g, color.b, amount);

        /// <summary>An opaque, non-emissive colour.</summary>
        public static Color Solid(Color color) => new(color.r, color.g, color.b, 0f);

        // ---------------------------------------------------------------- icosphere
        static List<Vector3> icoVerts;
        static List<Vector3Int> icoFaces;

        static List<Vector3> Icosphere(out List<Vector3Int> faces) {
            if (icoVerts != null) {
                faces = icoFaces;
                return icoVerts;
            }
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var verts = new List<Vector3> {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var baseFaces = new List<Vector3Int> {
                new(0, 11, 5), new(0, 5, 1), new(0, 1, 7), new(0, 7, 10), new(0, 10, 11),
                new(1, 5, 9), new(5, 11, 4), new(11, 10, 2), new(10, 7, 6), new(7, 1, 8),
                new(3, 9, 4), new(3, 4, 2), new(3, 2, 6), new(3, 6, 8), new(3, 8, 9),
                new(4, 9, 5), new(2, 4, 11), new(6, 2, 10), new(8, 6, 7), new(9, 8, 1)
            };
            // One subdivision: 80 faces, chunky but round enough.
            var midpoints = new Dictionary<long, int>();
            int Mid(int a, int b) {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (midpoints.TryGetValue(key, out int index)) return index;
                verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                midpoints.Add(key, verts.Count - 1);
                return verts.Count - 1;
            }
            faces = new List<Vector3Int>();
            foreach (var f in baseFaces) {
                int ab = Mid(f.x, f.y), bc = Mid(f.y, f.z), ca = Mid(f.z, f.x);
                faces.Add(new Vector3Int(f.x, ab, ca));
                faces.Add(new Vector3Int(f.y, bc, ab));
                faces.Add(new Vector3Int(f.z, ca, bc));
                faces.Add(new Vector3Int(ab, bc, ca));
            }
            icoVerts = verts;
            icoFaces = faces;
            return verts;
        }
    }
}
#endif
