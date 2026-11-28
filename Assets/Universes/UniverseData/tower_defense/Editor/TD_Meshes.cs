using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.tower_defense.Editor {
    /// <summary>
    /// Procedural meshes Tower Defense needs and no asset pack provides (the road, which follows the lane exactly),
    /// saved under Meshes/ so prefabs and the scene reference stable assets.
    /// </summary>
    static class TD_Meshes {
        /// <summary>A flat ribbon following <paramref name="points"/> at <paramref name="width"/>, with square caps extended by half the width so corners close.</summary>
        public static Mesh Ribbon(string folder, string name, IReadOnlyList<Vector3> points, float width, float y, float uvPerMetre) {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float half = width * 0.5f;
            float v = 0f;
            for (int s = 0; s + 1 < points.Count; s++) {
                Vector3 a = points[s], b = points[s + 1];
                Vector3 dir = (b - a).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, dir) * half;
                a -= dir * half;
                b += dir * half;
                float length = Vector3.Distance(a, b);
                int start = vertices.Count;
                // each segment sits a hair above the previous one so overlapping corners never z-fight
                Vector3 lift = Vector3.up * (y + s * 0.002f);
                vertices.AddRange(new[] { a - side + lift, a + side + lift, b + side + lift, b - side + lift });
                for (int k = 0; k < 4; k++) normals.Add(Vector3.up);
                uvs.AddRange(new[] { new Vector2(0f, v), new Vector2(1f, v), new Vector2(1f, v + length * uvPerMetre), new Vector2(0f, v + length * uvPerMetre) });
                v += length * uvPerMetre;
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
            return Save(folder, name, vertices, normals, triangles, uvs);
        }

        /// <summary>
        /// A flat strip following <paramref name="points"/> at <paramref name="width"/> with every corner rounded
        /// to <paramref name="cornerRadius"/> (measured on the centre line), so the road bends instead of meeting
        /// in square overlaps. Both ends run on by half the width. U goes across (0..1), V along at
        /// <paramref name="uvPerMetre"/>. Keep the radius at least half the width or the inner edge folds over.
        /// </summary>
        public static Mesh RoundedStrip(string folder, string name, IReadOnlyList<Vector3> points, float width, float y, float uvPerMetre, float cornerRadius) {
            var line = Filleted(points, cornerRadius, width * 0.5f);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float half = width * 0.5f, v = 0f;
            for (int i = 0; i < line.Count; i++) {
                Vector3 dir = i == 0 ? line[1] - line[0] : i == line.Count - 1 ? line[i] - line[i - 1] : line[i + 1] - line[i - 1];
                dir.y = 0f;
                Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * half;
                if (i > 0) v += Vector3.Distance(line[i], line[i - 1]) * uvPerMetre;
                Vector3 p = new(line[i].x, y, line[i].z);
                vertices.Add(p - side);
                vertices.Add(p + side);
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0f, v));
                uvs.Add(new Vector2(1f, v));
                if (i == 0) continue;
                int a = vertices.Count - 4;
                triangles.AddRange(new[] { a, a + 3, a + 1, a, a + 2, a + 3 });
            }
            return Save(folder, name, vertices, normals, triangles, uvs);
        }

        /// <summary>
        /// The polyline with each inner corner replaced by an arc of <paramref name="radius"/> (shrunk where the
        /// legs are too short), and both ends pushed out by <paramref name="cap"/>.
        /// </summary>
        public static List<Vector3> Filleted(IReadOnlyList<Vector3> points, float radius, float cap) {
            var result = new List<Vector3> { points[0] - (points[1] - points[0]).normalized * cap };
            for (int i = 1; i + 1 < points.Count; i++) {
                Vector3 p = points[i], d0 = (p - points[i - 1]).normalized, d1 = (points[i + 1] - p).normalized;
                float turn = Vector3.Angle(d0, d1) * Mathf.Deg2Rad;
                if (turn < 0.01f) { result.Add(p); continue; }
                float legs = Mathf.Min(Vector3.Distance(p, points[i - 1]), Vector3.Distance(p, points[i + 1])) * 0.5f;
                float r = Mathf.Min(radius, legs / Mathf.Tan(turn * 0.5f));
                float trim = r * Mathf.Tan(turn * 0.5f);
                Vector3 start = p - d0 * trim;
                // Centre of the arc: from the start point, perpendicular to d0 toward the inside of the turn.
                Vector3 inward = Vector3.Cross(Vector3.up, d0);
                if (Vector3.Dot(inward, d1) < 0f) inward = -inward;
                Vector3 centre = start + inward * r;
                int steps = Mathf.Max(3, Mathf.CeilToInt(turn / (Mathf.PI / 16f)));
                Vector3 from = start - centre;
                float sign = Vector3.Cross(d0, d1).y > 0f ? 1f : -1f;
                for (int k = 0; k <= steps; k++)
                    result.Add(centre + Quaternion.AngleAxis(sign * turn * Mathf.Rad2Deg * k / steps, Vector3.up) * from);
            }
            result.Add(points[^1] + (points[^1] - points[^2]).normalized * cap);
            return result;
        }

        static Mesh Save(string folder, string name, List<Vector3> vertices, List<Vector3> normals, List<int> triangles, List<Vector2> uvs) {
            string path = $"{folder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) {
                mesh = new Mesh { name = name };
                AssetDatabase.CreateAsset(mesh, path);
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
