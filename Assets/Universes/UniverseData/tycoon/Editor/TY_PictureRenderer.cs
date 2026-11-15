using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Universes.UniverseData.tycoon.Editor {
    /// <summary>
    /// Renders a picture of every catalog item's prefab (transparent background) to
    /// <c>UI/Pictures/&lt;index&gt;_&lt;Name&gt;.png</c>, imported as sprites for the HUD build list and the
    /// buy-pad signs. Needs a graphics device; in -nographics batchmode the existing pictures are kept.
    /// </summary>
    public static class TY_PictureRenderer {
        public const string Folder = TY_Authoring.Root + "/UI/Pictures";
        const int Size = 256;
        static readonly Vector3 ViewDirection = new Vector3(0.75f, 0.62f, 1f).normalized;

        /// <summary>Name of an optional child to frame instead of the whole prefab (e.g. one lamp of a set).</summary>
        public const string FocusName = "PictureFocus";

        public static string PathFor(int item) => $"{Folder}/{item:00}_{TY_Catalog.Items[item].name.Replace(" ", "").Replace("-", "")}.png";

        public static void RenderAll(GameObject[] itemPrefabs) {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) {
                Debug.LogWarning("[Tycoon] No graphics device: keeping the existing item pictures.");
                return;
            }
            Directory.CreateDirectory(Folder);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.75f, 0.8f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.3f, 0.32f);
            var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.4f;
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            var cam = new GameObject("PictureCamera", typeof(Camera)).GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            cam.allowHDR = false;
            // Async compilation would draw not-yet-compiled shaders as placeholders in the first pictures.
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            // The very first render after opening the scene comes out unlit, so it is done twice.
            bool warmedUp = false;
            for (int i = 0; i < itemPrefabs.Length; i++) {
                if (itemPrefabs[i] == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(itemPrefabs[i]);
                // Floating labels would crowd the picture.
                foreach (var text in go.GetComponentsInChildren<TMPro.TMP_Text>(true)) text.gameObject.SetActive(false);
                foreach (var light in go.GetComponentsInChildren<Light>(true)) light.enabled = false;
                Transform focus = go.transform.Find(FocusName);
                Bounds bounds = VisibleBounds(focus != null ? focus.gameObject : go);
                float radius = bounds.extents.magnitude;
                float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.92f;
                cam.transform.position = bounds.center + ViewDirection * distance;
                cam.transform.LookAt(bounds.center);
                if (!warmedUp) { Save(cam, PathFor(i)); warmedUp = true; }
                Save(cam, PathFor(i));
                Object.DestroyImmediate(go);
            }
            ShaderUtil.allowAsyncCompilation = async;
            AssetDatabase.Refresh();
            for (int i = 0; i < itemPrefabs.Length; i++) {
                if (itemPrefabs[i] == null || !File.Exists(PathFor(i))) continue;
                var importer = (TextureImporter)AssetImporter.GetAtPath(PathFor(i));
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        static Bounds VisibleBounds(GameObject go) {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.one);
            foreach (var r in renderers) if (r.enabled && r.gameObject.activeInHierarchy) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        static void Save(Camera cam, string path) {
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

        /// <summary>The item pictures, index-aligned with the catalog (null where an item has no picture).</summary>
        public static Sprite[] LoadAll() {
            var sprites = new Sprite[TY_Catalog.Count];
            for (int i = 0; i < sprites.Length; i++) sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(PathFor(i));
            return sprites;
        }
    }
}
