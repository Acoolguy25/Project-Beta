using System.Collections.Generic;
using FishNet.Managing.Object;
using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Client;
using RyanAssets.Tools.Shared;
using RyanAssets.UI.Navigation;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using Universes.UniverseData.classic_horror.Client;

namespace Universes.UniverseData.classic_horror.Editor {
    /// <summary>Rebuildable presentation assets. Scene layout and story edits are kept separate.</summary>
    public static class CH_Authoring {
        public const string Root = "Assets/Universes/UniverseData/classic_horror";
        static readonly Color Amber = new(0.78f, 0.55f, 0.28f);

        public static void EnsureFolders() {
            foreach (string folder in new[] { "Prefabs", "Materials", "Data", "Scenes" })
                if (!AssetDatabase.IsValidFolder(Root + "/" + folder)) AssetDatabase.CreateFolder(Root, folder);
            if (!AssetDatabase.IsValidFolder("Packages/com.ryan.gameframework/Runtime/Tools/Flashlight")) AssetDatabase.CreateFolder("Packages/com.ryan.gameframework/Runtime/Tools", "Flashlight");
        }
        static Material Material(string name, Color color, bool glow = false, string folder = null) {
            string path = (folder ?? Root + "/Materials") + "/" + name + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null) { result = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(result, path); }
            result.SetColor("_BaseColor", color);
            result.SetFloat("_Smoothness", 0.18f);
            if (glow) { result.EnableKeyword("_EMISSION"); result.SetColor("_EmissionColor", color * 2f); }
            EditorUtility.SetDirty(result);
            return result;
        }
        static GameObject Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, Vector3 angles = default) {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localEulerAngles = angles;
            obj.transform.localScale = scale;
            Object.DestroyImmediate(obj.GetComponent<Collider>());
            obj.GetComponent<Renderer>().sharedMaterial = material;
            return obj;
        }
        static void Set(Object target, string property, string value) { var so = new SerializedObject(target); so.FindProperty(property).stringValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }

        [MenuItem("Ryan/Classic Horror/Configure Realtime Map Lighting")]
        public static void ConfigureMapLighting() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode before editing map lighting.");
            var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            string path = Root + "/Scenes/classic_horror_start.unity";
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                EnsureFolders();
                var skyShader = Shader.Find("Classic Horror/Night Sky");
                if (skyShader == null) throw new System.InvalidOperationException("Classic Horror night sky shader is missing.");
                string skyPath = Root + "/Materials/NightSky.mat";
                var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
                if (sky == null) {
                    sky = new Material(skyShader) { name = "NightSky" };
                    AssetDatabase.CreateAsset(sky, skyPath);
                }
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.009f, 0.013f, 0.02f);
                RenderSettings.ambientIntensity = 1f;
                // The imported daylight cubemap makes wet surfaces shine even at night.
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
                RenderSettings.customReflectionTexture = null;
                RenderSettings.reflectionIntensity = 0.1f;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = new Color(0.018f, 0.028f, 0.045f);
                RenderSettings.fogDensity = 0.014f;
                if (RenderSettings.sun != null) {
                    RenderSettings.sun.color = new Color(0.47f, 0.6f, 0.78f);
                    RenderSettings.sun.intensity = 0.025f;
                    EditorUtility.SetDirty(RenderSettings.sun);
                }
                // FishNet creates unsaved holding scenes during loading. Baking here opens a
                // save dialog for those scenes, even with both GI options disabled.
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
                var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(Root + "/Data/RealtimeLighting.lighting");
                if (settings == null) {
                    settings = new LightingSettings { name = "Classic Horror Realtime Lighting" };
                    AssetDatabase.CreateAsset(settings, Root + "/Data/RealtimeLighting.lighting");
                }
                settings.bakedGI = false;
                settings.realtimeGI = false;
                Lightmapping.lightingSettings = settings;
                Lightmapping.lightingDataAsset = null;
                LightmapSettings.lightmaps = System.Array.Empty<LightmapData>();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            } finally {
                if (previous.IsValid() && previous.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
                if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Ryan/Classic Horror/Rebuild Presentation Prefabs")]
        public static void BuildPresentation() {
            EnsureFolders();
            BuildFlashlight();
            RyanAssets.Editor.NPCCharacterAuthoring.Build();
            BuildClueViews();
            BuildHud();
            var library = AssetDatabase.LoadAssetAtPath<CH_StoryLibrary>(Root + "/Data/StoryLibrary.asset");
            if (library == null) AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<CH_StoryLibrary>(), Root + "/Data/StoryLibrary.asset");
            AssetDatabase.SaveAssets();
        }

        static void BuildFlashlight() {
            const string folder = "Packages/com.ryan.gameframework/Runtime/Tools/Flashlight";
            var shell = Material("FlashlightShell", new Color(0.045f, 0.052f, 0.055f), false, folder);
            var lens = Material("FlashlightLens", new Color(0.85f, 0.91f, 0.82f), true, folder);
            var root = new GameObject("Flashlight");
            var visual = new GameObject("LightHousing"); visual.transform.SetParent(root.transform, false);
            Shape("Barrel", PrimitiveType.Cylinder, visual.transform, Vector3.zero, new Vector3(0.15f, 0.23f, 0.15f), shell, new Vector3(90, 0, 0));
            Shape("LampHead", PrimitiveType.Cylinder, visual.transform, new Vector3(0, 0, 0.24f), new Vector3(0.24f, 0.075f, 0.24f), shell, new Vector3(90, 0, 0));
            Shape("Lens", PrimitiveType.Cylinder, visual.transform, new Vector3(0, 0, 0.317f), new Vector3(0.205f, 0.008f, 0.205f), lens, new Vector3(90, 0, 0));
            var lightGO = new GameObject("Beam"); lightGO.transform.SetParent(visual.transform, false);
            var beam = lightGO.AddComponent<Light>();
            beam.type = LightType.Spot; beam.range = 48; beam.spotAngle = 54; beam.innerSpotAngle = 30; beam.intensity = 16;
            beam.color = new Color(0.86f, 0.92f, 0.8f); beam.shadows = LightShadows.Soft;
            beam.cullingMask = ~LayerMask.GetMask("LocalCharacter");
            visual.AddComponent<AudioSource>().playOnAwake = false;
            root.AddComponent<NetworkObject>();
            var tool = root.AddComponent<ToolFlashlightShared>();
            tool.toolEnum = ToolEnum.Flashlight; tool.toolName = "Flashlight";
            tool.toolImage = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "/FlashlightIcon.png");
            tool.toolDesc = "Left click to toggle. Unlimited power. Light can reveal you to things in the dark.";
            tool.staminaCostInit = 0; tool.hitDamageInit = 0; tool.currentAmmo = -1; tool.maxClipAmmoInit = -1;
            tool.attackCooldownInit = 0.2f; tool.weaponRoot = visual;
            Set(tool, "clientScript", "RyanAssets.Tools.Client.FlashlightToolClient, RyanAssets.Tools.Client");
            Set(tool, "clientObserver", ""); Set(tool, "serverScript", "");
            var serialized = new SerializedObject(tool); serialized.FindProperty("beam").objectReferenceValue = beam; serialized.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<FirstPersonToolView>();
            SetPrefabHash(root, folder + "/Flashlight.prefab");
            var saved = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Flashlight.prefab");
            Register(saved); Object.DestroyImmediate(root);
        }

        static void Register(GameObject prefab) {
            var catalog = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>("Assets/DefaultPrefabObjects.asset");
            catalog.AddObject(prefab.GetComponent<NetworkObject>(), true);
            EditorUtility.SetDirty(catalog);
        }
        static void SetPrefabHash(GameObject root, string path) {
            // A cloned NetworkObject carries its source's hash. Assign the new
            // identity before saving, including when replacing an existing prefab.
            var key = new System.Text.StringBuilder();
            foreach (char c in (path + root.name).Trim().ToLowerInvariant())
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') key.Append(c);
            root.GetComponent<NetworkObject>().SetAssetPathHash(GameKit.Dependencies.Utilities.Hashing.GetStableHashU64(key.ToString()));
        }




        const string FloodedProps = "Assets/Flooded_Grounds/Prefabs/Props/";
        const string QuickEffects = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/";

        static GameObject PlaceVisual(string path, Transform parent, Vector3 position, Vector3 scale, Vector3 angles = default) {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new System.InvalidOperationException("Missing Classic Horror presentation asset: " + path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            // Imported Flooded Grounds props carry their demo-scene world transforms.
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(angles);
            instance.transform.localScale = scale;
            // These are presentation-only children. Keep their prefab links, but do
            // not let imported demo colliders block the case's interaction rays.
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true)) {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            return instance;
        }

        static Material UrpParticleMaterial(Material source) {
            string path = Root + "/Materials/VFX_" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) throw new System.InvalidOperationException("URP particle shader is missing.");
            if (material == null) {
                material = new Material(shader) { name = "VFX_" + source.name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", source.mainTexture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject PlaceEffect(string path, Transform parent, Vector3 position, float scale, Color tint) {
            var effect = PlaceVisual(path, parent, position, Vector3.one * scale);
            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true)) {
                var main = particle.main;
                main.startColor = tint;
            }
            foreach (var renderer in effect.GetComponentsInChildren<ParticleSystemRenderer>(true)) {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && !materials[i].shader.name.StartsWith("Universal Render Pipeline/"))
                        materials[i] = UrpParticleMaterial(materials[i]);
                renderer.sharedMaterials = materials;
            }
            return effect;
        }

        static void ClearVisualChildren(GameObject root) {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        }

        [MenuItem("Ryan/Classic Horror/Rebuild Evidence Prefabs")]
        public static void BuildClueViews() {
            EnsureFolders();
            var paper = Material("EvidencePaper", new Color(0.65f, 0.58f, 0.41f));
            var ink = Material("EvidenceInk", new Color(0.12f, 0.16f, 0.15f));
            var rune = Material("EvidenceGlow", new Color(0.78f, 0.54f, 0.25f), true);
            string cluePath = Root + "/Prefabs/EvidenceRecord.prefab";
            var clue = PrefabUtility.LoadPrefabContents(cluePath);
            try {
                ClearVisualChildren(clue);
                PlaceVisual(FloodedProps + "Prop_SmallTable_B.prefab", clue.transform, Vector3.zero, Vector3.one);
                PlaceVisual(FloodedProps + "Prop_Lamp_A.prefab", clue.transform, new Vector3(0.32f, 0.70f, 0.19f), Vector3.one * 0.68f);
                var notes = Shape("Field Notes", PrimitiveType.Quad, clue.transform, new Vector3(-0.16f, 0.735f, -0.04f), new Vector3(0.48f, 0.34f, 1f), paper, new Vector3(90f, 18f, 0f));
                Shape("Case Heading", PrimitiveType.Quad, notes.transform, new Vector3(-0.06f, 0.28f, -0.004f), new Vector3(0.68f, 0.09f, 1f), ink);
                for (int i = 0; i < 3; i++)
                    Shape("Handwritten Line " + i, PrimitiveType.Quad, notes.transform,
                        new Vector3(-0.08f, 0.06f - i * 0.17f, -0.004f), new Vector3(0.55f - i * 0.08f, 0.025f, 1f), ink);
                Shape("Wax Seal", PrimitiveType.Sphere, clue.transform, new Vector3(-0.34f, 0.745f, -0.15f), new Vector3(0.065f, 0.012f, 0.065f), rune);
                PlaceEffect(QuickEffects + "vfx_Portal_01.prefab", clue.transform, new Vector3(-0.16f, 0.76f, 0f), 0.1f, new Color(0.95f, 0.62f, 0.3f, 0.35f));
                var light = clue.GetComponent<Light>();
                light.color = Amber; light.range = 4f; light.intensity = 0.85f;
                PrefabUtility.SaveAsPrefabAsset(clue, cluePath);
            } finally { PrefabUtility.UnloadPrefabContents(clue); }

            string sourcePath = Root + "/Prefabs/HauntingSource.prefab";
            var source = PrefabUtility.LoadPrefabContents(sourcePath);
            try {
                ClearVisualChildren(source);
                PlaceVisual(FloodedProps + "Prop_Pulpit_A.prefab", source.transform, Vector3.zero, Vector3.one * 0.82f);
                PlaceVisual(FloodedProps + "Prop_Vase_A.prefab", source.transform, new Vector3(0f, 1.75f, 0.15f), Vector3.one * 0.9f);
                for (int i = 0; i < 3; i++) {
                    float a = i * Mathf.PI * 2f / 3f;
                    PlaceVisual(FloodedProps + "Prop_Lamp_C.prefab", source.transform,
                        new Vector3(Mathf.Sin(a) * 1.3f, 0.58f, Mathf.Cos(a) * 1.3f), Vector3.one * 0.6f);
                }
                PlaceEffect(QuickEffects + "vfx_Portal_01.prefab", source.transform, new Vector3(0f, 1.9f, 0.22f), 0.34f, new Color(0.3f, 0.75f, 0.7f, 0.7f));
                PlaceEffect(QuickEffects + "vfx_Electricity_01.prefab", source.transform, new Vector3(0f, 2.0f, 0.16f), 0.16f, new Color(0.45f, 0.85f, 0.78f, 0.45f));
                var light = source.GetComponent<Light>();
                light.color = new Color(0.33f, 0.73f, 0.64f); light.range = 11f; light.intensity = 0.75f;
                PrefabUtility.SaveAsPrefabAsset(source, sourcePath);
            } finally { PrefabUtility.UnloadPrefabContents(source); }
            AssetDatabase.SaveAssets();
        }

        static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax) {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            return rect;
        }
        static UnityEngine.UI.Image Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color) {
            var rect = Rect(name, parent, min, max, Vector2.zero, Vector2.zero);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false; return image;
        }

        /// <summary>
        /// The investigation HUD, made from the shared HudKit so it matches the rest of the game, in a
        /// horror key: dark glass, one amber accent, and nothing on screen that the case doesn't need
        /// right now. Case status top centre, compass under it, prompts low, radio transcript at the
        /// bottom, journal and ending as cards.
        /// </summary>
        static void BuildHud() {
            RectTransform canvas = HudKit.Canvas("InvestigationHUD", 40);
            var root = canvas.gameObject;
            var client = root.AddComponent<CH_ClientController>();
            client.dangerOverlay = Panel("Danger", root.transform, Vector2.zero, Vector2.one, new Color(0.3f, 0.035f, 0.025f, 0));
            client.evidenceIcon = HudKit.Icon("magnifier");
            client.offeringIcon = HudKit.Icon("candle");
            client.solvedIcon = HudKit.Icon("laurels_trophy");
            client.failedIcon = HudKit.Icon("skull");

            BuildCaseStatus(canvas, client);
            BuildCompass(canvas, client);

            RectTransform reticle = UIKit.Pinned(canvas, "Reticle", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));
            UIKit.Rounded(reticle, UIColorRole.TextPrimary, 3f, 0.75f, raycast: false);

            RectTransform interaction = HudKit.KeyPrompt(canvas, "Interaction", "E", "Inspect", Amber, out client.interactionLabel, out UnityEngine.UI.Image key, 46f);
            interaction.anchorMin = interaction.anchorMax = interaction.pivot = new Vector2(0.5f, 0.5f);
            interaction.anchoredPosition = new Vector2(0f, -110f);
            client.interactionKey = key.gameObject;
            client.interactionPrompt = HudKit.Popup(interaction, false);

            BuildRadio(canvas, client);
            BuildJournal(canvas, client);
            BuildEnding(canvas, client);
            var radio = new GameObject("RadioAudio"); radio.transform.SetParent(root.transform, false); client.radioAudio = radio.AddComponent<AudioSource>(); client.radioAudio.playOnAwake = false;
            client.radioCue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Flooded_Grounds/Content/Sounds/Taps.mp3");
            var objectiveCollect = new GameObject("ObjectiveCollectAudio"); objectiveCollect.transform.SetParent(root.transform, false); client.objectiveCollectAudio = objectiveCollect.AddComponent<AudioSource>();
            client.objectiveCollectAudio.playOnAwake = false; client.objectiveCollectAudio.spatialBlend = 0;
            client.objectiveCollectCue = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/objective-collect.mp3");
            var danger = new GameObject("PresenceAudio"); danger.transform.SetParent(root.transform, false); client.dangerAudio = danger.AddComponent<AudioSource>();
            client.dangerAudio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Flooded_Grounds/Content/Sounds/DeepRattle.mp3"); client.dangerAudio.loop = true; client.dangerAudio.volume = 0;
            client.dangerAudio.playOnAwake = true;
            var scare = root.AddComponent<CH_Jumpscare>();
            var scarePanel = Panel("Encounter", root.transform, Vector2.zero, Vector2.one, new Color(0.002f,0,0,0.98f));
            scare.overlay = scarePanel.gameObject.AddComponent<CanvasGroup>();
            scare.overlay.alpha = 0; scare.overlay.blocksRaycasts = false; scare.overlay.interactable = false;
            var face = Rect("The face", scarePanel.transform, new Vector2(0.5f,0.5f), new Vector2(0.5f,0.5f), new Vector2(-650,-650), new Vector2(650,650));
            scare.face = face;
            var portrait = face.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            portrait.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(RyanAssets.Editor.NPCCharacterAuthoring.Root + "/Data/PresencePortrait.png"); portrait.raycastTarget = false;
            scare.sting = scarePanel.gameObject.AddComponent<AudioSource>();
            scare.sting.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Packages/com.ryan.ui/Runtime/Client/ClientAudio/JumpscareAudio/jumpscare.mp3");
            scare.sting.playOnAwake = false; scare.sting.volume = 0.75f; scare.sting.spatialBlend = 0;
            UIKit.SavePrefab(root, Root + "/Prefabs/InvestigationHUD.prefab");
        }

        /// <summary>Top centre: chapter, the current objective, case progress and the clock.</summary>
        static void BuildCaseStatus(RectTransform canvas, CH_ClientController client) {
            RectTransform card = UIKit.Pinned(canvas, "CaseStatus", new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(560f, 100f));
            HudKit.Glass(card, 16f);
            UIKit.Row(card.gameObject, 14f, new RectOffset(14, 20, 12, 12), TextAnchor.MiddleLeft);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.Emblem(card, "Emblem", "magnifier", 58f, Amber, out _, out client.caseIcon);

            RectTransform text = UIKit.Node(card, "Text");
            UIKit.Size(text, -1f, -1f, 1f);
            UIKit.Column(text.gameObject, 3f, null, TextAnchor.MiddleLeft);
            client.chapterLabel = UIKit.Text(text, "Chapter", "Investigator", UIText.Caption, UIColorRole.TextMuted);
            client.chapterLabel.color = Amber;
            Object.DestroyImmediate(client.chapterLabel.GetComponent<UIThemeColor>());
            client.objectiveLabel = UIKit.Text(text, "Objective", "Establishing the radio link...", UIText.Strong);
            client.objectiveLabel.fontSize = 18f;
            client.objectiveLabel.textWrappingMode = TextWrappingModes.Normal;

            RectTransform chips = UIKit.Node(text, "Chips");
            UIKit.Row(chips.gameObject, 8f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft);
            client.progressChip = HudKit.Chip(chips, "Progress", "magnifier", "Evidence 0 / 4", Amber, 28f, UIText.Small);
            client.timerChip = HudKit.Chip(chips, "Time", "hourglass", "00:00", Amber, 28f, UIText.Small);
            RectTransform journal = UIKit.Node(chips, "JournalHint");
            UIKit.Row(journal.gameObject, 6f, new RectOffset(6, 0, 0, 0), TextAnchor.MiddleLeft);
            HudKit.KeyCap(journal, "F");
            UIKit.Text(journal, "Label", "Journal", UIText.Small, UIColorRole.TextSecondary);
        }

        /// <summary>Under the case status: an arrow towards the nearest lead and how far it is.</summary>
        static void BuildCompass(RectTransform canvas, CH_ClientController client) {
            RectTransform pill = UIKit.Pinned(canvas, "ObjectiveCompass", new Vector2(0.5f, 1f), new Vector2(0f, -184f), new Vector2(260f, 38f));
            HudKit.Glass(pill, 19f);
            UIKit.Row(pill.gameObject, 8f, new RectOffset(8, 16, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(pill.gameObject, true, false);
            RectTransform pointer = UIKit.Node(pill, "DirectionPointer");
            UIKit.Size(pointer, 26f, 26f);
            UnityEngine.UI.Image arrow = HudKit.Icon(pointer, "navigation", 22f, Amber);
            UIKit.Center(arrow.rectTransform);
            var readout = UIKit.Text(pill, "Readout", "SEARCH   FORWARD   12 m", UIText.Caption, UIColorRole.TextPrimary);
            client.objectiveCompass = canvas.gameObject.AddComponent<ObjectiveCompass>();
            client.objectiveCompass.Configure(pill.gameObject, pointer, readout, "SEARCH");
        }

        /// <summary>Bottom centre: the radio transcript, as a card that fades in for each new message.</summary>
        static void BuildRadio(RectTransform canvas, CH_ClientController client) {
            RectTransform card = HudKit.Card(canvas, "RadioTranscript", new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(820f, 120f));
            HudKit.AccentStripe(card, Amber, 4f, left: true);
            UIKit.Row(card.gameObject, 16f, new RectOffset(22, 26, 18, 18), TextAnchor.UpperLeft);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.Emblem(card, "Emblem", "pocket_radio", 52f, Amber, out _, out _);
            RectTransform text = UIKit.Node(card, "Text");
            UIKit.Size(text, -1f, -1f, 1f);
            UIKit.Column(text.gameObject, 4f, null, TextAnchor.UpperLeft);
            var caption = UIKit.Text(text, "Caption", "Radio", UIText.Caption, UIColorRole.TextMuted);
            caption.color = Amber;
            Object.DestroyImmediate(caption.GetComponent<UIThemeColor>());
            client.dialogueLabel = UIKit.Text(text, "Transcript", string.Empty, UIText.Body);
            client.dialogueLabel.fontSize = 19f;
            client.dialogueLabel.textWrappingMode = TextWrappingModes.Normal;
            client.dialogueLabel.richText = true;
            client.dialoguePopup = card.GetComponent<RyanAssets.UI.GameHud.HudPopup>();
        }

        /// <summary>The field journal: a readable card with page buttons; the world keeps running behind it.</summary>
        static void BuildJournal(RectTransform canvas, CH_ClientController client) {
            RectTransform card = HudKit.Card(canvas, "FieldJournal", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040f, 700f));
            client.journalPanel = card.gameObject;
            card.GetComponentInChildren<UnityEngine.UI.Image>().raycastTarget = true;
            HudKit.AccentStripe(card, Amber, 5f);
            UIKit.Column(card.gameObject, 14f, new RectOffset(40, 40, 30, 30), TextAnchor.UpperLeft);

            RectTransform header = UIKit.Node(card, "Header");
            UIKit.Row(header.gameObject, 14f, null, TextAnchor.MiddleLeft);
            HudKit.Emblem(header, "Emblem", "notebook", 56f, Amber, out _, out _);
            RectTransform titles = UIKit.Node(header, "Titles");
            UIKit.Size(titles, -1f, -1f, 1f);
            UIKit.Column(titles.gameObject, 0f, null, TextAnchor.MiddleLeft);
            UIKit.Text(titles, "Title", "Field notes", UIText.Title);
            client.journalPageLabel = UIKit.Text(titles, "Page", "Page 1 / 1", UIText.Small, UIColorRole.TextMuted);
            client.journalClose = UIKit.IconButton(header, "Close", "close", 40f, UIColorRole.SurfaceInset, 20f, out _, out _);
            UIKit.Divider(card);

            client.journalBody = UIKit.Text(card, "Entry", string.Empty, UIText.Body, UIColorRole.TextPrimary, TextAlignmentOptions.TopLeft);
            client.journalBody.fontSize = 23f;
            client.journalBody.lineSpacing = 12f;
            client.journalBody.textWrappingMode = TextWrappingModes.Normal;
            client.journalBody.overflowMode = TextOverflowModes.Overflow;
            client.journalBody.richText = true;
            UIKit.Size(client.journalBody, -1f, 440f);

            RectTransform footer = UIKit.Node(card, "Footer");
            UIKit.Row(footer.gameObject, 10f, null, TextAnchor.MiddleLeft);
            UnityEngine.UI.Image warn = HudKit.Icon(footer, "warning", 20f, Amber);
            var note = UIKit.Text(footer, "Warning", "The settlement does not pause while you read.", UIText.Small, UIColorRole.TextSecondary);
            UIKit.Size(note, -1f, -1f, 1f);
            client.journalPrevious = UIKit.TextButton(footer, "Previous", "Previous", "arrow_back", UIColorRole.SurfaceInset, UIColorRole.TextPrimary, 42f);
            UIKit.Size(client.journalPrevious, 150f, 42f);
            client.journalNext = UIKit.TextButton(footer, "Next", "Next", "arrow_forward", UIColorRole.Accent, UIColorRole.OnAccent, 42f);
            UIKit.Size(client.journalNext, 150f, 42f);
            warn.raycastTarget = false;
            card.gameObject.SetActive(false);
            // The journal opens and closes by script, not through HudPopup.
            Object.DestroyImmediate(card.GetComponent<RyanAssets.UI.GameHud.HudPopup>());
            card.GetComponent<CanvasGroup>().alpha = 1f;
        }

        /// <summary>The case ending: how it ended, the epilogue, and when the next case starts.</summary>
        static void BuildEnding(RectTransform canvas, CH_ClientController client) {
            RectTransform card = HudKit.Card(canvas, "CaseEnding", new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(860f, 420f));
            client.endingStripe = HudKit.AccentStripe(card, Amber, 5f);
            UIKit.Column(card.gameObject, 10f, new RectOffset(48, 48, 32, 30), TextAnchor.UpperCenter);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.Emblem(card, "Emblem", "laurels_trophy", 92f, Amber, out client.endingDisc, out client.endingIcon);
            UIKit.Text(card, "Kicker", "Case closed", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            client.endingTitle = UIKit.Text(card, "Title", "Last rites", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            Object.DestroyImmediate(client.endingTitle.GetComponent<UIThemeColor>());
            client.endingLabel = UIKit.Text(card, "Epilogue", string.Empty, UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            client.endingLabel.fontSize = 20f;
            client.endingLabel.lineSpacing = 8f;
            client.endingLabel.textWrappingMode = TextWrappingModes.Normal;
            RectTransform chips = UIKit.Node(card, "Chips");
            UIKit.Row(chips.gameObject, 10f, new RectOffset(0, 0, 10, 0), TextAnchor.MiddleCenter);
            client.endingNextCase = HudKit.Chip(chips, "NextCase", "hourglass_bottom", "Next case in 20s", Amber, 32f, UIText.Small);
            client.endingSolved = HudKit.Chip(chips, "Solved", "star_medal", "Cases solved: 0", Amber, 32f, UIText.Small);
            client.endingPopup = card.GetComponent<RyanAssets.UI.GameHud.HudPopup>();
        }
    }
}
