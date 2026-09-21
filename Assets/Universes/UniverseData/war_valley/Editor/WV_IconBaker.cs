using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace Universes.UniverseData.war_valley.Editor {
    /// <summary>
    /// Renders shop and production-menu icons from the models the prefabs already use.
    /// <para>
    /// The War Valley structures shipped with no <c>StructureComponent.Sprite</c> at all, which is
    /// why every card in the build menu drew an empty frame. Rather than hand-drawing art, each icon
    /// is baked from the same Cartoon Military model the structure is built from, so an icon can
    /// never drift from the thing it represents and a re-import of the pack re-bakes cleanly.
    /// </para>
    /// <para>
    /// <see cref="AssetPreview"/> is deliberately not used: it returns null until Unity's own
    /// preview queue happens to have finished, which makes a rebuild non-deterministic. This renders
    /// an explicit camera into a <see cref="RenderTexture"/> instead.
    /// </para>
    /// </summary>
    public static class WV_IconBaker {
        const int IconSize = 256;
        /// <summary>Camera direction, chosen so a building reads as a three-quarter view.</summary>
        static readonly Vector3 ViewDirection = new Vector3(-0.55f, 0.42f, -1f);

        public static string IconFolder => WV_Authoring.Root + "/Presentation/Icons";

        public static void EnsureFolder() {
            if (!AssetDatabase.IsValidFolder(WV_Authoring.Root + "/Presentation"))
                AssetDatabase.CreateFolder(WV_Authoring.Root, "Presentation");
            if (!AssetDatabase.IsValidFolder(IconFolder))
                AssetDatabase.CreateFolder(WV_Authoring.Root + "/Presentation", "Icons");
        }

        /// <summary>
        /// Bakes <paramref name="model"/> to <c>Icons/{name}.png</c> and returns the imported sprite.
        /// Returns null if the model has nothing to render, so a caller can leave the icon unset
        /// rather than assigning a blank square.
        /// </summary>
        public static Sprite Bake(GameObject model, string iconName) {
            if (model == null)
                return null;

            EnsureFolder();
            string path = $"{IconFolder}/{iconName}.png";

            Scene stage = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject instance = null;
            GameObject rig = null;
            RenderTexture target = null;
            RenderTexture previousActive = RenderTexture.active;

            try {
                instance = Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(instance, stage);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                // A skinned rig outside Play Mode never updates its bones, so its renderer reports
                // stale bounds and Unity culls it - which is why the infantry icon baked empty.
                foreach (SkinnedMeshRenderer skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    skinned.updateWhenOffscreen = true;

                if (!TryGetRenderBounds(instance, out Bounds bounds))
                    return null;

                rig = new GameObject("IconRig");
                SceneManager.MoveGameObjectToScene(rig, stage);

                // A model with no light renders black, so the stage carries its own key light.
                var light = new GameObject("Key").AddComponent<Light>();
                light.transform.SetParent(rig.transform, false);
                light.type = LightType.Directional;
                light.intensity = 1.25f;
                light.transform.rotation = Quaternion.Euler(38f, 152f, 0f);

                var camera = new GameObject("IconCamera").AddComponent<Camera>();
                camera.transform.SetParent(rig.transform, false);
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                // Transparent so the icon sits on the card's own frame rather than on a grey tile.
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.cullingMask = ~0;

                float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
                Vector3 direction = ViewDirection.normalized;
                camera.transform.position = bounds.center - direction * (radius * 3f);
                camera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = radius * 8f;
                // Fit to what the camera actually sees rather than to the bounds' diagonal. A tall
                // thin model like the watchtower would otherwise be sized by a diagonal it never
                // fills and bake as a sliver in the middle of an empty icon.
                camera.orthographicSize = GetFramingSize(camera, bounds);

                target = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32) {
                    antiAliasing = 8
                };
                camera.targetTexture = target;
                camera.Render();

                RenderTexture.active = target;
                var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0f, 0f, IconSize, IconSize), 0, 0);
                texture.Apply();

                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                camera.targetTexture = null;
            } finally {
                RenderTexture.active = previousActive;
                if (target != null) {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                if (rig != null)
                    Object.DestroyImmediate(rig);
                if (instance != null)
                    Object.DestroyImmediate(instance);
                EditorSceneManager.CloseScene(stage, true);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ApplySpriteImportSettings(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Half-height of the smallest orthographic box that contains every corner of
        /// <paramref name="bounds"/> in this camera's view, with a small margin.
        /// </summary>
        static float GetFramingSize(Camera camera, Bounds bounds) {
            float maxY = 0.01f;
            float maxX = 0.01f;
            for (int corner = 0; corner < 8; corner++) {
                var offset = new Vector3(
                    (corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                    (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                    (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                Vector3 view = camera.transform.InverseTransformPoint(bounds.center + offset);
                maxY = Mathf.Max(maxY, Mathf.Abs(view.y));
                maxX = Mathf.Max(maxX, Mathf.Abs(view.x));
            }
            // The icon is square, so the wider axis is the one that decides the fit.
            return Mathf.Max(maxY, maxX) * 1.06f;
        }

        static void ApplySpriteImportSettings(string path) {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        static bool TryGetRenderBounds(GameObject instance, out Bounds bounds) {
            bounds = default;
            bool found = false;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true)) {
                // Particle and trail renderers have no meaningful rest bounds on a static model.
                if (renderer is not (MeshRenderer or SkinnedMeshRenderer))
                    continue;
                if (!found) {
                    bounds = renderer.bounds;
                    found = true;
                } else {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }
    }
}
