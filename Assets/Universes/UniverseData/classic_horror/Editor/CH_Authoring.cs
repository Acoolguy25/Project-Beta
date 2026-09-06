using System.Collections.Generic;
using FishNet.Managing.Object;
using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Client;
using RyanAssets.Tools.Shared;
using TMPro;
using UnityEditor;
using UnityEngine;
using Universes.UniverseData.classic_horror.Client;

namespace Universes.UniverseData.classic_horror.Editor {
    /// <summary>Rebuildable presentation assets. Scene layout and story edits are kept separate.</summary>
    public static class CH_Authoring {
        public const string Root = "Assets/Universes/UniverseData/classic_horror";
        static readonly Color Ink = new(0.025f, 0.042f, 0.05f, 0.93f);
        static readonly Color Paper = new(0.86f, 0.86f, 0.78f);
        static readonly Color Amber = new(0.78f, 0.55f, 0.28f);

        public static void EnsureFolders() {
            foreach (string folder in new[] { "Prefabs", "Materials", "Data", "Scenes" })
                if (!AssetDatabase.IsValidFolder(Root + "/" + folder)) AssetDatabase.CreateFolder(Root, folder);
            if (!AssetDatabase.IsValidFolder("Assets/RyanAssets/Tools/Flashlight")) AssetDatabase.CreateFolder("Assets/RyanAssets/Tools", "Flashlight");
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
            const string folder = "Assets/RyanAssets/Tools/Flashlight";
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




        static void BuildClueViews() {
            var paper = Material("EvidencePaper", new Color(0.65f, 0.58f, 0.41f));
            var metal = Material("RitualMetal", new Color(0.12f, 0.16f, 0.16f));
            var rune = Material("EvidenceGlow", new Color(0.78f, 0.54f, 0.25f), true);
            var clue = new GameObject("EvidenceRecord");
            Shape("Record", PrimitiveType.Cube, clue.transform, Vector3.zero, new Vector3(0.55f, 0.08f, 0.4f), paper, new Vector3(-15, 0, 0));
            Shape("Seal", PrimitiveType.Cylinder, clue.transform, new Vector3(0, 0.15f, 0), new Vector3(0.2f, 0.025f, 0.2f), rune);
            var lamp = clue.AddComponent<Light>(); lamp.type = LightType.Point; lamp.color = Amber; lamp.range = 5; lamp.intensity = 1.2f;
            PrefabUtility.SaveAsPrefabAsset(clue, Root + "/Prefabs/EvidenceRecord.prefab"); Object.DestroyImmediate(clue);
            var source = new GameObject("HauntingSource");
            Shape("SubmergedBell", PrimitiveType.Cylinder, source.transform, Vector3.zero, new Vector3(2, 1.1f, 2), metal);
            for (int i = 0; i < 3; i++) {
                float a = i * Mathf.PI * 2 / 3;
                Shape("Offering" + i, PrimitiveType.Cube, source.transform, new Vector3(Mathf.Sin(a) * 1.6f, -0.75f, Mathf.Cos(a) * 1.6f), new Vector3(0.7f, 0.12f, 0.7f), rune);
            }
            var sourceLight = source.AddComponent<Light>(); sourceLight.type = LightType.Point; sourceLight.range = 14; sourceLight.intensity = 0.7f; sourceLight.color = new Color(0.33f, 0.73f, 0.64f);
            PrefabUtility.SaveAsPrefabAsset(source, Root + "/Prefabs/HauntingSource.prefab"); Object.DestroyImmediate(source);
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
        static TextMeshProUGUI Text(string name, Transform parent, Vector2 min, Vector2 max, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft) {
            var rect = Rect(name, parent, min, max, new Vector2(20, 6), new Vector2(-20, -6));
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size; text.color = color; text.alignment = alignment; text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Ellipsis; text.text = name;
            return text;
        }
        static UnityEngine.UI.Button Button(string name, Transform parent, Vector2 min, Vector2 max) {
            var background = Panel(name, parent, min, max, new Color(0.15f, 0.19f, 0.2f)); background.raycastTarget = true;
            var button = background.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = background;
            Text(name, background.transform, Vector2.zero, Vector2.one, 25, Paper, TextAlignmentOptions.Center);
            return button;
        }
        static void BuildHud() {
            var root = new GameObject("InvestigationHUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 40;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            var client = root.AddComponent<CH_ClientController>();
            client.dangerOverlay = Panel("Danger", root.transform, Vector2.zero, Vector2.one, new Color(0.3f, 0.035f, 0.025f, 0));
            var top = Panel("CaseHeader", root.transform, new Vector2(0.33f, 0.79f), new Vector2(0.86f, 0.94f), Ink);
            Panel("AmberRule", top.transform, new Vector2(0, 0), new Vector2(0.006f, 1), Amber);
            client.caseLabel = Text("Case", top.transform, new Vector2(0.01f, 0.7f), new Vector2(1, 1), 19, Amber);
            client.chapterLabel = Text("Chapter", top.transform, new Vector2(0.01f, 0.32f), new Vector2(1, 0.75f), 35, Paper);
            client.objectiveLabel = Text("Objective", top.transform, new Vector2(0.01f, 0), new Vector2(1, 0.33f), 24, Paper);
            client.clockLabel = Text("Clock", root.transform, new Vector2(0.85f, 0.75f), new Vector2(0.98f, 0.84f), 42, Paper, TextAlignmentOptions.TopRight);
            client.bearingLabel = Text("Search bearing", root.transform, new Vector2(0.33f, 0.735f), new Vector2(0.85f, 0.785f), 20, Paper);
            client.countersLabel = Text("Case progress", root.transform, new Vector2(0.33f, 0.68f), new Vector2(0.88f, 0.73f), 19, Amber);
            Text("Reticle", root.transform, new Vector2(0.475f, 0.475f), new Vector2(0.525f, 0.525f), 25, Paper, TextAlignmentOptions.Center).text = "+";
            client.interactionLabel = Text("Interaction", root.transform, new Vector2(0.22f, 0.35f), new Vector2(0.78f, 0.44f), 26, Paper, TextAlignmentOptions.Center);
            var dialogue = Panel("RadioTranscript", root.transform, new Vector2(0.2f, 0.17f), new Vector2(0.8f, 0.34f), Ink);
            client.dialoguePanel = dialogue.gameObject;
            client.dialogueLabel = Text("Transcript", dialogue.transform, new Vector2(0.01f, 0.06f), new Vector2(0.99f, 0.94f), 25, Paper);
            client.controlsLabel = Text("Controls", root.transform, new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.145f), 20, Paper, TextAlignmentOptions.Center);
            var journal = Panel("FieldJournal", root.transform, new Vector2(0.17f, 0.17f), new Vector2(0.83f, 0.83f), new Color(0.04f, 0.06f, 0.065f, 0.995f));
            journal.raycastTarget = true; client.journalPanel = journal.gameObject;
            client.journalPageLabel = Text("Field notes", journal.transform, new Vector2(0.035f, 0.85f), new Vector2(0.97f, 0.97f), 25, Amber);
            client.journalBody = Text("Journal entry", journal.transform, new Vector2(0.05f, 0.23f), new Vector2(0.95f, 0.82f), 31, Paper);
            client.journalBody.overflowMode = TextOverflowModes.Overflow;
            Text("Live warning", journal.transform, new Vector2(0.045f, 0.14f), new Vector2(0.95f, 0.21f), 19, Amber).text = "The settlement does not pause while you read.";
            client.journalPrevious = Button("Previous", journal.transform, new Vector2(0.06f, 0.035f), new Vector2(0.28f, 0.12f));
            client.journalNext = Button("Next", journal.transform, new Vector2(0.32f, 0.035f), new Vector2(0.54f, 0.12f));
            client.journalClose = Button("Close [F / Esc]", journal.transform, new Vector2(0.65f, 0.035f), new Vector2(0.94f, 0.12f));
            var ending = Panel("CaseEnding", root.transform, new Vector2(0.13f, 0.23f), new Vector2(0.87f, 0.77f), Ink); client.endingPanel = ending.gameObject;
            client.endingLabel = Text("Ending", ending.transform, new Vector2(0.045f, 0.07f), new Vector2(0.955f, 0.93f), 32, Paper, TextAlignmentOptions.Center);
            var radio = new GameObject("RadioAudio"); radio.transform.SetParent(root.transform, false); client.radioAudio = radio.AddComponent<AudioSource>(); client.radioAudio.playOnAwake = false;
            client.radioCue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Flooded_Grounds/Content/Sounds/Taps.mp3");
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
            scare.sting.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Flooded_Grounds/Content/Sounds/Horn.mp3");
            scare.sting.playOnAwake = false; scare.sting.volume = 0.75f; scare.sting.spatialBlend = 0;
            PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/InvestigationHUD.prefab"); Object.DestroyImmediate(root);
        }
    }
}
