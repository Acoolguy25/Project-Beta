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
