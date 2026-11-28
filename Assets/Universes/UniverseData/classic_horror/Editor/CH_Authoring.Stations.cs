using UnityEngine;

namespace Universes.UniverseData.classic_horror.Editor {
    public static partial class CH_Authoring {
        static readonly Color EngineMetal = new(0.09f, 0.1f, 0.11f), EngineCowl = new(0.32f, 0.07f, 0.05f), CordGrip = new(0.9f, 0.55f, 0.15f);
        static readonly Color ChainIron = new(0.1f, 0.1f, 0.11f), LockBrass = new(0.55f, 0.4f, 0.16f);

        /// <summary>
        /// The ferry's outboard engine, bolted to the stern: a cowling, a leg down into the water and a pull-cord with a
        /// bright grip, so the first minigame has something to be done to. It is a child of the ferry and sails with it.
        /// </summary>
        internal static GameObject BuildEngineProp(Transform ferry, Vector3 stern, Vector3 outward) {
            var root = new GameObject("Outboard engine").transform;
            root.SetParent(ferry, true);
            root.position = stern;
            outward.y = 0f;
            root.rotation = Quaternion.LookRotation(outward.sqrMagnitude > 1e-4f ? outward : Vector3.forward);
            Material metal = Material("EngineMetal", EngineMetal), cowl = Material("EngineCowl", EngineCowl), grip = Material("EngineCord", CordGrip, true);
            Shape("Cowling", PrimitiveType.Cube, root, new Vector3(0f, 0.55f, 0f), new Vector3(0.5f, 0.5f, 0.62f), cowl);
            Shape("Cowling cap", PrimitiveType.Cylinder, root, new Vector3(0f, 0.84f, 0f), new Vector3(0.44f, 0.05f, 0.44f), metal);
            Shape("Leg", PrimitiveType.Cube, root, new Vector3(0f, 0.05f, -0.06f), new Vector3(0.14f, 0.95f, 0.22f), metal);
            Shape("Gearbox", PrimitiveType.Cylinder, root, new Vector3(0f, -0.42f, -0.06f), new Vector3(0.2f, 0.1f, 0.2f), metal, new Vector3(90f, 0f, 0f));
            Shape("Cord", PrimitiveType.Cylinder, root, new Vector3(0.26f, 0.52f, 0.06f), new Vector3(0.015f, 0.14f, 0.015f), metal, new Vector3(0f, 0f, 70f));
            Shape("Cord grip", PrimitiveType.Capsule, root, new Vector3(0.44f, 0.47f, 0.06f), new Vector3(0.07f, 0.09f, 0.07f), grip, new Vector3(0f, 0f, 70f));
            return root.gameObject;
        }

        /// <summary>
        /// The chains and three padlocks holding the bell to the altar: two heavy straps crossed over where the bell stands
        /// and a lock on each end and in the middle. The client hides them when the third lock is picked.
        /// </summary>
        internal static GameObject BuildBellChains(Transform parent, Vector3 altarTop, Vector3 nave) {
            var root = new GameObject("Bell chains").transform;
            root.SetParent(parent, false);
            nave.y = 0f;
            root.SetPositionAndRotation(altarTop, Quaternion.LookRotation(nave.sqrMagnitude > 1e-4f ? nave : Vector3.forward));
            Material iron = Material("ChainIron", ChainIron), brass = Material("LockBrass", LockBrass, true);
            iron.SetFloat("_Metallic", 0.85f);
            iron.SetFloat("_Smoothness", 0.35f);
            const float BellRadius = 0.26f, Height = 0.3f;
            // Two straps crossed over the bell, links as short capsules strung along each.
            for (int strap = 0; strap < 2; strap++) {
                float yaw = strap == 0 ? 35f : -35f;
                var arc = new GameObject($"Strap {strap + 1}").transform;
                arc.SetParent(root, false);
                arc.localEulerAngles = new Vector3(0f, yaw, 0f);
                const int Links = 11;
                for (int link = 0; link < Links; link++) {
                    float t = link / (Links - 1f) * Mathf.PI;
                    Vector3 at = new(Mathf.Cos(t) * (BellRadius + 0.05f), 0.04f + Mathf.Sin(t) * (Height + 0.08f), 0f);
                    Vector3 along = new(-Mathf.Sin(t), Mathf.Cos(t), 0f);
                    GameObject shape = Shape($"Link {link + 1}", PrimitiveType.Capsule, arc, at, new Vector3(0.05f, 0.045f, 0.05f), iron,
                        new Vector3(0f, 0f, Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg - 90f));
                    if (link % 2 == 1) shape.transform.Rotate(0f, 90f, 0f, Space.Self);
                }
            }
            // Three padlocks: one at each end of the straps and one on top where they cross.
            Vector3[] lockAt = { new(BellRadius + 0.06f, 0.1f, 0.0f), new(-(BellRadius + 0.06f), 0.1f, 0.0f), new(0f, Height + 0.14f, 0f) };
            for (int i = 0; i < lockAt.Length; i++) {
                Shape($"Padlock {i + 1}", PrimitiveType.Cube, root, lockAt[i], new Vector3(0.1f, 0.12f, 0.06f), brass);
                Shape($"Shackle {i + 1}", PrimitiveType.Cylinder, root, lockAt[i] + Vector3.up * 0.09f, new Vector3(0.055f, 0.03f, 0.02f), iron, new Vector3(0f, 0f, 90f));
            }
            return root.gameObject;
        }
    }
}
