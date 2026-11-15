using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.murder_mystery.Client;

namespace Universes.UniverseData.murder_mystery.Editor {
    /// <summary>
    /// Rebuilds the murder mystery presentation prefabs (HUD and body marker) and makes sure the HUD is in the
    /// start scene. Run from Ryan/Murder Mystery in a client (non-server) Editor.
    /// </summary>
    public static class MM_Authoring {
        public const string Root = "Assets/Universes/UniverseData/murder_mystery";
        const string HudPath = Root + "/Prefabs/MM_HUD.prefab";
        const string MarkerPath = Root + "/Prefabs/MM_BodyMarker.prefab";
        const string StartScene = Root + "/murder_mystery_start.unity";
        const string QuickEffects = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/";
        const string Sounds = "Assets/Universes/UniverseData/classic_horror/Flooded_Grounds/Content/Sounds/";

        static readonly Color Ink = new(0.05f, 0.06f, 0.08f, 0.88f);
        static readonly Color Paper = new(0.95f, 0.94f, 0.9f);
        static readonly Color Muted = new(0.7f, 0.72f, 0.75f);
        static readonly Color Alarm = new(0.95f, 0.3f, 0.25f);
        static readonly Color Blood = new(0.9f, 0.08f, 0.06f, 0.85f);

        [MenuItem("Ryan/Murder Mystery/Rebuild HUD and Markers")]
        public static void BuildAll() {
            EnsureFolders();
            GameObject marker = BuildMarker();
            BuildHud(marker);
            AddHudToStartScene();
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolders() {
            foreach (string folder in new[] { "Prefabs", "Materials" })
                if (!AssetDatabase.IsValidFolder(Root + "/" + folder))
                    AssetDatabase.CreateFolder(Root, folder);
        }

        // ---------- body marker ----------

        static GameObject BuildMarker() {
            GameObject root = new("MM_BodyMarker");
            GameObject effect = PlaceEffect(QuickEffects + "vfx_LootDrop_01.prefab", root.transform, new Vector3(0, -0.9f, 0), 1.3f, Blood);
            effect.name = "Beacon";
            Light light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0, 0.4f, 0);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.15f, 0.1f);
            light.range = 5f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, MarkerPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        static GameObject PlaceEffect(string path, Transform parent, Vector3 position, float scale, Color tint) {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new System.InvalidOperationException("Missing murder mystery presentation asset: " + path);
            GameObject effect = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            effect.transform.localPosition = position;
            effect.transform.localRotation = Quaternion.identity;
            effect.transform.localScale = Vector3.one * scale;
            foreach (ParticleSystem particle in effect.GetComponentsInChildren<ParticleSystem>(true)) {
                ParticleSystem.MainModule main = particle.main;
                main.startColor = tint;
                main.loop = true;
                main.playOnAwake = true;
            }
            foreach (ParticleSystemRenderer renderer in effect.GetComponentsInChildren<ParticleSystemRenderer>(true)) {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && !materials[i].shader.name.StartsWith("Universal Render Pipeline/"))
                        materials[i] = UrpParticleMaterial(materials[i]);
                renderer.sharedMaterials = materials;
            }
            return effect;
        }

