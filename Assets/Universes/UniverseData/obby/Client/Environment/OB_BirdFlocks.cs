using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Flocks of birds wheeling over landmarks and open sky (gulls over the castle, crows over the
    /// fields, vultures over the dunes, glowing wisps in the void). Each bird is one instanced mesh whose
    /// wings flap in the vertex shader; positions are computed on the clock each frame into reused
    /// arrays, so it allocates nothing and every racer sees the same flocks.
    /// </summary>
    public sealed class OB_BirdFlocks : MonoBehaviour {
        public Mesh birdMesh;
        [Tooltip("Bird material per difficulty tier (indexed by OB_Tier); null for no birds.")]
        public Material[] tierMaterials;
        [Min(1)] public int birdsPerFlock = 7;
        [Tooltip("Flight speed along the circle (m/s).")]
        public Vector2 speed = new(9f, 15f);
        [Tooltip("Bird size (the mesh is about 1 m across).")]
        public Vector2 size = new(1.6f, 2.6f);
        [Tooltip("How far birds stray from their flock's circle: across, up and down (m).")]
        public Vector2 spread = new(6f, 4f);
        [Tooltip("Bank into the turn (degrees).")]
        public float bankDegrees = 18f;
        [Tooltip("Up-and-down drift of a bird's height (m) and its period (s).")]
        public Vector2 drift = new(2f, 7f);

        struct Bird {
            public int flock;
            public float angle, radius, height, scale, phase;
        }

        struct Flock {
            public Vector3 centre;
            public float radius, angularSpeed;
            public int tier;
        }

        readonly List<Flock> flocks = new();
        readonly List<Bird> birds = new();
        Matrix4x4[][] matrices;
        int[] counts;
        Bounds bounds;

        public void Arrange(IReadOnlyList<OB_Placement> placements) {
            flocks.Clear();
            birds.Clear();
            int tierCount = tierMaterials?.Length ?? 0;
            counts = new int[tierCount];
            bool any = false;
            foreach (var placement in placements) {
                if (placement.kind != (byte)OB_SceneryKind.Flock || (int)placement.tier >= tierCount || tierMaterials[(int)placement.tier] == null) continue;
                var random = new OB_EnvRandom(placement.variant, 0xB1Du);
                float radius = placement.matrix.lossyScale.x;
                float direction = random.Sign();
                var flock = new Flock {
                    centre = placement.matrix.GetColumn(3), radius = radius, tier = (int)placement.tier,
                    angularSpeed = direction * random.Range(speed) / Mathf.Max(1f, radius)
                };
                float start = random.Range(0f, Mathf.PI * 2f);
                for (int b = 0; b < birdsPerFlock; b++)
                    birds.Add(new Bird {
                        flock = flocks.Count, angle = start + b * random.Range(0.05f, 0.18f),
                        radius = radius + random.Range(-spread.x, spread.x), height = random.Range(-spread.y, spread.y),
                        scale = random.Range(size), phase = random.Range(0f, drift.y)
                    });
                counts[flock.tier] += birdsPerFlock;
                var reach = new Bounds(flock.centre, Vector3.one * ((radius + spread.x) * 2f + spread.y + drift.x));
                if (any) bounds.Encapsulate(reach); else bounds = reach;
                any = true;
                flocks.Add(flock);
            }
            matrices = new Matrix4x4[tierCount][];
            for (int t = 0; t < tierCount; t++) matrices[t] = new Matrix4x4[counts[t]];
        }

        void Update() => Draw(null);

        /// <summary>Queues this frame's birds; a camera limits them to that camera (editor previews).</summary>
        public void Draw(Camera only) {
            if (birds.Count == 0 || birdMesh == null) return;
            float time = Time.time;
            System.Array.Clear(counts, 0, counts.Length);
            foreach (var bird in birds) {
                Flock flock = flocks[bird.flock];
                float angle = bird.angle + flock.angularSpeed * time;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float bob = Mathf.Sin((time + bird.phase) * Mathf.PI * 2f / drift.y) * drift.x;
                Vector3 position = flock.centre + offset * bird.radius + Vector3.up * (bird.height + bob);
                // Fly along the circle, banked into the turn.
                Vector3 heading = Vector3.Cross(Vector3.up, offset) * Mathf.Sign(flock.angularSpeed);
                Quaternion rotation = Quaternion.LookRotation(heading) * Quaternion.Euler(0f, 0f, bankDegrees * Mathf.Sign(flock.angularSpeed));
                matrices[flock.tier][counts[flock.tier]++] = Matrix4x4.TRS(position, rotation, Vector3.one * bird.scale);
            }
            for (int t = 0; t < counts.Length; t++) {
                if (counts[t] == 0) continue;
                var parameters = new RenderParams(tierMaterials[t]) {
                    worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                    layer = gameObject.layer, camera = only, lightProbeUsage = LightProbeUsage.BlendProbes
                };
                Graphics.RenderMeshInstanced(parameters, birdMesh, 0, matrices[t], counts[t]);
            }
        }
    }
}
