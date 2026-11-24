#if !UNITY_SERVER
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Universes.UniverseData.obby.Client;
using B = Universes.UniverseData.obby.Editor.OB_EnvMeshBuilder;

namespace Universes.UniverseData.obby.Editor {
    public static partial class OB_EnvironmentAuthoring {
        const string LandmarkFolder = PrefabFolder + "/Landmarks";
        /// <summary>How long a fall ribbon is (m) and the length its material fades over; falls vanish into the cloud sea.</summary>
        const float FallLength = 200f;
        const int FallSegments = 14;

        /// <summary>Every tier's landmark prefabs, in the order of its <see cref="OB_WorldStyle.landmarks"/>.</summary>
        static OB_Landmarks.TierSet[] BuildLandmarks(TierMaterials[] materials) {
            EnsureFolder(LandmarkFolder);
            var sets = new OB_Landmarks.TierSet[OB_Tiers.Count];
            for (int t = 0; t < OB_Tiers.Count; t++) {
                var tier = (OB_Tier)t;
                OB_WorldStyle world = OB_Themes.Get(tier).world;
                var kinds = world.landmarks ?? new OB_LandmarkKind[0];
                sets[t].prefabs = new OB_Landmark[kinds.Length];
                for (int i = 0; i < kinds.Length; i++) {
                    var kit = new LandmarkKit(kinds[i], world, materials[t]);
                    float radius = BuildLandmark(kit);
                    sets[t].prefabs[i] = kit.Save(radius);
                }
            }
            return sets;
        }

        /// <summary>Builds one landmark into <paramref name="kit"/> and returns its footprint radius (m).</summary>
        static float BuildLandmark(LandmarkKit kit) => kit.kind switch {
            OB_LandmarkKind.SkyCastle => SkyCastle(kit),
            OB_LandmarkKind.WindmillIsle => WindmillIsle(kit),
            OB_LandmarkKind.BalloonFleet => BalloonFleet(kit),
            OB_LandmarkKind.WorldTree => WorldTree(kit),
            OB_LandmarkKind.WaterfallCliffs => WaterfallCliffs(kit),
            OB_LandmarkKind.Pyramids => Pyramids(kit),
            OB_LandmarkKind.MesaArch => MesaArch(kit),
            OB_LandmarkKind.IceMountain => IceMountain(kit),
            OB_LandmarkKind.GlacierSpires => GlacierSpires(kit),
            OB_LandmarkKind.Volcano => Volcano(kit),
            OB_LandmarkKind.BasaltSpires => BasaltSpires(kit),
            OB_LandmarkKind.ShatteredPlanet => ShatteredPlanet(kit),
            OB_LandmarkKind.PortalRings => PortalRings(kit),
            _ => throw new System.ArgumentOutOfRangeException(nameof(kit), kit.kind, "No builder for this landmark")
        };

        /// <summary>
        /// Everything one landmark prefab is made of while it is being built: the static body mesh, moving
        /// parts (each its own mesh and <see cref="OB_EnvMotion"/>), falls and particle effects. The prefab's
        /// origin is the centre of its base island's top, and it faces the course along +Z.
        /// </summary>
        sealed class LandmarkKit {
            public readonly OB_LandmarkKind kind;
            public readonly OB_WorldStyle world;
            public readonly Palette p;
            public readonly TierMaterials materials;
            public readonly B body = new();
            public readonly B part = new();
            public readonly FallStrips falls = new();
            public readonly GameObject root;
            public OB_EnvRandom random;

            public LandmarkKit(OB_LandmarkKind kind, OB_WorldStyle world, TierMaterials materials) {
                this.kind = kind;
                this.world = world;
                this.materials = materials;
                p = new Palette(world);
                random = new OB_EnvRandom(ShapeSeed, 400u + (uint)kind);
                root = new GameObject(kind.ToString());
            }

            /// <summary>Saves <see cref="part"/> as a moving child at <paramref name="position"/> and clears it for the next.</summary>
            public OB_EnvMotion Part(string name, Vector3 position, Quaternion? rotation = null) {
                var mesh = SaveMesh(part, $"{kind}{name}", "Landmarks");
                part.Clear();
                var renderer = MeshChild(root.transform, name, mesh, materials.landmark, ShadowCastingMode.On);
                renderer.transform.localPosition = position;
                renderer.transform.localRotation = rotation ?? Quaternion.identity;
                var motion = renderer.gameObject.AddComponent<OB_EnvMotion>();
                motion.phase = random.Range(0f, 20f);
                return motion;
            }

            /// <summary>A fall pouring off a lip at <paramref name="lip"/> outward along <paramref name="outward"/>.</summary>
            public void Fall(Vector3 lip, Vector3 outward, float width, float reach = 6f) => falls.Add(lip, outward, width, reach);

            public OB_Landmark Save(float radius) {
                var bodyMesh = SaveMesh(body, $"{kind}Body", "Landmarks");
                var bodyRenderer = MeshChild(root.transform, "Body", bodyMesh, materials.landmark, ShadowCastingMode.On);
                bodyRenderer.transform.SetAsFirstSibling();
                if (!falls.Empty && materials.fall != null) {
                    var fallRenderer = MeshChild(root.transform, "Falls", SaveMesh(falls.Build($"{kind}Falls"), "Landmarks"), materials.fall, ShadowCastingMode.Off);
                    fallRenderer.receiveShadows = false;
                    fallRenderer.lightProbeUsage = LightProbeUsage.Off;
                }
                var landmark = root.AddComponent<OB_Landmark>();
                landmark.kind = kind;
                landmark.radius = radius;
                landmark.height = Mathf.Max(1f, Top());
                var prefab = SavePrefab(root, $"{LandmarkFolder}/{kind}.prefab");
                return prefab.GetComponent<OB_Landmark>();
            }

