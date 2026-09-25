using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Core {
    /// <summary>
    /// Finds the "primary part" of a model - the surface that reads as its main body - so it can be
    /// painted on its own while every other part keeps its authored colours.
    /// <para>
    /// A mesh with several submeshes already has its parts separated by material; the submesh with
    /// the most surface area is the primary part. Most low-poly packs instead draw a whole model with
    /// one material and one small colour atlas, where every face of a given colour samples the same
    /// patch of the texture. For those, faces are grouped by the atlas cell their UVs land in and the
    /// cell covering the most surface is split off into a submesh of its own. The split copy is made
    /// once per source mesh and shared, so painting a hundred barracks costs one extra mesh.
    /// </para>
    /// <para>
    /// A mesh that is not CPU-readable, or has no UVs, cannot be analysed; callers fall back to
    /// leaving the model unpainted rather than guessing.
    /// </para>
    /// </summary>
    public static class MeshPrimaryRegion {
        /// <summary>
        /// Atlas cells per UV axis. Fine enough to separate the swatches of a typical colour atlas,
        /// coarse enough that faces sampling one swatch land in the same cell.
        /// </summary>
        public const int AtlasGrid = 32;

        /// <summary>The primary part of one mesh.</summary>
        public readonly struct Result {
            /// <summary>The mesh to draw: the source itself, or its split copy.</summary>
            public readonly Mesh Mesh;
            /// <summary>Submesh (and therefore material slot) holding the primary part.</summary>
            public readonly int Submesh;
            /// <summary>Surface area of the primary part, in the mesh's own units.</summary>
            public readonly float Area;
            /// <summary>True when <see cref="Mesh"/> is a split copy with one more submesh than the source.</summary>
            public readonly bool IsSplit;

            public Result(Mesh mesh, int submesh, float area, bool isSplit) {
                Mesh = mesh;
                Submesh = submesh;
                Area = area;
                IsSplit = isSplit;
            }
        }

        static readonly Dictionary<Mesh, Result> cache = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => cache.Clear();

        public static bool TryGetPrimary(Mesh source, out Result result) {
            result = default;
            if (source == null || !source.isReadable)
                return false;
            if (cache.TryGetValue(source, out result))
                return result.Mesh != null;

            result = source.subMeshCount > 1 ? LargestSubmesh(source) : SplitDominantAtlasCell(source);
            cache[source] = result;
            return result.Mesh != null;
        }

        static Result LargestSubmesh(Mesh mesh) {
            Vector3[] vertices = mesh.vertices;
            int best = 0;
            float bestArea = -1f;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) {
                float area = SurfaceArea(vertices, mesh.GetTriangles(submesh));
                if (area > bestArea) {
                    bestArea = area;
                    best = submesh;
                }
            }
            return new Result(mesh, best, bestArea, isSplit: false);
        }

        static Result SplitDominantAtlasCell(Mesh source) {
            Vector3[] vertices = source.vertices;
            Vector2[] uvs = source.uv;
            int[] triangles = source.GetTriangles(0);
            if (uvs.Length != vertices.Length || triangles.Length == 0)
                return default;

            var cellAreas = new Dictionary<int, float>();
            var triangleCells = new int[triangles.Length / 3];
            for (int t = 0; t < triangleCells.Length; t++) {
                int a = triangles[t * 3], b = triangles[t * 3 + 1], c = triangles[t * 3 + 2];
                int cell = AtlasCell((uvs[a] + uvs[b] + uvs[c]) / 3f);
                triangleCells[t] = cell;
                cellAreas.TryGetValue(cell, out float area);
                cellAreas[cell] = area + TriangleArea(vertices[a], vertices[b], vertices[c]);
            }

            int primaryCell = 0;
            float primaryArea = -1f;
            foreach (KeyValuePair<int, float> entry in cellAreas) {
                if (entry.Value > primaryArea) {
                    primaryArea = entry.Value;
                    primaryCell = entry.Key;
                }
            }

            var primary = new List<int>();
            var rest = new List<int>();
            for (int t = 0; t < triangleCells.Length; t++) {
                List<int> target = triangleCells[t] == primaryCell ? primary : rest;
                target.Add(triangles[t * 3]);
                target.Add(triangles[t * 3 + 1]);
                target.Add(triangles[t * 3 + 2]);
            }

            Mesh split = Object.Instantiate(source);
            split.name = $"{source.name} (primary split)";
            split.subMeshCount = 2;
            split.SetTriangles(primary, 0, calculateBounds: false);
            split.SetTriangles(rest, 1, calculateBounds: false);
            return new Result(split, 0, primaryArea, isSplit: true);
        }

        static int AtlasCell(Vector2 uv) {
            int x = Mathf.Min(AtlasGrid - 1, Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * AtlasGrid));
            int y = Mathf.Min(AtlasGrid - 1, Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * AtlasGrid));
            return y * AtlasGrid + x;
        }

        static float SurfaceArea(Vector3[] vertices, int[] triangles) {
            float total = 0f;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
                total += TriangleArea(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
            return total;
        }

        static float TriangleArea(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).magnitude * 0.5f;
    }
}
