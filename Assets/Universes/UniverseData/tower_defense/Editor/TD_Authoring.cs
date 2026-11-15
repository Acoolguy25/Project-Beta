using System.IO;
using System.Linq;
using RyanAssets.Authoring;
using RyanAssets.Rounds;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static RyanAssets.Authoring.UniverseAuthoringKit;

namespace Universes.UniverseData.tower_defense.Editor {
    /// <summary>
    /// Rebuilds Tower Defense: materials, tower / enemy / pad / effect prefabs (Cartoon Military
    /// models on military helipads), the HUD (<see cref="TD_HudAuthoring"/>), the map
    /// (<see cref="TD_MapAuthoring"/>) and the start scene. Safe to re-run.
    /// Server runner: run BuildServerAssets from the Linux Server profile.
    /// </summary>
    public static class TD_Authoring {
        public const string Universe = "tower_defense";
        public const string Root = "Assets/Universes/UniverseData/tower_defense";
        public const string IconFolder = Root + "/UI/Icons";
        internal static readonly UniverseAuthoringKit Kit = new(Root);
        const string ServerPrefab = Root + "/Server/TD_ServerRunner.prefab";
        const string Military = "Assets/CartoonMilitaryModelPack/Prefebs/";
        const string Effects = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/";
        const string FireClip = "Packages/com.ryan.gameframework/Runtime/Tools/Audio/pistol-gun-shot.mp3";

        /// <summary>Turret model and plinth-relative size per tower, index-aligned with <see cref="TD_TowerKind"/>.</summary>
        static readonly (string model, float size)[] TowerModels = {
            ("Turret_01_Prefeb.prefab", 4.2f), ("Turret_05_Prefeb.prefab", 4.8f), ("Turret_08_Prefeb.prefab", 4.4f), ("Turret_10_Prefeb.prefab", 5.2f),
        };

        /// <summary>Vehicle model and size per enemy, index-aligned with <see cref="TD_EnemyKind"/>.</summary>
        static readonly (string model, float size)[] EnemyModels = {
            ("APC_Prefebs/APC_01_Prefeb.prefab", 3.6f), ("APC_Prefebs/APC_03_Prefeb.prefab", 4.6f), ("Tank_Prefebs/Tank_01_Prefeb.prefab", 5.2f),
            ("Chopper_Prefebs/Chopper_01_Prefeb.prefab", 6f), ("Tank_Prefebs/Tank_05_Prefeb.prefab", 8.5f),
        };

        /// <summary>Width of a tower pad (the helipad model) in metres.</summary>
        const float PadSize = 5.4f;

        [MenuItem("Ryan/Tower Defense/Build Client Assets")]
        public static void BuildClientAssets() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            Kit.EnsureFolders("Prefabs", "Prefabs/Towers", "Prefabs/Enemies", "Prefabs/Fx", "Materials", "Textures", "Meshes", "Server", "UI", "UI/Portraits");
            TD_HudAuthoring.ImportIcons();
            BuildTextures();
            BuildScene();
            RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));
            AssetDatabase.SaveAssets();
            Debug.Log("[TowerDefense] Client assets built.");
        }

        /// <summary>Batch entry point: builds everything, then (with a GPU) portraits and previews.</summary>
        public static void BuildClientAssetsBatch() {
            BuildClientAssets();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            TD_Portraits.RenderAll();
            BuildClientAssets(); // re-generate the HUD with the fresh portraits
            RenderPreviews();
        }

        [MenuItem("Ryan/Tower Defense/Build Server Assets (Linux Server profile)")]
        public static void BuildServerAssets() {
#if UNITY_SERVER
            Kit.EnsureFolders("Server");
            Kit.BuildServerRunner<Universes.UniverseData.tower_defense.Server.TD_ServerRunner>(ServerPrefab, Kit.ServerScene(Universe));
            RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));
            Debug.Log("[TowerDefense] Server assets built.");
#else
            throw new System.InvalidOperationException("Switch to the Linux Server build profile (UNITY_SERVER) to build the server runner prefab.");
