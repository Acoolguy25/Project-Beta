using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Universes.UniverseData.tower_defense.Editor {
    /// <summary>
    /// Renders transparent three-quarter portraits of every tower and enemy prefab into UI/Portraits,
    /// used by the shop cards, the tower card and the next-wave preview. Needs a GPU (skipped in
    /// -nographics batch runs; the HUD then falls back to the flat icons).
    /// </summary>
    public static class TD_Portraits {
        const int Size = 256;
        /// <summary>Children hidden while rendering (labels, bars, rings, shadows).</summary>
        static readonly string[] Hidden = { "Badge", "RangeRing", "HealthBar", "FrostShell", "Shadow", "FireSound" };

        [MenuItem("Ryan/Tower Defense/Render Portraits")]
        public static void RenderAll() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string folder = TD_Authoring.Root + "/UI/Portraits";
            Directory.CreateDirectory(folder);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.64f, 0.7f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.5f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.transform.rotation = Quaternion.Euler(40f, -140f, 0f);
            var cam = new GameObject("PortraitCamera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 26f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            foreach (TD_TowerKind kind in System.Enum.GetValues(typeof(TD_TowerKind)))
                Render(cam, $"{TD_Authoring.Root}/Prefabs/Towers/{TD_Rules.Tower(kind).name}.prefab", $"{folder}/tower_{kind.ToString().ToLowerInvariant()}.png", 25f);
            foreach (TD_EnemyKind kind in System.Enum.GetValues(typeof(TD_EnemyKind)))
                Render(cam, $"{TD_Authoring.Root}/Prefabs/Enemies/{TD_Rules.Enemy(kind).name}.prefab", $"{folder}/enemy_{kind.ToString().ToLowerInvariant()}.png", 20f);
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(sun.gameObject);
            AssetDatabase.Refresh();
            TD_HudAuthoring.ImportIcons();
        }

        static void Render(Camera cam, string prefabPath, string output, float pitch) {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) {
                Debug.LogWarning("[TowerDefense] No prefab for portrait: " + prefabPath);
                return;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != go.transform && System.Array.IndexOf(Hidden, t.name) >= 0) t.gameObject.SetActive(false);
            go.transform.rotation = Quaternion.Euler(0f, 210f, 0f);
            var bounds = RyanAssets.Authoring.UniverseAuthoringKit.RendererBounds(go);
            float radius = bounds.extents.magnitude;
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.92f;
            cam.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
            cam.transform.position = bounds.center - cam.transform.forward * distance;

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new UnityEngine.Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            File.WriteAllBytes(output, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }
    }
}