        static Material UrpParticleMaterial(Material source) {
            string path = Root + "/Materials/VFX_" + source.name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                throw new System.InvalidOperationException("URP particle shader is missing.");
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

        // ---------- HUD ----------

        static void BuildHud(GameObject marker) {
            GameObject root = new("MM_HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the spectate bar (5) and vote window (20), below the top bar (50).
            canvas.sortingOrder = 30;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            MM_ClientHUD hud = root.AddComponent<MM_ClientHUD>();
            Transform t = root.transform;

            // Role reveal card, centre.
            Image card = Panel("RoleCard", t, new Vector2(0.27f, 0.5f), new Vector2(0.73f, 0.78f), Ink);
            hud.roleCard = card.gameObject;
            hud.roleCardStripe = Panel("Stripe", card.transform, new Vector2(0, 0.96f), new Vector2(1, 1), Paper);
            Text("Kicker", card.transform, new Vector2(0, 0.76f), new Vector2(1, 0.94f), 22, Muted, TextAlignmentOptions.Center).text = "THE MYSTERY BEGINS";
            hud.roleCardTitle = Text("Title", card.transform, new Vector2(0, 0.42f), new Vector2(1, 0.78f), 50, Paper, TextAlignmentOptions.Center);
            hud.roleCardTitle.fontStyle = FontStyles.Bold;
            hud.roleCardGoal = Text("Goal", card.transform, new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.42f), 24, Paper, TextAlignmentOptions.Center);

            // Role badge, bottom left.
            Image badge = Panel("RoleBadge", t, new Vector2(0.012f, 0.2f), new Vector2(0.17f, 0.3f), Ink);
            hud.roleBadge = badge.gameObject;
            hud.roleBadgeText = Text("Text", badge.transform, Vector2.zero, Vector2.one, 26, Paper, TextAlignmentOptions.Left);

            // Report prompt, lower centre.
            Image prompt = Panel("ReportPrompt", t, new Vector2(0.32f, 0.2f), new Vector2(0.68f, 0.26f), new Color(0.35f, 0.04f, 0.04f, 0.9f));
            hud.reportPanel = prompt.gameObject;
            hud.reportText = Text("Text", prompt.transform, Vector2.zero, Vector2.one, 28, Paper, TextAlignmentOptions.Center);

            // Meeting notice, under the top bar so the vote window stays visible.
            Image meet = Panel("MeetingNotice", t, new Vector2(0.27f, 0.75f), new Vector2(0.73f, 0.9f), Ink);
            hud.meetingPanel = meet.gameObject;
            Panel("Stripe", meet.transform, new Vector2(0, 0), new Vector2(0.008f, 1), Alarm);
            hud.meetingText = Text("Text", meet.transform, new Vector2(0.02f, 0), Vector2.one, 25, Paper, TextAlignmentOptions.Center);

            // Ejection banner, centre.
            Image eject = Panel("Ejection", t, new Vector2(0.25f, 0.42f), new Vector2(0.75f, 0.6f), Ink);
            hud.ejectionPanel = eject.gameObject;
            hud.ejectionStripe = Panel("Stripe", eject.transform, new Vector2(0, 0.93f), new Vector2(1, 1), Paper);
            hud.ejectionTitle = Text("Title", eject.transform, new Vector2(0, 0.45f), new Vector2(1, 0.9f), 50, Paper, TextAlignmentOptions.Center);
            hud.ejectionTitle.fontStyle = FontStyles.Bold;
            hud.ejectionBody = Text("Body", eject.transform, new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.45f), 28, Paper, TextAlignmentOptions.Center);

            // End-of-round reveal.
            Image res = Panel("Results", t, new Vector2(0.22f, 0.12f), new Vector2(0.78f, 0.86f), new Color(0.04f, 0.05f, 0.07f, 0.95f));
            hud.resultsPanel = res.gameObject;
            Text("Kicker", res.transform, new Vector2(0.04f, 0.9f), new Vector2(0.96f, 0.97f), 22, Muted, TextAlignmentOptions.Center).text = "ROUND OVER  -  ROLES REVEALED";
            hud.resultsTitle = Text("Winner", res.transform, new Vector2(0.04f, 0.8f), new Vector2(0.96f, 0.91f), 56, Paper, TextAlignmentOptions.Center);
            hud.resultsTitle.fontStyle = FontStyles.Bold;
            hud.resultsSummary = Text("Summary", res.transform, new Vector2(0.04f, 0.74f), new Vector2(0.96f, 0.8f), 24, Muted, TextAlignmentOptions.Center);
            hud.resultsBody = Text("Table", res.transform, new Vector2(0.06f, 0.1f), new Vector2(0.94f, 0.72f), 26, Paper, TextAlignmentOptions.TopLeft);
            hud.resultsBody.overflowMode = TextOverflowModes.Truncate;
            hud.resultsFooter = Text("Footer", res.transform, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.09f), 22, Muted, TextAlignmentOptions.Center);

            hud.bodyMarkerPrefab = marker;
            GameObject audio = new("Sfx");
            audio.transform.SetParent(t, false);
            hud.sfx = audio.AddComponent<AudioSource>();
            hud.sfx.playOnAwake = false;
            hud.sfx.spatialBlend = 0f;
            hud.sfx.volume = 0.6f;
            hud.meetingClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Sounds + "Horn.mp3");
            hud.ejectionClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Sounds + "Taps.mp3");

            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
            Object.DestroyImmediate(root);
        }

        static void AddHudToStartScene() {
            var scene = EditorSceneManager.GetSceneByPath(StartScene);
            bool opened = !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(StartScene, OpenSceneMode.Additive);
            try {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
                foreach (GameObject rootObject in scene.GetRootGameObjects())
                    if (PrefabUtility.GetCorrespondingObjectFromSource(rootObject) == prefab)
                        return;
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = "MM_HUD";
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            } finally {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max) {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static Image Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color) {
            Image image = Rect(name, parent, min, max).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static TextMeshProUGUI Text(string name, Transform parent, Vector2 min, Vector2 max, float size, Color color, TextAlignmentOptions alignment) {
            RectTransform rect = Rect(name, parent, min, max);
            rect.offsetMin = new Vector2(18, 8);
            rect.offsetMax = new Vector2(-18, -8);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Round(size * 0.6f);
            text.fontSizeMax = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.Normal;
            // LiberationSans SDF has no ellipsis glyph, so overflow is truncated.
            text.overflowMode = TextOverflowModes.Truncate;
            text.text = "";
            return text;
        }
    }
}
