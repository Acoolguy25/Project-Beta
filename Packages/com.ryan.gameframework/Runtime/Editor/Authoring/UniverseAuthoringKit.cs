using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RyanAssets.Authoring {
    /// <summary>
    /// Editor helpers shared by the universe authoring scripts (Sky Obby, Brick Tycoon, Tower Defense,
    /// Hide and Seek, Flag Wars): generated textures, URP materials, prefab pieces, screen-space HUD
    /// widgets, scene registration, server-runner scenes and preview renders. Everything is written in
    /// place, so re-running an authoring script keeps asset GUIDs and the references to them.
    /// </summary>
    public sealed class UniverseAuthoringKit {
        public readonly string Root;
        public UniverseAuthoringKit(string root) => Root = root.TrimEnd('/');

        public string StartScene(string universe) => $"{Root}/{universe}_start.unity";
        public string ServerScene(string universe) => $"{Root}/{universe}_server.unity";

        public void EnsureFolders(params string[] folders) {
            foreach (string folder in folders) {
                string path = Root;
                foreach (string part in folder.Split('/')) {
                    string next = path + "/" + part;
                    if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(path, part);
                    path = next;
                }
            }
        }

        // ---------------------------------------------------------------- textures
        public delegate Color Pixel(float u, float v);

        public Texture2D SaveTexture(string name, Pixel pixel, int size = 128) {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, pixel((x + 0.5f) / size, (y + 0.5f) / size));
            tex.Apply();
            string path = $"{Root}/Textures/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{name}.png");

        public static float Hash(float x, float y) {
            float h = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>Studded plastic tile with a bevelled edge, the look shared by the brick games.</summary>
        public static Color Studded(float u, float v) {
            float edge = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
            float shade = edge < 0.04f ? 0.62f : edge < 0.08f ? 0.82f : 1f;
            float inner = Mathf.Abs(Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) - 0.25f) < 0.012f ? 0.9f : 1f;
            float c = shade * inner * (0.96f + 0.04f * Hash(u, v));
            return new Color(c, c, c, 1);
        }

        // ---------------------------------------------------------------- materials
        public Material Mat(string name, Color color, Texture2D texture = null, float emission = 0f, float alpha = 1f, float smoothness = 0.25f, float metallic = 0f) {
            string path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            color.a = alpha;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", texture);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (emission > 0f) {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * emission);
                mat.SetTexture("_EmissionMap", texture);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            } else {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
            bool transparent = alpha < 1f;
            mat.SetFloat("_Surface", transparent ? 1f : 0f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            mat.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", transparent ? 0f : 1f);
            mat.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            mat.renderQueue = transparent ? (int)RenderQueue.Transparent : -1;
            if (transparent) mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Sets a material's texture tiling (for long walls, belts and floors) and returns it.</summary>
        public static Material Tiled(Material mat, float x, float y) {
            mat.SetTextureScale("_BaseMap", new Vector2(x, y));
            if (mat.HasTexture("_EmissionMap")) mat.SetTextureScale("_EmissionMap", new Vector2(x, y));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------------------------------------------------------- pieces
        public static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider = true) =>
            Primitive(PrimitiveType.Cube, name, parent, position, scale, material, collider);

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider = true) {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static GameObject Empty(string name, Transform parent, Vector3 position = default) {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go;
        }

        public static TextMeshPro WorldText(string name, Transform parent, Vector3 position, string text, float size, Color color, Vector2 box, float yaw = 0f) {
            var go = new GameObject(name, typeof(TextMeshPro));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var tmp = go.GetComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = new Color32(0, 0, 0, 180);
            tmp.rectTransform.sizeDelta = box;
            return tmp;
        }

        /// <summary>Saves <paramref name="go"/> as <c>Root/Prefabs/&lt;folder&gt;/&lt;name&gt;.prefab</c> and destroys the scene copy.</summary>
        public GameObject Save(GameObject go, string folder, string name = null) {
            string path = $"{Root}/Prefabs/{(string.IsNullOrEmpty(folder) ? "" : folder + "/")}{name ?? go.name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            if (prefab == null) throw new InvalidOperationException("Could not save " + path);
            return prefab;
        }

        public static void NoShadows(GameObject go) {
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        public static Bounds RendererBounds(GameObject go) {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        /// <summary>
        /// Instantiates a model asset under <paramref name="parent"/>, scaled so its largest side is
        /// <paramref name="size"/> metres and its base sits on the parent's origin. Returns null when the
        /// asset is missing (callers fall back to primitives).
        /// </summary>
        public static GameObject Model(string assetPath, Transform parent, float size, Vector3 offset = default, float yaw = 0f, bool keepColliders = false) {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (model == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            if (!keepColliders) foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.localRotation;
            var bounds = RendererBounds(go);
            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest > 0.0001f) go.transform.localScale *= size / largest;
            bounds = RendererBounds(go);
            Vector3 anchor = parent != null ? parent.position : Vector3.zero;
            go.transform.position += anchor + offset - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            return go;
        }

        // ---------------------------------------------------------------- HUD
        public static readonly Color Ink = new(0.05f, 0.07f, 0.12f, 0.72f);

        public static GameObject Canvas(string name, int sortingOrder = 30) {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return root;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static UnityEngine.UI.Image Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color) {
            var image = Rect(name, parent, min, max).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static TMP_Text Text(string name, Transform parent, Vector2 min, Vector2 max, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false) {
            var text = Rect(name, parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            if (bold) text.fontStyle = FontStyles.Bold;
            text.outlineWidth = 0.18f;
            text.outlineColor = new Color32(0, 0, 0, 200);
            return text;
        }

        public static AudioSource Sfx(Transform parent) {
            var audio = new GameObject("Sfx");
            audio.transform.SetParent(parent, false);
            var source = audio.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        public static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        public const string CoinClip = "Packages/com.ryan.gameframework/Runtime/Items/Coin/freesound_gamestudio-drop-coin-384921.mp3";
        public const string LandClip = "Assets/UnityTechnologies/SpaceRobotKyle/Sfx/Player_Land.wav";

        // ---------------------------------------------------------------- scenes
        /// <summary>Opens a fresh empty scene with a sun, trilight ambient and fog. Returns the sun.</summary>
        public Light NewLitScene(Color skyTint, Color fog, float fogStart, float fogEnd, Vector3 sunEuler, float sunIntensity = 1.3f) {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sky = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/Sky.mat");
            if (sky == null) {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, $"{Root}/Materials/Sky.mat");
            }
            sky.SetColor("_SkyTint", skyTint);
            sky.SetColor("_GroundColor", Color.Lerp(skyTint, Color.white, 0.3f));
            sky.SetFloat("_AtmosphereThickness", 0.8f);
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(skyTint, Color.white, 0.35f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.6f, 0.65f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.36f, 0.4f);
            RenderSettings.fog = fogEnd > 0f;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
            var sun = new GameObject("Sun", typeof(Light));
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = sunIntensity;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(sunEuler);
            RenderSettings.sun = light;
            return light;
        }

        public void SaveActiveScene(string path) => EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path);

        /// <summary>
        /// Clients load universe scenes from the shared scene list; the Linux Server profile overrides it
        /// with its own list, which also carries the server-only scene.
        /// </summary>
        public static void RegisterScenes(string startScene, string serverScene) {
            if (File.Exists(startScene) && EditorBuildSettings.scenes.All(s => s.path != startScene))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Append(new EditorBuildSettingsScene(startScene, true)).ToArray();
            var wanted = new[] { startScene, serverScene }.Where(File.Exists).ToArray();
            foreach (string guid in AssetDatabase.FindAssets("t:BuildProfile", new[] { "Assets/Settings/Build Profiles" })) {
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile == null || !new SerializedObject(profile).FindProperty("m_OverrideGlobalSceneList").boolValue) continue;
                var scenes = profile.scenes.ToList();
                int before = scenes.Count;
                foreach (string path in wanted)
                    if (scenes.All(s => s.path != path)) scenes.Add(new EditorBuildSettingsScene(path, true));
                if (scenes.Count == before) continue;
                profile.scenes = scenes.ToArray();
                EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Reads (value null) or sets ParrelSync's cached "block asset saving in clones" preference.</summary>
        public static bool? CloneSaveBlock(bool? value) {
            var prefs = Type.GetType("ParrelSync.Preferences, ParrelSync");
            var pref = prefs?.GetField("AssetModPref", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
            var property = pref?.GetType().GetProperty("Value");
            if (property == null) return null;
            if (value.HasValue) property.SetValue(pref, value.Value);
            return (bool)property.GetValue(pref);
        }

        /// <summary>
        /// Saves a server runner prefab holding <typeparamref name="T"/> plus a server scene that
        /// instantiates it. Call from an Editor in the Linux Server profile (the runner only compiles
        /// with UNITY_SERVER); the ParrelSync clone's save block is lifted for the duration.
        /// </summary>
        public void BuildServerRunner<T>(string prefabPath, string serverScene, Action<T> configure = null) where T : MonoBehaviour {
            bool? blocked = CloneSaveBlock(null);
            CloneSaveBlock(false);
            try {
                var go = new GameObject(typeof(T).Name);
                var runner = go.AddComponent<T>();
                // ServerRunner's DebugBool switches default to on; ship real-time timers like the other universes.
                var serialized = new SerializedObject(runner);
                foreach (string flag in new[] { "DebugTimerSpeedUp._editor_value", "DebugTimerInfinite._editor_value" }) {
                    var property = serialized.FindProperty(flag);
                    if (property != null) property.boolValue = false;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                configure?.Invoke(runner);
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                Object.DestroyImmediate(go);
                if (prefab == null) throw new InvalidOperationException("Could not save " + prefabPath);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                PrefabUtility.InstantiatePrefab(prefab, scene);
                EditorSceneManager.SaveScene(scene, serverScene);
                AssetDatabase.SaveAssets();
            } finally {
                if (blocked.HasValue) CloneSaveBlock(blocked.Value);
            }
        }

        // ---------------------------------------------------------------- previews
        public static void Shoot(Camera cam, Vector3 position, Vector3 target, string path, int width, int height) {
            cam.transform.position = position;
            cam.transform.LookAt(target);
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

        public static Camera PreviewCamera(float fov = 55f) {
            var cam = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>();
            cam.fieldOfView = fov;
            cam.farClipPlane = 1500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            return cam;
        }

        /// <summary>Copies a rendered preview to Resources/UniverseDataPub/&lt;universe&gt;/thumbnail.png (the game browser card).</summary>
        public void PublishThumbnail(string universe, string renderedPng) {
            EnsureFolders("Resources", "Resources/UniverseDataPub", $"Resources/UniverseDataPub/{universe}");
            string path = $"{Root}/Resources/UniverseDataPub/{universe}/thumbnail.png";
            File.Copy(renderedPng, path, true);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        public static string PreviewDir(string universe) {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, $"../Logs/{universe}-previews"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