            /// <summary>Highest point of the landmark's meshes above its origin (m).</summary>
            float Top() {
                float top = 0f;
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>()) {
                    if (filter.sharedMesh == null) continue;
                    Bounds local = filter.sharedMesh.bounds;
                    var toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    // The bounds' corners, so rotated parts are measured too.
                    for (int corner = 0; corner < 8; corner++) {
                        Vector3 c = local.center + Vector3.Scale(local.extents, new Vector3((corner & 1) * 2 - 1, (corner & 2) - 1, (corner & 4) / 2f - 1));
                        top = Mathf.Max(top, toRoot.MultiplyPoint3x4(c).y);
                    }
                }
                return top;
            }
        }

        /// <summary>
        /// Ribbons for the Fall shader: UV0.x runs across the stream (0..1), UV0.y along it in metres from
        /// the lip. Each stream arcs out from its lip, falls and widens as it goes.
        /// </summary>
        sealed class FallStrips {
            readonly List<Vector3> vertices = new();
            readonly List<Vector2> uvs = new();
            readonly List<int> triangles = new();
            const float Widening = 0.6f;

            public bool Empty => vertices.Count == 0;

            public void Add(Vector3 lip, Vector3 outward, float width, float reach) {
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.forward;
                Vector3 across = Vector3.Cross(Vector3.up, outward).normalized;
                int start = vertices.Count;
                for (int i = 0; i <= FallSegments; i++) {
                    float t = i / (float)FallSegments, drop = t * FallLength;
                    // Out from the lip quickly, then nearly straight down, like water leaving an edge.
                    Vector3 centre = lip + outward * (reach * Mathf.Sqrt(t)) + Vector3.down * drop;
                    float half = width * 0.5f * (1f + Widening * t);
                    vertices.Add(centre - across * half);
                    vertices.Add(centre + across * half);
                    uvs.Add(new Vector2(0f, drop));
                    uvs.Add(new Vector2(1f, drop));
                    if (i == FallSegments) continue;
                    int v = start + i * 2;
                    triangles.AddRange(new[] { v, v + 1, v + 3, v, v + 3, v + 2 });
                }
            }

            public Mesh Build(string name) {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ---------------------------------------------------------------- shared landmark pieces
        /// <summary>A landmark's base island, its top at the origin.</summary>
        static void BaseIsland(LandmarkKit k, float radius, float depth, int sides = 14) =>
            Island(k.body, Vector3.zero, radius, depth, k.p, ref k.random, sides);

        /// <summary>Falls pouring off the island's course-facing edge (+Z) at the given angles from +Z (degrees).</summary>
        static void EdgeFalls(LandmarkKit k, float radius, float width, params float[] degrees) {
            foreach (float d in degrees) {
                Vector3 outward = Quaternion.Euler(0f, d, 0f) * Vector3.forward;
                k.Fall(outward * (radius * 0.97f) + Vector3.down * 0.5f, outward, width);
            }
        }

        /// <summary>A tower: round shaft, a band at the top and a conical roof.</summary>
        static void Tower(B b, Vector3 at, float radius, float height, float roof, Color wall, Color band, Color roofColor, int sides = 10) {
            b.Prism(at, radius, radius * 0.94f, height, sides, wall);
            b.Prism(at + Vector3.up * height, radius * 1.15f, radius * 1.15f, radius * 0.5f, sides, band);
            b.Prism(at + Vector3.up * (height + radius * 0.5f), radius * 1.2f, 0f, roof, sides, roofColor);
        }

        /// <summary>A flag on a pole, fixed in the breeze.</summary>
        static void Flag(B b, Vector3 at, float pole, float size, Color poleColor, Color cloth) {
            b.Prism(at, size * 0.04f, size * 0.03f, pole, 4, poleColor);
            Vector3 top = at + Vector3.up * pole;
            b.Panel(top, top + new Vector3(0f, -size * 0.1f, size), top + new Vector3(0f, -size * 0.65f, size * 0.9f), top + Vector3.down * (size * 0.6f), cloth);
        }

        /// <summary>Lumps of rock floating around a landmark, each bobbing on its own.</summary>
        static void FloatingChunks(LandmarkKit k, int count, Vector2 distance, Vector2 height, Vector2 size, Vector3 bob) {
            for (int i = 0; i < count; i++) {
                float s = k.random.Range(size);
                k.part.Rock(Vector3.zero, new Vector3(s, s * 0.7f, s), ref k.random, k.p.rockTop, k.p.rockSide);
                k.part.Prism(Vector3.down * (s * 0.4f), s * 0.6f, 0f, -s * 1.4f, 6, k.p.rockDark);
                Vector3 at = k.random.OnCircle() * k.random.Range(distance) + Vector3.up * k.random.Range(height);
                var motion = k.Part($"Chunk{i}", at, Quaternion.Euler(0f, k.random.Range(0f, 360f), 0f));
                motion.bobOffset = bob * k.random.Range(0.7f, 1.3f);
                motion.bobPeriod = k.random.Range(6f, 11f);
                motion.spinSpeed = k.random.Range(-3f, 3f);
            }
        }
    }
}
#endif
