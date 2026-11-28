using System.Linq;
using RyanAssets.Platformer;
using RyanAssets.Rounds;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static RyanAssets.Authoring.UniverseAuthoringKit;

namespace Universes.UniverseData.tycoon.Editor {
    /// <summary>
    /// Brick Buddies: the pet crate and one blocky model per pet of <see cref="TY_Pets.All"/>, built from
    /// collider-free primitives in each pet's colours (rarer pets glow a little). Every pet faces +Z with its
    /// feet on the origin, about knee-high to a player. "Build Pets" rebuilds only these and hooks them into
    /// tycoon_start's TY_Map, so it never regenerates the HUD; the full client build calls the same code.
    /// </summary>
    public static partial class TY_Authoring {
        [MenuItem("Ryan/Tycoon/Build Pets and Crate")]
        public static void BuildPets() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            Kit.EnsureFolders("Prefabs", "Prefabs/Pets", "Materials");
            var crate = Crate();
            var pets = PetPrefabs();
            var scene = EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
            var map = Object.FindAnyObjectByType<TY_Map>();
            if (map == null) throw new System.InvalidOperationException("tycoon_start has no TY_Map: run Build Client Assets.");
            map.cratePrefab = crate;
            map.petPrefabs = pets;
            EditorUtility.SetDirty(map);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Tycoon] Pets built.");
        }

        static GameObject[] PetPrefabs() => Enumerable.Range(0, TY_Pets.Count).Select(Pet).ToArray();

        // ---------------------------------------------------------------- crate
        /// <summary>A brick crate with gold bands and a "?" on every side, on a pad, with a sign the client fills in.</summary>
        static GameObject Crate() {
            var root = new GameObject("PetCrate");
            var view = root.AddComponent<TY_CrateView>();
            Color purple = TY_Pets.RarityColor(TY_Rarity.Epic);
            var brick = Tiled(Kit.Mat("CrateBrick", new Color(0.78f, 0.36f, 0.25f), Kit.Tex("BrickFace"), smoothness: 0.2f), 3f, 2f);
            var band = Kit.Mat("CrateBand", new Color(1f, 0.78f, 0.2f), emission: 0.35f, smoothness: 0.85f, metallic: 0.9f);
            var pad = Kit.Mat("CratePad", purple, Kit.Tex("Tile"), 0.6f, smoothness: 0.5f);
            NoShadows(Cube("Pad", root.transform, new Vector3(0f, 0.05f, 0f), new Vector3(TY_Catalog.ChestPadSize, 0.1f, TY_Catalog.ChestPadSize), pad, false));

            var body = Empty("Body", root.transform, new Vector3(0f, 0.1f, 0f));
            view.body = body.transform;
            Bit(PrimitiveType.Cube, "Box", body.transform, new Vector3(0f, 0.55f, 0f), new Vector3(1.5f, 1.1f, 1.5f), brick);
            foreach (float y in new[] { 0.15f, 0.95f })
                Bit(PrimitiveType.Cube, "Band", body.transform, new Vector3(0f, y, 0f), new Vector3(1.56f, 0.12f, 1.56f), band);
            for (int side = 0; side < 4; side++) {
                float yaw = side * 90f;
                Vector3 normal = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
                var mark = WorldText("Mark", body.transform, new Vector3(0f, 0.58f, 0f) + normal * 0.76f, "?", 6f, new Color(1f, 0.88f, 0.4f), new Vector2(1f, 1f), yaw);
                mark.fontStyle = FontStyles.Bold;
            }
            var lid = Empty("Lid", body.transform, new Vector3(0f, 1.18f, 0f));
            view.lid = lid.transform;
            Bit(PrimitiveType.Cube, "Top", lid.transform, Vector3.zero, new Vector3(1.66f, 0.18f, 1.66f), brick);
            Bit(PrimitiveType.Cube, "Knob", lid.transform, new Vector3(0f, 0.16f, 0f), new Vector3(0.36f, 0.16f, 0.36f), band);

            var glow = Empty("Glow", root.transform, new Vector3(0f, 1.7f, 0f));
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point; light.color = purple; light.range = 5f; light.intensity = 2.5f; light.shadows = LightShadows.None;
            var sparkle = Empty("Sparkle", glow.transform, new Vector3(0f, 0.3f, 0f));
            sparkle.AddComponent<Spinner>().degreesPerSecond = new Vector3(0f, 90f, 0f);
            Bit(PrimitiveType.Cube, "Gem", sparkle.transform, Vector3.zero, Vector3.one * 0.3f, Kit.Mat("CrateGem", purple, emission: 2f, smoothness: 0.9f))
                .transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            view.readyGlow = glow;

            // Mirrors the chest's sign on the other side of the spawn point (+x), at the same height.
            Transform sign = DarkSign(root.transform, new Vector3(0.4f, 2.85f, 0f), new Vector2(3.4f, 0.95f), purple);
            view.label = WorldText("Label", sign, new Vector3(0f, -0.05f, -0.02f), "PET CRATE", 2.2f, Color.white, new Vector2(3.2f, 0.85f));
            view.label.textWrappingMode = TextWrappingModes.NoWrap;
            var font = TY_HudAuthoring.Font(true);
            if (font != null) view.label.font = font;
            ((TextMeshPro)view.label).sortingOrder = 3;
            return Kit.Save(root, "", "PetCrate");
        }

        // ---------------------------------------------------------------- pets
        /// <summary>Materials of one pet: body, accent, eyes and the eye shine.</summary>
        readonly struct PetMats {
            public readonly Material body, accent, eye, shine;
            public PetMats(Material body, Material accent, Material eye, Material shine) { this.body = body; this.accent = accent; this.eye = eye; this.shine = shine; }
        }

        static GameObject Pet(int index) {
            TY_Pet pet = TY_Pets.All[index];
            string id = pet.name.Replace(" ", "");
            // Rarer pets shine: a little glow from Epic, and the Golden Piggy is real gold.
            float glow = pet.rarity >= TY_Rarity.Legendary ? 0.35f : pet.rarity >= TY_Rarity.Epic ? 0.18f : 0f;
            bool metal = pet.shape is TY_PetShape.Pig or TY_PetShape.Mole;
            var m = new PetMats(
                Kit.Mat($"Pet{id}Body", pet.body, emission: glow, smoothness: metal ? 0.85f : 0.35f, metallic: metal ? 0.75f : 0f),
                Kit.Mat($"Pet{id}Accent", pet.accent, emission: pet.shape == TY_PetShape.Mole ? 2f : glow, smoothness: 0.5f),
                Kit.Mat("PetEye", new Color(0.06f, 0.06f, 0.08f), smoothness: 0.95f),
                Kit.Mat("PetShine", Color.white, emission: 0.8f));
            var root = new GameObject(id);
            var model = Empty("Model", root.transform);
            switch (pet.shape) {
                case TY_PetShape.Dog: Dog(model.transform, m); break;
                case TY_PetShape.Cat: Cat(model.transform, m); break;
                case TY_PetShape.Duck: Duck(model.transform, m); break;
                case TY_PetShape.Bunny: Bunny(model.transform, m); break;
                case TY_PetShape.Fox: Fox(model.transform, m); break;
                case TY_PetShape.Owl: Owl(model.transform, m); break;
                case TY_PetShape.Pig: Pig(model.transform, m); break;
                case TY_PetShape.Mole: Mole(model.transform, m); break;
                case TY_PetShape.Dragon: Dragon(model.transform, m); break;
                default: Unicorn(model.transform, m); break;
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return Kit.Save(root, "Pets", id);
        }

        static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, Vector3 euler = default) {
            var go = Bit(PrimitiveType.Cube, name, parent, position, size, material);
            go.transform.localRotation = Quaternion.Euler(euler);
            return go;
        }

        /// <summary>Two eyes with a shine on the front (+Z) face of a head centred at <paramref name="head"/>.</summary>
        static void Eyes(Transform parent, Vector3 head, float headHalf, float spacing, float size, PetMats m) {
            for (int s = -1; s <= 1; s += 2) {
                Vector3 eye = head + new Vector3(s * spacing, headHalf * 0.25f, headHalf + 0.005f);
                Box(parent, "Eye", eye, new Vector3(size, size * 1.2f, 0.03f), m.eye);
                Box(parent, "Shine", eye + new Vector3(size * 0.22f, size * 0.3f, 0.02f), Vector3.one * size * 0.35f, m.shine);
            }
        }

        /// <summary>A four-legged body; returns the head centre. Legs are <paramref name="legH"/> tall under a body of <paramref name="body"/>.</summary>
        static Vector3 FourLegs(Transform p, Vector3 body, float legH, float head, Material bodyMat, Material legMat) {
            Box(p, "Body", new Vector3(0f, legH + body.y * 0.5f, 0f), body, bodyMat);
            float lx = body.x * 0.32f, lz = body.z * 0.34f, leg = Mathf.Min(body.x, body.z) * 0.28f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(p, "Leg", new Vector3(sx * lx, legH * 0.5f, sz * lz), new Vector3(leg, legH, leg), legMat);
            return new Vector3(0f, legH + body.y + head * 0.3f, body.z * 0.5f + head * 0.25f);
        }

        static void Dog(Transform p, PetMats m) {
            Vector3 h = FourLegs(p, new Vector3(0.42f, 0.34f, 0.66f), 0.22f, 0.42f, m.body, m.body);
            Box(p, "Head", h, Vector3.one * 0.42f, m.body);
            Box(p, "Snout", h + new Vector3(0f, -0.08f, 0.26f), new Vector3(0.24f, 0.18f, 0.16f), m.accent);
            Box(p, "Nose", h + new Vector3(0f, -0.02f, 0.35f), new Vector3(0.08f, 0.06f, 0.04f), m.eye);
            for (int s = -1; s <= 1; s += 2) Box(p, "Ear", h + new Vector3(s * 0.24f, 0.04f, -0.02f), new Vector3(0.07f, 0.28f, 0.18f), m.accent, new Vector3(0f, 0f, s * 12f));
            Box(p, "Tail", new Vector3(0f, 0.68f, -0.38f), new Vector3(0.08f, 0.26f, 0.08f), m.body, new Vector3(-35f, 0f, 0f));
            Box(p, "Collar", h + new Vector3(0f, -0.25f, -0.1f), new Vector3(0.44f, 0.07f, 0.3f), Kit.Mat("PetCollar", new Color(0.3f, 0.6f, 1f)));
            Eyes(p, h, 0.21f, 0.11f, 0.08f, m);
        }

        static void Cat(Transform p, PetMats m) {
            Vector3 h = FourLegs(p, new Vector3(0.36f, 0.3f, 0.6f), 0.2f, 0.38f, m.body, m.body);
            Box(p, "Head", h, new Vector3(0.4f, 0.34f, 0.34f), m.body);
            Box(p, "Muzzle", h + new Vector3(0f, -0.08f, 0.18f), new Vector3(0.2f, 0.12f, 0.06f), m.accent);
            for (int s = -1; s <= 1; s += 2) {
                Box(p, "Ear", h + new Vector3(s * 0.13f, 0.2f, 0f), new Vector3(0.12f, 0.12f, 0.06f), m.body, new Vector3(0f, 0f, 45f));
                Box(p, "Whisker", h + new Vector3(s * 0.17f, -0.07f, 0.18f), new Vector3(0.14f, 0.012f, 0.012f), m.eye);
            }
            Box(p, "Belly", new Vector3(0f, 0.22f, 0.02f), new Vector3(0.26f, 0.04f, 0.4f), m.accent);
            Box(p, "Tail1", new Vector3(0f, 0.55f, -0.36f), new Vector3(0.07f, 0.32f, 0.07f), m.body, new Vector3(-20f, 0f, 0f));
            Box(p, "Tail2", new Vector3(0f, 0.76f, -0.38f), new Vector3(0.07f, 0.16f, 0.07f), m.accent, new Vector3(25f, 0f, 0f));
            Eyes(p, h, 0.17f, 0.09f, 0.08f, m);
        }

        static void Duck(Transform p, PetMats m) {
            var feet = m.accent;
            for (int s = -1; s <= 1; s += 2) {
                Box(p, "Leg", new Vector3(s * 0.1f, 0.08f, 0f), new Vector3(0.05f, 0.16f, 0.05f), feet);
                Box(p, "Foot", new Vector3(s * 0.1f, 0.015f, 0.06f), new Vector3(0.12f, 0.03f, 0.16f), feet);
            }
            Box(p, "Body", new Vector3(0f, 0.34f, 0f), new Vector3(0.44f, 0.36f, 0.56f), m.body);
            Box(p, "TailTip", new Vector3(0f, 0.46f, -0.3f), new Vector3(0.24f, 0.14f, 0.12f), m.body, new Vector3(-30f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2) Box(p, "Wing", new Vector3(s * 0.24f, 0.38f, -0.04f), new Vector3(0.05f, 0.22f, 0.36f), m.body, new Vector3(-10f, 0f, 0f));
            Vector3 h = new(0f, 0.7f, 0.16f);
            Box(p, "Head", h, new Vector3(0.3f, 0.3f, 0.3f), m.body);
            Box(p, "Beak", h + new Vector3(0f, -0.04f, 0.2f), new Vector3(0.2f, 0.07f, 0.16f), m.accent);
            Eyes(p, h, 0.15f, 0.08f, 0.06f, m);
        }

        static void Bunny(Transform p, PetMats m) {
            Box(p, "Body", new Vector3(0f, 0.24f, -0.04f), new Vector3(0.38f, 0.36f, 0.46f), m.body);
            for (int s = -1; s <= 1; s += 2) {
                Box(p, "Foot", new Vector3(s * 0.12f, 0.04f, 0.12f), new Vector3(0.1f, 0.08f, 0.2f), m.body);
                Box(p, "Ear", new Vector3(s * 0.08f, 0.84f, 0.06f), new Vector3(0.08f, 0.36f, 0.12f), m.body, new Vector3(-8f, 0f, s * 8f));
                Box(p, "EarInner", new Vector3(s * 0.08f, 0.84f, 0.125f), new Vector3(0.05f, 0.28f, 0.02f), m.accent, new Vector3(-8f, 0f, s * 8f));
            }
            Vector3 h = new(0f, 0.52f, 0.1f);
            Box(p, "Head", h, new Vector3(0.32f, 0.28f, 0.28f), m.body);
            Box(p, "Nose", h + new Vector3(0f, -0.03f, 0.145f), new Vector3(0.06f, 0.04f, 0.02f), m.accent);
            Bit(PrimitiveType.Sphere, "Tail", p, new Vector3(0f, 0.22f, -0.3f), Vector3.one * 0.16f, Kit.Mat("PetFluff", new Color(0.97f, 0.97f, 1f)));
            Eyes(p, h, 0.14f, 0.08f, 0.06f, m);
        }

        static void Fox(Transform p, PetMats m) {
            Vector3 h = FourLegs(p, new Vector3(0.36f, 0.3f, 0.66f), 0.24f, 0.38f, m.body, m.eye);
            Box(p, "Head", h, new Vector3(0.38f, 0.32f, 0.34f), m.body);
            Box(p, "Snout", h + new Vector3(0f, -0.07f, 0.23f), new Vector3(0.16f, 0.12f, 0.16f), m.accent);
            Box(p, "Nose", h + new Vector3(0f, -0.04f, 0.32f), new Vector3(0.06f, 0.05f, 0.03f), m.eye);
            Box(p, "Chest", new Vector3(0f, 0.42f, 0.28f), new Vector3(0.24f, 0.22f, 0.12f), m.accent);
            for (int s = -1; s <= 1; s += 2) Box(p, "Ear", h + new Vector3(s * 0.12f, 0.22f, -0.02f), new Vector3(0.12f, 0.16f, 0.06f), m.body, new Vector3(0f, 0f, s * -12f));
            Box(p, "Tail", new Vector3(0f, 0.52f, -0.48f), new Vector3(0.2f, 0.2f, 0.42f), m.body, new Vector3(25f, 0f, 0f));
            Box(p, "TailTip", new Vector3(0f, 0.62f, -0.69f), new Vector3(0.18f, 0.18f, 0.12f), m.accent, new Vector3(25f, 0f, 0f));
            Eyes(p, h, 0.17f, 0.09f, 0.07f, m);
        }

        static void Owl(Transform p, PetMats m) {
            var claw = Kit.Mat("PetClaw", new Color(1f, 0.7f, 0.2f));
            for (int s = -1; s <= 1; s += 2) Box(p, "Foot", new Vector3(s * 0.1f, 0.03f, 0.08f), new Vector3(0.1f, 0.06f, 0.12f), claw);
            Box(p, "Body", new Vector3(0f, 0.36f, 0f), new Vector3(0.46f, 0.6f, 0.4f), m.body);
            Box(p, "Belly", new Vector3(0f, 0.3f, 0.2f), new Vector3(0.3f, 0.36f, 0.02f), Kit.Mat("PetOwlBelly", new Color(0.85f, 0.86f, 0.9f)));
            for (int s = -1; s <= 1; s += 2) {
                Box(p, "Wing", new Vector3(s * 0.25f, 0.36f, -0.02f), new Vector3(0.06f, 0.42f, 0.32f), m.body, new Vector3(0f, 0f, s * -6f));
                Box(p, "Tuft", new Vector3(s * 0.17f, 0.72f, 0f), new Vector3(0.08f, 0.14f, 0.08f), m.body, new Vector3(0f, 0f, s * -20f));
                // Big round owl eyes: an accent disc, then the pupil.
                Bit(PrimitiveType.Cylinder, "EyeRing", p, new Vector3(s * 0.11f, 0.53f, 0.2f), new Vector3(0.17f, 0.01f, 0.17f), m.accent).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Box(p, "Pupil", new Vector3(s * 0.11f, 0.53f, 0.215f), new Vector3(0.07f, 0.07f, 0.02f), m.eye);
                Box(p, "Shine", new Vector3(s * 0.11f + 0.02f, 0.555f, 0.225f), Vector3.one * 0.025f, m.shine);
            }
            Box(p, "Beak", new Vector3(0f, 0.45f, 0.22f), new Vector3(0.06f, 0.1f, 0.06f), claw, new Vector3(20f, 0f, 0f));
        }

        static void Pig(Transform p, PetMats m) {
            Vector3 h = FourLegs(p, new Vector3(0.5f, 0.42f, 0.62f), 0.16f, 0.4f, m.body, m.body);
            h += new Vector3(0f, -0.12f, 0f);
            Box(p, "Head", h, new Vector3(0.42f, 0.38f, 0.32f), m.body);
            Box(p, "Snout", h + new Vector3(0f, -0.05f, 0.19f), new Vector3(0.2f, 0.14f, 0.08f), m.accent);
            for (int s = -1; s <= 1; s += 2) {
                Box(p, "Nostril", h + new Vector3(s * 0.045f, -0.05f, 0.235f), new Vector3(0.035f, 0.05f, 0.01f), m.eye);
                Box(p, "Ear", h + new Vector3(s * 0.15f, 0.21f, 0f), new Vector3(0.12f, 0.08f, 0.1f), m.accent, new Vector3(0f, 0f, s * -25f));
            }
            Box(p, "Coin slot", new Vector3(0f, 0.585f, -0.05f), new Vector3(0.05f, 0.02f, 0.24f), m.eye);
            Box(p, "Tail", new Vector3(0f, 0.44f, -0.34f), new Vector3(0.06f, 0.06f, 0.1f), m.accent, new Vector3(0f, 45f, 45f));
            Eyes(p, h, 0.16f, 0.11f, 0.06f, m);
        }

        static void Mole(Transform p, PetMats m) {
            var tread = Kit.Mat("PetTread", new Color(0.18f, 0.19f, 0.22f), smoothness: 0.3f);
            for (int s = -1; s <= 1; s += 2) Box(p, "Tread", new Vector3(s * 0.22f, 0.09f, 0f), new Vector3(0.12f, 0.18f, 0.56f), tread);
            Box(p, "Body", new Vector3(0f, 0.36f, -0.02f), new Vector3(0.42f, 0.34f, 0.54f), m.body);
            Vector3 h = new(0f, 0.42f, 0.3f);
            Box(p, "Head", h, new Vector3(0.34f, 0.28f, 0.16f), m.body);
            Box(p, "Visor", h + new Vector3(0f, 0.05f, 0.085f), new Vector3(0.26f, 0.08f, 0.02f), m.accent);
            // The drill nose: a cone of shrinking slices.
            for (int i = 0; i < 4; i++) {
                float w = 0.16f - i * 0.035f;
                Bit(PrimitiveType.Cylinder, "Drill", p, h + new Vector3(0f, -0.05f, 0.12f + i * 0.05f), new Vector3(w, 0.025f, w), Kit.Mat("PetDrill", new Color(1f, 0.78f, 0.2f), smoothness: 0.9f, metallic: 0.9f))
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            Box(p, "Antenna", new Vector3(0.12f, 0.66f, -0.1f), new Vector3(0.025f, 0.24f, 0.025f), m.body);
            Box(p, "AntennaTip", new Vector3(0.12f, 0.79f, -0.1f), Vector3.one * 0.06f, m.accent);
            for (int s = -1; s <= 1; s += 2) Box(p, "Claw", new Vector3(s * 0.26f, 0.3f, 0.26f), new Vector3(0.08f, 0.06f, 0.14f), m.accent);
        }

        static void Dragon(Transform p, PetMats m) {
            Vector3 h = FourLegs(p, new Vector3(0.42f, 0.36f, 0.68f), 0.22f, 0.42f, m.body, m.body);
            h += new Vector3(0f, 0.1f, 0.06f);
            Box(p, "Neck", new Vector3(0f, 0.68f, 0.3f), new Vector3(0.2f, 0.3f, 0.2f), m.body, new Vector3(25f, 0f, 0f));
            Box(p, "Head", h, new Vector3(0.36f, 0.3f, 0.38f), m.body);
            Box(p, "Jaw", h + new Vector3(0f, -0.1f, 0.22f), new Vector3(0.26f, 0.12f, 0.2f), m.body);
            for (int s = -1; s <= 1; s += 2) {
                Box(p, "Horn", h + new Vector3(s * 0.12f, 0.2f, -0.12f), new Vector3(0.06f, 0.2f, 0.06f), m.accent, new Vector3(-30f, 0f, s * -10f));
                Box(p, "Nostril", h + new Vector3(s * 0.06f, -0.06f, 0.32f), new Vector3(0.03f, 0.03f, 0.01f), m.eye);
                // Wings: a gold arm and a membrane, raised and swept back.
                var wing = Empty("Wing", p, new Vector3(s * 0.22f, 0.62f, 0.02f));
                wing.transform.localRotation = Quaternion.Euler(0f, s * -25f, s * -35f);
                Box(wing.transform, "Arm", new Vector3(s * 0.25f, 0f, 0f), new Vector3(0.5f, 0.05f, 0.05f), m.accent);
                Box(wing.transform, "Membrane", new Vector3(s * 0.24f, -0.02f, -0.15f), new Vector3(0.46f, 0.02f, 0.3f), m.body);
            }
            for (int i = 0; i < 3; i++) {
                Box(p, "Tail", new Vector3(0f, 0.42f - i * 0.06f, -0.4f - i * 0.15f), Vector3.one * (0.18f - i * 0.04f), m.body);
                Box(p, "Spike", new Vector3(0f, 0.62f, 0.2f - i * 0.2f), new Vector3(0.04f, 0.1f, 0.1f), m.accent, new Vector3(45f, 0f, 0f));
            }
            Box(p, "Belly", new Vector3(0f, 0.21f, 0f), new Vector3(0.3f, 0.03f, 0.5f), m.accent);
            Eyes(p, h, 0.19f, 0.1f, 0.07f, m);
        }

        static void Unicorn(Transform p, PetMats m) {
            Vector3 h = FourLegs(p, new Vector3(0.38f, 0.34f, 0.66f), 0.3f, 0.4f, m.body, m.body);
            h += new Vector3(0f, 0.12f, 0.04f);
            var hoof = Kit.Mat("PetHoof", new Color(1f, 0.82f, 0.35f), emission: 0.3f, metallic: 0.8f, smoothness: 0.85f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(p, "Hoof", new Vector3(sx * 0.12f, 0.03f, sz * 0.22f), new Vector3(0.12f, 0.06f, 0.12f), hoof);
            Box(p, "Neck", new Vector3(0f, 0.74f, 0.28f), new Vector3(0.2f, 0.32f, 0.2f), m.body, new Vector3(20f, 0f, 0f));
            Box(p, "Head", h, new Vector3(0.3f, 0.3f, 0.42f), m.body);
            Box(p, "Muzzle", h + new Vector3(0f, -0.06f, 0.2f), new Vector3(0.24f, 0.16f, 0.1f), Kit.Mat("PetMuzzle", new Color(1f, 0.86f, 0.9f)));
            Box(p, "Horn", h + new Vector3(0f, 0.27f, 0.1f), new Vector3(0.06f, 0.28f, 0.06f), hoof, new Vector3(25f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2) Box(p, "Ear", h + new Vector3(s * 0.1f, 0.19f, -0.1f), new Vector3(0.06f, 0.12f, 0.05f), m.body);
            // A mane of accent blocks down the neck, and a long tail.
            for (int i = 0; i < 4; i++) Box(p, "Mane", new Vector3(0f, 0.96f - i * 0.1f, 0.16f - i * 0.06f), new Vector3(0.1f, 0.12f, 0.12f), m.accent);
            Box(p, "Tail", new Vector3(0f, 0.5f, -0.4f), new Vector3(0.12f, 0.36f, 0.12f), m.accent, new Vector3(-20f, 0f, 0f));
            Eyes(p, h, 0.21f, 0.1f, 0.06f, m);
        }
    }
}
