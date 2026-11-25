using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Draws many copies of a few meshes with GPU instancing and no GameObjects: floating rocks, small
    /// islands and the course dressing. Instances are grouped by mesh, material and a coarse world
    /// cell, so the engine frustum-culls each cell's batch (for the camera and the shadow cascades) and
    /// this layer skips cells beyond <see cref="drawDistance"/>. Built once per course; drawing allocates
    /// nothing.
    /// </summary>
    public sealed class OB_InstancedLayer : MonoBehaviour {
        [Tooltip("Instances are batched per cell of this size (m). Bigger cells mean fewer draw calls but coarser culling.")]
        [Min(8f)] public float cellSize = 96f;
        [Tooltip("Cells farther than this from the camera are not drawn (m).")]
        [Min(1f)] public float drawDistance = 1400f;
        public ShadowCastingMode shadows = ShadowCastingMode.On;
        public bool receiveShadows = true;

        /// <summary>Unity draws at most this many instances per call with constant-buffer instancing.</summary>
        const int MaxPerCall = 1023;
        const int InitialCapacity = 16;

        sealed class Batch {
            public Mesh mesh;
            public Material material;
            public Matrix4x4[] matrices = new Matrix4x4[InitialCapacity];
            public int count;
            public Bounds bounds;
        }

        /// <summary>Batches are keyed by mesh and material reference plus the world cell.</summary>
        readonly struct BatchKey : System.IEquatable<BatchKey> {
            readonly Mesh mesh;
            readonly Material material;
            readonly long cell;
            public BatchKey(Mesh mesh, Material material, long cell) {
                this.mesh = mesh;
                this.material = material;
                this.cell = cell;
            }
            public bool Equals(BatchKey other) => ReferenceEquals(mesh, other.mesh) && ReferenceEquals(material, other.material) && cell == other.cell;
            public override bool Equals(object obj) => obj is BatchKey other && Equals(other);
            public override int GetHashCode() =>
                (RuntimeHelpers.GetHashCode(mesh) * 397 ^ RuntimeHelpers.GetHashCode(material)) * 397 ^ cell.GetHashCode();
        }

        readonly List<Batch> batches = new();
        readonly Dictionary<BatchKey, Batch> lookup = new();

        /// <summary>Instances currently queued, for diagnostics and tests.</summary>
        public int InstanceCount { get; private set; }
        public int BatchCount => batches.Count;

        /// <summary>Removes every instance. Arrays are kept for the next course.</summary>
        public void Clear() {
            foreach (var batch in batches) batch.count = 0;
            InstanceCount = 0;
        }

        public void Add(Mesh mesh, Material material, Matrix4x4 matrix) {
            if (mesh == null || material == null) return;
            Vector3 position = matrix.GetColumn(3);
            float inv = 1f / cellSize;
            long cell = ((long)Mathf.FloorToInt(position.x * inv) << 32) ^ (uint)Mathf.FloorToInt(position.z * inv);
            var key = new BatchKey(mesh, material, cell);
            if (!lookup.TryGetValue(key, out var batch)) {
                batch = new Batch { mesh = mesh, material = material };
                lookup.Add(key, batch);
                batches.Add(batch);
            }
            if (batch.count == batch.matrices.Length) System.Array.Resize(ref batch.matrices, batch.matrices.Length * 2);
            Bounds world = TransformBounds(matrix, mesh.bounds);
            if (batch.count == 0) batch.bounds = world; else batch.bounds.Encapsulate(world);
            batch.matrices[batch.count++] = matrix;
            InstanceCount++;
        }

        void Update() => Draw(null);

        /// <summary>
        /// Queues this frame's draws. Pass a camera to draw for that camera only (editor preview renders);
        /// null draws for every camera, as the game does each frame.
        /// </summary>
        public void Draw(Camera only) {
            if (InstanceCount == 0) return;
            Camera view = only != null ? only : Camera.main;
            bool cull = view != null;
            Vector3 eye = cull ? view.transform.position : Vector3.zero;
            float limit = drawDistance * drawDistance;
            foreach (var batch in batches) {
                if (batch.count == 0 || cull && batch.bounds.SqrDistance(eye) > limit) continue;
                var parameters = new RenderParams(batch.material) {
                    worldBounds = batch.bounds,
                    shadowCastingMode = shadows,
                    receiveShadows = receiveShadows,
                    layer = gameObject.layer,
                    camera = only,
                    // No baked probes in the obby: this hands the instances the scene's ambient light.
                    lightProbeUsage = LightProbeUsage.BlendProbes
                };
                for (int start = 0; start < batch.count; start += MaxPerCall)
                    Graphics.RenderMeshInstanced(parameters, batch.mesh, 0, batch.matrices, Mathf.Min(MaxPerCall, batch.count - start), start);
            }
        }

        /// <summary>The world-space box around <paramref name="local"/> after <paramref name="matrix"/>.</summary>
        public static Bounds TransformBounds(in Matrix4x4 matrix, Bounds local) {
            Vector3 centre = matrix.MultiplyPoint3x4(local.center);
            Vector3 e = local.extents;
            var extents = new Vector3(
                Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
                Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
                Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z);
            return new Bounds(centre, extents * 2f);
        }
    }
}
