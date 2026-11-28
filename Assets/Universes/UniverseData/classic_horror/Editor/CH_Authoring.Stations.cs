using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Editor {
    /// <summary>
    /// The set pieces of the minigames: the ferry's outboard and the chains and padlocks on the bell. The pack has no
    /// outboard, chain or padlock, so they are modelled here from lathed and swept meshes (saved as mesh assets) and
    /// lit like everything else; nothing is a Unity primitive and nothing glows.
    /// </summary>
    public static partial class CH_Authoring {
        static readonly Color EngineMetal = new(0.13f, 0.14f, 0.15f), EngineCowl = new(0.3f, 0.1f, 0.07f), CordGrip = new(0.85f, 0.5f, 0.12f), EngineRubber = new(0.04f, 0.04f, 0.045f);
        static readonly Color ChainIron = new(0.17f, 0.17f, 0.18f), LockBrass = new(0.62f, 0.45f, 0.18f);

        /// <summary>Sizes of the outboard, in metres, from the stern clamp up and down.</summary>
        static class Outboard {
            public const float CowlBottom = 0.38f, CowlTop = 0.98f, LegBottom = -0.62f, TorpedoLength = 0.34f, TorpedoRadius = 0.052f;
            public const float BladeLength = 0.24f, BladeWidth = 0.05f, BladeTwist = 28f;
            public const float CordLength = 0.34f, CordRadius = 0.01f, GripLength = 0.1f, GripRadius = 0.022f;
        }

        /// <summary>Sizes of the chains and padlocks on the bell.</summary>
        static class BellLocks {
            public const float BellRadius = 0.27f, Height = 0.34f, Standoff = 0.045f;
            public const int Links = 13;
            public const float LinkLength = 0.075f, LinkWidth = 0.046f, LinkTube = 0.0105f;
            public const float PadlockWidth = 0.06f, PadlockHeight = 0.11f, PadlockDepth = 0.026f, ShackleRadius = 0.032f, ShackleTube = 0.0085f;
        }

        /// <summary>How far past the stern mark, and how high, the outboard's clamp sits so its leg hangs over the transom into the water.</summary>
        const float TransomOffset = 0.95f, TransomLift = 0.0f;

        static Material Metal(string name, Color colour, float metallic, float smoothness) {
            Material material = Material(name, colour);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject Piece(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 angles = default, Vector3? scale = null) {
            var piece = new GameObject(name);
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = position;
            piece.transform.localEulerAngles = angles;
            piece.transform.localScale = scale ?? Vector3.one;
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            piece.AddComponent<MeshRenderer>().sharedMaterial = material;
            return piece;
        }

        /// <summary>A round part from its rings: each is (height, half width, half depth) in metres.</summary>
        static Mesh Turned(string name, params Vector3[] rings) => LatheMesh(name, rings, 1f);

        /// <summary>
        /// A chain link: a tube swept round a stadium (two half circles joined by straight sides) in the XY plane, long
        /// side along Y. <paramref name="length"/> is the straight part; with zero it is a plain ring.
        /// </summary>
        static Mesh LinkMesh(string name, float length, float width, float tube) {
            const int Path = 40, Around = 10;
            string path = Root + "/Materials/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            float radius = width * 0.5f, half = length * 0.5f;
            Vector3 Centre(float t, out Vector3 tangent) {
                // Perimeter split: two straights of `length`, two half circles of pi * radius.
                float arc = Mathf.PI * radius, total = 2f * length + 2f * arc, d = t * total;
                if (d < length) { tangent = Vector3.up; return new Vector3(radius, -half + d, 0f); }
                d -= length;
                if (d < arc) { float a = d / radius; tangent = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f); return new Vector3(Mathf.Cos(a) * radius, half + Mathf.Sin(a) * radius, 0f); }
                d -= arc;
                if (d < length) { tangent = Vector3.down; return new Vector3(-radius, half - d, 0f); }
                d -= length;
                { float a = d / radius; tangent = new Vector3(Mathf.Sin(a), -Mathf.Cos(a), 0f); return new Vector3(-Mathf.Cos(a) * radius, -half - Mathf.Sin(a) * radius, 0f); }
            }
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= Path; i++) {
                Vector3 centre = Centre(Mathf.Min(i / (float)Path, 0.9999f), out Vector3 tangent);
                Vector3 side = Vector3.forward, up = Vector3.Cross(tangent, side).normalized;
                for (int j = 0; j < Around; j++) {
                    float a = j * Mathf.PI * 2f / Around;
                    vertices.Add(centre + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * tube);
                }
            }
            for (int i = 0; i < Path; i++)
                for (int j = 0; j < Around; j++) {
                    int a = i * Around + j, b = i * Around + (j + 1) % Around, c = a + Around, d = b + Around;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>
        /// The ferry's outboard engine, clamped to the stern: a cowling with a seam and a choke knob, a leg down into the
        /// water, a torpedo gearbox with a three-bladed propeller, a tiller arm and a pull cord with a bright grip, so the
        /// first minigame has something to be done to. It is a child of the ferry and sails with it.
        /// </summary>
        internal static GameObject BuildEngineProp(Transform ferry, Vector3 stern, Vector3 outward) {
            var root = new GameObject("Outboard engine").transform;
            root.SetParent(ferry, true);
            root.position = stern + outward.normalized * TransomOffset + Vector3.up * TransomLift;
            outward.y = 0f;
            root.rotation = Quaternion.LookRotation(outward.sqrMagnitude > 1e-4f ? outward : Vector3.forward);
            Material cowl = Metal("EngineCowl", EngineCowl, 0.15f, 0.32f), steel = Metal("EngineMetal", EngineMetal, 0.8f, 0.45f);
            Material rubber = Metal("EngineRubber", EngineRubber, 0f, 0.3f), grip = Metal("EngineCord", CordGrip, 0f, 0.4f);
            Vector3 R(float y, float x, float z) => new(y, x, z);
            const float Bottom = Outboard.CowlBottom, Top = Outboard.CowlTop;
            // The hood: a barrel with a flat vented band round the middle and a rounded crown, long fore and aft.
            Piece("Cowling", root, Turned("CHEngineCowling",
                R(Bottom, 0.17f, 0.23f), R(Bottom + 0.025f, 0.19f, 0.255f), R(Bottom + 0.12f, 0.195f, 0.265f), R(Bottom + 0.4f, 0.19f, 0.26f),
                R(Top - 0.13f, 0.175f, 0.24f), R(Top - 0.07f, 0.14f, 0.2f), R(Top - 0.025f, 0.08f, 0.12f), R(Top, 0f, 0f)), cowl, Vector3.zero);
            Piece("Vent band", root, Turned("CHEngineVent", R(Bottom + 0.16f, 0.196f, 0.268f), R(Bottom + 0.17f, 0.2f, 0.272f), R(Bottom + 0.3f, 0.2f, 0.272f), R(Bottom + 0.31f, 0.196f, 0.268f)), steel, Vector3.zero);
            Piece("Fuel cap", root, Turned("CHEngineFuelCap", R(0f, 0.04f, 0.04f), R(0.02f, 0.045f, 0.045f), R(0.035f, 0.04f, 0.04f), R(0.04f, 0f, 0f)), steel, new Vector3(0f, Top - 0.045f, -0.1f));
            Piece("Cowl seam", root, Turned("CHEngineSeam", R(Bottom - 0.012f, 0.176f, 0.236f), R(Bottom + 0.018f, 0.196f, 0.262f), R(Bottom + 0.05f, 0.198f, 0.268f)), steel, Vector3.zero);
            Piece("Choke knob", root, Turned("CHEngineKnob", R(0f, 0.012f, 0.012f), R(0.03f, 0.012f, 0.012f), R(0.045f, 0.022f, 0.022f), R(0.06f, 0.02f, 0.02f), R(0.065f, 0f, 0f)), rubber,
                new Vector3(0.21f, Bottom + 0.3f, 0.02f), new Vector3(0f, 0f, -90f));
            // The drive shaft housing and the leg down under the water.
            Piece("Midsection", root, Turned("CHEngineMid", R(Bottom, 0.1f, 0.14f), R(Bottom - 0.1f, 0.082f, 0.115f), R(Bottom - 0.4f, 0.07f, 0.1f)), steel, Vector3.zero);
            Piece("Leg", root, Turned("CHEngineLeg", R(0f, 0.07f, 0.1f), R(-0.2f, 0.062f, 0.088f), R(Outboard.LegBottom + 0.0f, 0.05f, 0.07f)), steel, new Vector3(0f, Bottom - 0.4f, 0f));
            // The gearbox: a torpedo along the outward direction at the foot of the leg.
            float footY = Bottom - 0.4f + Outboard.LegBottom;
            float length = Outboard.TorpedoLength, radius = Outboard.TorpedoRadius;
            Piece("Gearbox", root, Turned("CHEngineTorpedo",
                R(-length * 0.5f, 0.005f, 0.005f), R(-length * 0.42f, radius * 0.7f, radius * 0.7f), R(-length * 0.25f, radius, radius), R(length * 0.2f, radius, radius),
                R(length * 0.42f, radius * 0.6f, radius * 0.6f), R(length * 0.5f, 0.01f, 0.01f)), steel, new Vector3(0f, footY + 0.03f, length * 0.1f), new Vector3(90f, 0f, 0f));
            Piece("Skeg", root, Turned("CHEngineSkeg", R(0f, 0.004f, 0.09f), R(0.07f, 0.004f, 0.07f), R(0.12f, 0.002f, 0.03f)), steel, new Vector3(0f, footY - 0.14f, 0.0f));
            // The propeller: a hub and three twisted blades, on the far end of the torpedo.
            Vector3 hub = new(0f, footY + 0.03f, length * 0.1f + length * 0.5f);
            Piece("Propeller hub", root, Turned("CHEngineHub", R(0f, 0.004f, 0.004f), R(0.012f, 0.02f, 0.02f), R(0.045f, 0.018f, 0.018f), R(0.06f, 0.004f, 0.004f)), steel, hub - Vector3.forward * 0.01f, new Vector3(90f, 0f, 0f));
            Mesh blade = Turned("CHEngineBlade", R(0f, 0.008f, 0.003f), R(0.03f, 0.022f, 0.005f), R(Outboard.BladeLength * 0.5f, Outboard.BladeWidth, 0.006f), R(Outboard.BladeLength * 0.85f, 0.03f, 0.004f), R(Outboard.BladeLength, 0.004f, 0.002f));
            for (int i = 0; i < 3; i++) {
                var spoke = new GameObject($"Blade {i + 1}").transform;
                spoke.SetParent(root, false);
                spoke.localPosition = hub;
                spoke.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
                Piece("Blade", spoke, blade, steel, Vector3.zero, new Vector3(0f, Outboard.BladeTwist, 0f));
            }
            // The tiller arm, running forward over the stern, and the clamp that holds all of it to the transom.
            Piece("Tiller arm", root, Turned("CHEngineTiller", R(0f, 0.014f, 0.014f), R(0.55f, 0.011f, 0.011f), R(0.6f, 0.022f, 0.022f), R(0.78f, 0.022f, 0.022f), R(0.8f, 0f, 0f)), rubber, new Vector3(0f, Bottom + 0.22f, 0f), new Vector3(-80f, 0f, 0f));
            Piece("Clamp", root, Turned("CHEngineClamp", R(0f, 0.09f, 0.025f), R(0.1f, 0.095f, 0.03f), R(0.2f, 0.09f, 0.025f)), steel, new Vector3(0f, Bottom - 0.1f, -0.15f));
            // The cord: out of the side of the cowling, hanging in two slack runs to a bright grip.
            Vector3 anchor = new(0.19f, Bottom + 0.4f, 0.04f);
            Mesh run = Turned("CHEngineCord", R(0f, Outboard.CordRadius, Outboard.CordRadius), R(Outboard.CordLength * 0.5f, Outboard.CordRadius, Outboard.CordRadius), R(Outboard.CordLength, Outboard.CordRadius, Outboard.CordRadius));
            Piece("Cord upper", root, run, rubber, anchor, new Vector3(0f, 0f, -28f));
            Vector3 elbow = anchor + Quaternion.Euler(0f, 0f, -28f) * (Vector3.up * Outboard.CordLength);
            Piece("Cord lower", root, run, rubber, elbow, new Vector3(0f, 0f, 168f), new Vector3(1f, 0.6f, 1f));
            Piece("Cord grip", root, Turned("CHEngineGrip", R(0f, 0.006f, 0.006f), R(0.008f, Outboard.GripRadius * 0.9f, Outboard.GripRadius * 0.9f), R(Outboard.GripLength * 0.5f, Outboard.GripRadius, Outboard.GripRadius),
                R(Outboard.GripLength - 0.008f, Outboard.GripRadius * 0.9f, Outboard.GripRadius * 0.9f), R(Outboard.GripLength, 0.006f, 0.006f)), grip,
                elbow + Quaternion.Euler(0f, 0f, 168f) * (Vector3.up * Outboard.CordLength * 0.6f), new Vector3(0f, 0f, 90f));
            return root.gameObject;
        }

        /// <summary>
        /// The chains and three padlocks holding the bell to the altar: two straps of links crossed over where the bell
        /// stands, one lock at each end and one where they cross. The client hides them when the third lock is picked.
        /// </summary>
        internal static GameObject BuildBellChains(Transform parent, Vector3 altarTop, Vector3 nave) {
            var root = new GameObject("Bell chains").transform;
            root.SetParent(parent, false);
            nave.y = 0f;
            root.SetPositionAndRotation(altarTop, Quaternion.LookRotation(nave.sqrMagnitude > 1e-4f ? nave : Vector3.forward));
            Material iron = Metal("ChainIron", ChainIron, 0.85f, 0.4f), brass = Metal("LockBrass", LockBrass, 0.9f, 0.6f);
            Mesh link = LinkMesh("CHChainLink", BellLocks.LinkLength, BellLocks.LinkWidth, BellLocks.LinkTube);
            const float Reach = BellLocks.BellRadius + BellLocks.Standoff, Rise = BellLocks.Height + BellLocks.Standoff;
            // Each strap is a half ellipse over the bell; links alternate a quarter turn about the strap's direction.
            for (int strap = 0; strap < 2; strap++) {
                var arc = new GameObject($"Strap {strap + 1}").transform;
                arc.SetParent(root, false);
                arc.localEulerAngles = new Vector3(0f, strap == 0 ? 40f : -40f, 0f);
                for (int i = 0; i < BellLocks.Links; i++) {
                    float t = (i + 0.5f) / BellLocks.Links * Mathf.PI;
                    Vector3 at = new(Mathf.Cos(t) * Reach, 0.03f + Mathf.Sin(t) * Rise, 0f);
                    Vector3 along = new Vector3(-Mathf.Sin(t) * Reach, Mathf.Cos(t) * Rise, 0f).normalized;
                    float roll = i % 2 == 0 ? 0f : 90f;
                    Quaternion face = Quaternion.FromToRotation(Vector3.up, along) * Quaternion.AngleAxis(roll, Vector3.up);
                    Piece($"Link {i + 1}", arc, link, iron, at, face.eulerAngles);
                }
            }
            // Three padlocks: at each end of the straps and on top where they cross.
            Mesh body = Turned("CHPadlockBody", new Vector3(0f, 0.045f, 0.02f), new Vector3(0.012f, BellLocks.PadlockWidth * 0.5f, BellLocks.PadlockDepth * 0.5f),
                new Vector3(BellLocks.PadlockHeight * 0.85f, BellLocks.PadlockWidth * 0.5f, BellLocks.PadlockDepth * 0.5f), new Vector3(BellLocks.PadlockHeight, 0.045f, 0.02f));
            Mesh shackle = LinkMesh("CHPadlockShackle", 0.012f, BellLocks.ShackleRadius * 2f, BellLocks.ShackleTube);
            Mesh keyhole = Turned("CHPadlockKeyhole", new Vector3(0f, 0.008f, 0.008f), new Vector3(0.003f, 0.008f, 0.008f), new Vector3(0.006f, 0.003f, 0.003f));
            Vector3[] lockAt = { new(Reach + 0.03f, 0.02f, 0f), new(-(Reach + 0.03f), 0.02f, 0f), new(0f, 0.03f + Rise - 0.02f, 0.07f) };
            Material hole = Metal("LockKeyhole", Color.black, 0f, 0.1f);
            for (int i = 0; i < lockAt.Length; i++) {
                var padlock = new GameObject($"Padlock {i + 1}").transform;
                padlock.SetParent(root, false);
                padlock.localPosition = lockAt[i];
                                Piece("Body", padlock, body, brass, Vector3.zero);
                Piece("Shackle", padlock, shackle, iron, new Vector3(0f, BellLocks.PadlockHeight + 0.018f, 0f));
                Piece("Keyhole", padlock, keyhole, hole, new Vector3(0f, BellLocks.PadlockHeight * 0.45f, BellLocks.PadlockDepth * 0.5f), new Vector3(90f, 0f, 0f));
            }
            return root.gameObject;
        }
    }
}