#endif
        }

        // ---------------------------------------------------------------- textures + materials
        static void BuildTextures() {
            // Worn dirt road: warm sand with pebbles, darker wheel ruts and soft darker shoulders.
            Kit.SaveTexture("Road", (u, v) => {
                float grain = Hash(Mathf.Floor(u * 96f), Mathf.Floor(v * 96f));
                float pebble = Hash(Mathf.Floor(u * 24f) + 7f, Mathf.Floor(v * 24f)) > 0.93f ? 0.82f : 1f;
                float rut = Mathf.Abs(Mathf.Abs(u - 0.5f) - 0.2f) < 0.045f ? 0.9f : 1f;
                float shoulder = Mathf.SmoothStep(0.8f, 1f, Mathf.Abs(u - 0.5f) * 2f);
                float c = (0.92f + 0.08f * grain) * pebble * rut * (1f - 0.18f * shoulder);
                return new Color(0.86f * c, 0.7f * c, 0.5f * c);
            }, 256);
            Kit.SaveTexture("Blob", (u, v) => {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                return new Color(0f, 0f, 0f, Mathf.Clamp01(1f - d) * 0.55f);
            });
            Kit.SaveTexture("Hazard", (u, v) => Mathf.Repeat(u + v, 0.5f) < 0.25f ? new Color(1f, 0.8f, 0.15f) : new Color(0.12f, 0.12f, 0.14f));
            foreach (string name in new[] { "Blob" }) {
                var importer = (TextureImporter)AssetImporter.GetAtPath($"{Root}/Textures/{name}.png");
                if (importer == null || importer.alphaIsTransparency) continue;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        /// <summary>The KayKit atlas on URP Lit with a tint, used to pull the pack's lime grass toward a deeper green.</summary>
        internal static Material Tinted(string name, string atlasPath, Color tint) {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
            return Kit.Mat(name, tint, atlas, smoothness: 0.08f);
        }

        /// <summary>Unlit (optionally transparent) URP material: health bars, glows, icons on quads.</summary>
        internal static Material Unlit(string name, Color color, Texture texture = null, bool transparent = false, bool additive = false) {
            string path = $"{Root}/Materials/{name}.mat";
            var shader = Shader.Find(additive ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Unlit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", texture);
            bool blend = transparent || additive;
            mat.SetFloat("_Surface", blend ? 1f : 0f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", blend ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : blend ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", blend ? 0f : 1f);
            mat.SetOverrideTag("RenderType", blend ? "Transparent" : "Opaque");
            mat.renderQueue = blend ? (int)RenderQueue.Transparent : -1;
            if (blend) mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal sealed class Materials {
            public Material barBack, barFill, frost, blob, star, marker, ring, tracer, projectile;
            public Material[] trims;
        }

        static Materials BuildMaterials() {
            return new Materials {
                barBack = Unlit("BarBack", new Color(0.06f, 0.07f, 0.1f, 0.85f), transparent: true),
                barFill = Unlit("BarFill", Color.white),
                frost = Kit.Mat("FrostShell", new Color(0.6f, 0.88f, 1f), emission: 0.8f, alpha: 0.35f, smoothness: 0.9f),
                blob = Unlit("ShadowBlob", Color.white, Kit.Tex("Blob"), transparent: true),
                star = Unlit("Star", new Color(1f, 0.82f, 0.25f), TD_HudAuthoring.IconTexture("star"), transparent: true),
                marker = Unlit("BuildMarker", new Color(1f, 1f, 1f, 0.9f), TD_HudAuthoring.IconTexture("build"), transparent: true),
                ring = Unlit("RangeRing", Color.white, additive: true),
                tracer = Unlit("Tracer", Color.white, additive: true),
                projectile = Kit.Mat("Projectile", Color.white, emission: 2f),
                trims = System.Enum.GetValues(typeof(TD_TowerKind)).Cast<TD_TowerKind>()
                    .Select(k => Kit.Mat("Trim" + k, TD_Palette.Tower(k), emission: 0.6f, smoothness: 0.5f)).ToArray(),
            };
        }

        // ---------------------------------------------------------------- prefab pieces
        static GameObject IconQuad(string name, Transform parent, Vector3 position, float size, Material material) {
            var quad = Primitive(PrimitiveType.Quad, name, parent, position, Vector3.one * size, material, false);
            NoShadows(quad);
            return quad;
        }

        static LineRenderer Ring(Transform parent, Material material) {
            var go = Empty("RangeRing", parent);
            var ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.positionCount = 72;
            ring.widthMultiplier = 0.3f;
            ring.sharedMaterial = material;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.SetActive(false);
            return ring;
        }

        static TextMeshPro Label(Transform parent, Vector3 position, string text, float size, Color color) {
            var label = WorldText("Label", parent, position, text, size, color, new Vector2(10f, 1.4f));
            label.fontStyle = FontStyles.Bold;
            label.outlineWidth = 0.25f;
            label.outlineColor = new Color32(10, 12, 18, 220);
            label.gameObject.AddComponent<Billboard>();
            return label;
        }

        /// <summary>A flat ring of <paramref name="radius"/> metres in the parent's space (pad glows).</summary>
        static LineRenderer FlatRing(string name, Transform parent, float radius, float height, Material material, int points = 48) {
            var go = Empty(name, parent);
            var ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = points;
            for (int i = 0; i < points; i++) {
                float a = i * Mathf.PI * 2f / points;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
            }
            ring.widthMultiplier = 0.22f;
            ring.sharedMaterial = material;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.alignment = LineAlignment.View;
            return ring;
        }

        /// <summary>Concrete helipad from the military pack with a glow ring, a floating build marker and a range preview.</summary>
        static GameObject Slot(Materials m) {
            var root = new GameObject("TowerSlot");
            var view = root.AddComponent<TD_SlotView>();
            var pad = Model(Military + "Building_Prefebs/HeliPad_Prefeb.prefab", root.transform, PadSize, keepColliders: true);
            float top = pad != null ? RendererBounds(pad).max.y : 0.3f;
            if (pad == null) Debug.LogWarning("[TowerDefense] HeliPad prefab missing; the tower pad has no model.");
            view.topHeight = top;
            view.glow = FlatRing("Glow", root.transform, PadSize * 0.4f, top + 0.04f, m.ring);
            var marker = Empty("Marker", root.transform, new Vector3(0f, 2.6f, 0f));
            marker.AddComponent<Billboard>();
            IconQuad("Icon", marker.transform, Vector3.zero, 1.6f, m.marker);
            view.marker = marker.transform;
            view.rangeRing = Ring(root.transform, m.ring);
            return Kit.Save(root, "", "TowerSlot");
        }

        /// <summary>A Cartoon Military turret (placed on a pad's top by the client), level stars, owner label and range ring.</summary>
        static GameObject Tower(TD_TowerKind kind, Materials m) {
            var stats = TD_Rules.Tower(kind);
            var (model, size) = TowerModels[(int)kind];
            var root = new GameObject(stats.name);
            var view = root.AddComponent<TD_TowerView>();
            var head = Empty("Head", root.transform);
            view.head = head.transform;
            var turret = Model(Military + "CustomPart_Prefebs/Turrets_Prefebs/" + model, head.transform, size);
            if (turret == null) Debug.LogWarning($"[TowerDefense] Turret model {model} missing; {stats.name} has no model.");
            else if (kind == TD_TowerKind.Frost)
                foreach (var r in turret.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => m.trims[(int)kind]).ToArray();
            if (turret != null) {
                var b = RendererBounds(turret);
                var box = root.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size;
            }
            view.muzzle = Empty("Muzzle", head.transform, new Vector3(0f, size * 0.45f, size * 0.55f)).transform;

            var badge = Empty("Badge", root.transform, new Vector3(0f, size + 1.6f, 0f));
            badge.AddComponent<Billboard>();
            view.levelStars = new GameObject[TD_Rules.MaxLevel];
            for (int i = 0; i < TD_Rules.MaxLevel; i++)
                view.levelStars[i] = IconQuad($"Star{i}", badge.transform, new Vector3((i - (TD_Rules.MaxLevel - 1) * 0.5f) * 0.85f, 0f, 0f), 0.8f, m.star);
            view.label = Label(badge.transform, new Vector3(0f, 0.8f, 0f), "", 2.4f, Color.white);

            view.rangeRing = Ring(root.transform, m.ring);
            var audio = Empty("FireSound", root.transform, new Vector3(0f, 2f, 0f)).AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.maxDistance = 60f;
            audio.volume = kind == TD_TowerKind.Gunner ? 0.25f : 0.5f;
            audio.pitch = kind switch { TD_TowerKind.Cannon => 0.6f, TD_TowerKind.Frost => 1.4f, TD_TowerKind.Sniper => 0.8f, _ => 1f };
            audio.clip = Clip(FireClip);
            view.fireSound = audio;
            return Kit.Save(root, "Towers", stats.name);
        }

        static GameObject Enemy(TD_EnemyKind kind, Materials m) {
            var stats = TD_Rules.Enemy(kind);
            var (model, size) = EnemyModels[(int)kind];
            var root = new GameObject(stats.name);
            var view = root.AddComponent<TD_EnemyView>();
            var body = Empty("Body", root.transform);
            view.body = body.transform;
            if (Model(Military + model, body.transform, size) == null)
                Cube("Hull", body.transform, new Vector3(0f, size * 0.25f, 0f), new Vector3(size * 0.5f, size * 0.4f, size), m.barFill, false);
            NoShadows(body);

            // Soft contact shadow (on the ground even for flying enemies).
            var blob = Primitive(PrimitiveType.Quad, "Shadow", root.transform, new Vector3(0f, (stats.flying ? -TD_Rules.FlyHeight : 0f) + 0.06f, 0f), new Vector3(size * 0.8f, size * 0.8f, 1f), m.blob, false);
            blob.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            NoShadows(blob);

            float barWidth = Mathf.Clamp(size * 0.5f, 2.2f, 4f);
            var bar = Empty("HealthBar", root.transform, new Vector3(0f, size * 0.7f + 1.2f, 0f));
            bar.AddComponent<Billboard>();
            NoShadows(Cube("Back", bar.transform, Vector3.zero, new Vector3(barWidth + 0.16f, 0.42f, 0.02f), m.barBack, false));
            var pivot = Empty("Pivot", bar.transform, new Vector3(-barWidth * 0.5f, 0f, -0.03f));
            var fill = Cube("Fill", pivot.transform, new Vector3(barWidth * 0.5f, 0f, 0f), new Vector3(barWidth, 0.26f, 0.02f), m.barFill, false);
            NoShadows(fill);
            view.healthFill = pivot.transform;
            view.healthRenderer = fill.GetComponent<Renderer>();

            var ice = Primitive(PrimitiveType.Sphere, "FrostShell", root.transform, new Vector3(0f, size * 0.25f, 0f), Vector3.one * size * 0.9f, m.frost, false);
            NoShadows(ice);
            ice.SetActive(false);
            view.slowTint = ice.GetComponent<Renderer>();
            return Kit.Save(root, "Enemies", stats.name);
        }

        // ---------------------------------------------------------------- effects
        static TD_EffectSet BuildEffects(Materials m) {
            var tracer = new GameObject("Tracer");
            var line = tracer.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.widthMultiplier = 0.12f;
            line.sharedMaterial = m.tracer;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.numCapVertices = 3;

            var projectile = Primitive(PrimitiveType.Sphere, "Projectile", null, Vector3.zero, Vector3.one, m.projectile, false);
            NoShadows(projectile);
            var trail = projectile.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.widthMultiplier = 0.5f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.sharedMaterial = m.tracer;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.minVertexDistance = 0.2f;

            var popup = new GameObject("RewardPopup");
            var text = popup.AddComponent<TextMeshPro>();
            text.text = "+$0";
            text.fontSize = 6f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.83f, 0.25f);
            text.outlineWidth = 0.3f;
            text.outlineColor = new Color32(40, 24, 0, 255);
            text.rectTransform.sizeDelta = new Vector2(8f, 2f);
            popup.AddComponent<Billboard>();

            return new TD_EffectSet {
                tracer = Kit.Save(tracer, "Fx", "Tracer"),
                projectile = Kit.Save(projectile, "Fx", "Projectile"),
                rewardPopup = Kit.Save(popup, "Fx", "RewardPopup"),
                muzzleFlash = AssetDatabase.LoadAssetAtPath<GameObject>(Effects + "vfx_MuzzleFlash_01.prefab"),
                impact = AssetDatabase.LoadAssetAtPath<GameObject>(Effects + "vfx_Sparks_01.prefab"),
                frostImpact = AssetDatabase.LoadAssetAtPath<GameObject>(Effects + "vfx_Shockwave_01.prefab"),
                explosion = AssetDatabase.LoadAssetAtPath<GameObject>(Effects + "vfx_Explosion_01.prefab"),
            };
        }

        // ---------------------------------------------------------------- scene
        static void BuildScene() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var m = BuildMaterials();
            var slot = Slot(m);
            var towers = System.Enum.GetValues(typeof(TD_TowerKind)).Cast<TD_TowerKind>().Select(k => Tower(k, m)).ToArray();
            var enemies = System.Enum.GetValues(typeof(TD_EnemyKind)).Cast<TD_EnemyKind>().Select(k => Enemy(k, m)).ToArray();
            var effects = BuildEffects(m);
            var hud = TD_HudAuthoring.Build();

            Kit.NewLitScene(new Color(0.42f, 0.66f, 1f), new Color(0.7f, 0.82f, 0.95f), 180f, 520f, new Vector3(48f, -38f, 0f), 1.45f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.66f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.4f, 0.3f);
            AddPostProcessing();
            TD_MapAuthoring.Build(m);

            var mapGo = new GameObject("TowerDefenseMap");
            var map = mapGo.AddComponent<TD_Map>();
            Vector3 end = TD_Rules.Lane[^1];
            map.playerSpawn = Empty("PlayerSpawn", mapGo.transform, end + new Vector3(-8f, 0f, -12f)).transform;
            map.towerPrefabs = towers;
            map.enemyPrefabs = enemies;
            map.slotPrefab = slot;
            map.effects = effects;
            map.clientPrefab = hud;
            EditorUtility.SetDirty(map);
            Kit.SaveActiveScene(Kit.StartScene(Universe));
        }

        static T Override<T>(VolumeProfile profile) where T : VolumeComponent {
            if (!profile.TryGet(out T component)) {
                component = profile.Add<T>(true);
                component.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            return component;
        }

        /// <summary>A bright, saturated grade: filmic tonemapping, bloom on glowing pads and shots, light vignette.</summary>
        static void AddPostProcessing() {
            string path = Root + "/Materials/TowerDefensePost.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            Override<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);
            var bloom = Override<Bloom>(profile);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.4f);
            bloom.scatter.Override(0.6f);
            var color = Override<ColorAdjustments>(profile);
            color.postExposure.Override(0.15f);
            color.contrast.Override(8f);
            color.saturation.Override(4f);
            var vignette = Override<Vignette>(profile);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.5f);
            EditorUtility.SetDirty(profile);
            var volume = new GameObject("PostProcessing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 5f;
            volume.sharedProfile = profile;
        }

        [MenuItem("Ryan/Tower Defense/Register Scenes")]
        public static void RegisterScenesMenu() => RegisterScenes(Kit.StartScene(Universe), Kit.ServerScene(Universe));

        // ---------------------------------------------------------------- previews
        [MenuItem("Ryan/Tower Defense/Render Previews")]
        public static void RenderPreviews() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
            var map = Object.FindAnyObjectByType<TD_Map>();
            string dir = PreviewDir(Universe);
            int[] built = { 0, 2, 5, 6, 8, 9, 12, 13 };
            for (int i = 0; i < TD_Rules.Slots.Length; i++) {
                var pad = (GameObject)PrefabUtility.InstantiatePrefab(map.slotPrefab, map.transform);
                pad.transform.position = TD_Rules.Slots[i];
                var slot = pad.GetComponent<TD_SlotView>();
                int k = System.Array.IndexOf(built, i);
                if (k < 0) continue;
                slot.marker.gameObject.SetActive(false);
                var tower = ((GameObject)PrefabUtility.InstantiatePrefab(map.towerPrefabs[k % map.towerPrefabs.Length], map.transform)).GetComponent<TD_TowerView>();
                tower.transform.position = TD_Rules.Slots[i] + Vector3.up * slot.topHeight;
                for (int s = 0; s < tower.levelStars.Length; s++) tower.levelStars[s].SetActive(s < 1 + k % 3);
            }
            var kinds = new[] { TD_EnemyKind.Scout, TD_EnemyKind.Scout, TD_EnemyKind.Apc, TD_EnemyKind.Tank, TD_EnemyKind.Chopper, TD_EnemyKind.Apc, TD_EnemyKind.Boss, TD_EnemyKind.Scout };
            for (int i = 0; i < kinds.Length; i++) {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(map.enemyPrefabs[(int)kinds[i]], map.transform);
                Vector3 p = TD_Rules.EnemyPosition(kinds[i], 40f + i * 22f, out Vector3 f);
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(f));
            }
            var cam = PreviewCamera();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            Shoot(cam, new Vector3(-20f, 110f, -135f), new Vector3(0f, 0f, -5f), Path.Combine(dir, "overview.png"), 1600, 900);
            Shoot(cam, new Vector3(-62f, 14f, 50f), new Vector3(-40f, 2f, 10f), Path.Combine(dir, "lane.png"), 1600, 900);
            Shoot(cam, new Vector3(-75f, 24f, 58f), new Vector3(-35f, 0f, 5f), Path.Combine(dir, "thumbnail.png"), 640, 360);
            Object.DestroyImmediate(cam.gameObject);
            Kit.PublishThumbnail(Universe, Path.Combine(dir, "thumbnail.png"));
            EditorSceneManager.OpenScene(Kit.StartScene(Universe), OpenSceneMode.Single);
        }
    }
}
