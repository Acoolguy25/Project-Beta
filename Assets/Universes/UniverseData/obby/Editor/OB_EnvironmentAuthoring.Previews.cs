#if !UNITY_SERVER
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Universes.UniverseData.obby.Client;

namespace Universes.UniverseData.obby.Editor {
    public static partial class OB_EnvironmentAuthoring {
        const string StartScenePath = Root + "/obby_start.unity";
        const int PreviewSeed = 20261115;
        /// <summary>Three stages per tier, so every tier's world shows up.</summary>
        const int PreviewStages = OB_Tiers.Count * 3;
        const int ShotWidth = 1280, ShotHeight = 720;
        const float ShotFieldOfView = 55f, ShotFarClip = 5000f;
        /// <summary>Seconds of weather simulated before each shot, so the air is full.</summary>
        const float WeatherWarmup = 8f;

        /// <summary>
        /// Renders every tier's world for review: a wide view along its stages, a checkpoint close-up (arch,
        /// banners, lamps), the rock and towers under its platforms, its landmarks and its horizon, plus an
        /// overview of the whole course. Writes PNGs to Logs/obby-previews/environment. Run after
        /// "Build Client Assets".
        /// </summary>
        [MenuItem("Ryan/Obby/Render Environment Previews")]
        public static void RenderEnvironmentPreviews() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(StartScenePath, OpenSceneMode.Single);
            var course = Object.FindAnyObjectByType<OB_Course>();
            if (course == null || course.clientPrefab == null) throw new System.InvalidOperationException("Build Client Assets first: the start scene needs its course and client prefab.");
            var layout = course.Build(PreviewSeed, PreviewStages);
            var client = (GameObject)PrefabUtility.InstantiatePrefab(course.clientPrefab);
            var backdrop = client.GetComponentInChildren<OB_Backdrop>();
            backdrop.Arrange(layout, course.LobbySpawn);
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/obby-previews/environment"));
            Directory.CreateDirectory(outDir);
            var cam = PreviewCamera();
            try {
                for (int t = 0; t < OB_Tiers.Count; t++) {
                    var tier = (OB_Tier)t;
                    int first = System.Array.FindIndex(layout.stages, s => s.tier == tier);
                    if (first < 0) continue;
                    int last = System.Array.FindLastIndex(layout.stages, s => s.tier == tier);
                    PreviewTier(backdrop, tier);
                    TierShots(cam, backdrop, layout, first, last, Path.Combine(outDir, $"{t}-{tier}"));
                }
                PreviewTier(backdrop, OB_Tier.Basic);
                Bounds all = backdrop.Clearance.CourseBounds;
                Vector3 across = Vector3.Cross(Vector3.up, Flat(layout.finishTop - course.LobbySpawn));
                Shot(cam, backdrop, all.center - across * (all.extents.magnitude * 1.4f) + Vector3.up * (all.extents.magnitude * 0.5f), all.center, Path.Combine(outDir, "course-overview.png"));
            } finally {
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(client);
                course.Clear();
                // Leave the start scene as it was saved (the atmosphere swaps its sky while previewing).
                EditorSceneManager.OpenScene(StartScenePath, OpenSceneMode.Single);
            }
            Debug.Log($"Environment previews written to {outDir}");
        }

        static void TierShots(Camera cam, OB_Backdrop backdrop, OB_CourseLayout layout, int first, int last, string prefix) {
            Vector3 start = layout.stages[first].spawn;
            Vector3 end = last + 1 < layout.StageCount ? layout.stages[last + 1].spawn : layout.finishTop;
            Vector3 along = Flat(end - start), side = Vector3.Cross(Vector3.up, along);
            Vector3 mid = (start + end) * 0.5f;
            float span = Mathf.Max(40f, Vector3.Distance(start, end));

            // 1. The tier's stretch of course in its world: from behind and above, landmarks beyond.
            Shot(cam, backdrop, start - along * (span * 0.35f) - side * (span * 0.3f) + Vector3.up * (span * 0.25f), mid, prefix + "-overview.png");
            // 2. A checkpoint pad: arch, banners, lamps, the ropes running on.
            Vector3 pad = layout.stages[Mathf.Min(first + 1, last)].spawn;
            Shot(cam, backdrop, pad - along * 16f + side * 7f + Vector3.up * 7f, pad + Vector3.up * 4f + along * 4f, prefix + "-checkpoint.png");
            // 3. Under the course: keels and towers hanging from the platforms.
            Shot(cam, backdrop, mid - side * 45f - Vector3.up * 30f, mid - Vector3.up * 12f, prefix + "-underside.png");
            // 4. The tier's landmarks, seen from the course.
            int shot = 0;
            foreach (var placement in backdrop.ScenePlacements) {
                if (placement.kind != (byte)OB_SceneryKind.Landmark || placement.tier != (OB_Tier)layout.stages[first].tier) continue;
                Vector3 landmark = placement.matrix.GetColumn(3);
                Vector3 eye = Nearest(layout, first, last, landmark) + Vector3.up * 8f;
                Shot(cam, backdrop, eye, landmark + Vector3.up * 70f, $"{prefix}-landmark{shot++}.png");
            }
            // 5. Out to the horizon and sky, away from the course.
            Shot(cam, backdrop, mid + Vector3.up * 6f, mid + Vector3.up * 40f + side * 400f, prefix + "-horizon.png");
            Shot(cam, backdrop, mid + Vector3.up * 6f, mid + Vector3.up * 60f - side * 400f + along * 200f, prefix + "-sky.png");
        }

        static Vector3 Nearest(OB_CourseLayout layout, int first, int last, Vector3 to) {
            Vector3 best = layout.stages[first].spawn;
            for (int s = first; s <= last; s++)
                if ((layout.stages[s].spawn - to).sqrMagnitude < (best - to).sqrMagnitude) best = layout.stages[s].spawn;
            return best;
        }

        static Vector3 Flat(Vector3 v) {
            v.y = 0f;
            return v.sqrMagnitude > 0.01f ? v.normalized : Vector3.forward;
        }

        static Camera PreviewCamera() {
            var cam = new GameObject("EnvironmentPreviewCamera", typeof(Camera)).GetComponent<Camera>();
            cam.fieldOfView = ShotFieldOfView;
            cam.farClipPlane = ShotFarClip;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            return cam;
        }

        /// <summary>Switches the whole world (sky, light, horizon, weather) to <paramref name="tier"/> at once.</summary>
        public static void PreviewTier(OB_Backdrop backdrop, OB_Tier tier) {
            // Components do not run in edit mode, so the tier event is not wired: set each part directly.
            if (backdrop.atmosphere != null) backdrop.atmosphere.ShowTier(tier);
            if (backdrop.horizon != null) backdrop.horizon.SetTier(tier, true);
            if (backdrop.weather != null) backdrop.weather.SetTier(tier, true);
        }

        /// <summary>
        /// What a preview render needs right before it, for <see cref="OB_Authoring"/>'s course previews too:
        /// the horizon centred on the camera, weather around it, and the instanced layers queued for it.
        /// </summary>
        public static void PrepareShot(OB_Backdrop backdrop, Camera cam) {
            if (backdrop == null || cam == null) return;
            backdrop.Follow(cam.transform.position);
            if (backdrop.horizon != null) backdrop.horizon.Follow(cam.transform.position);
            if (backdrop.weather != null) backdrop.weather.Preview(cam, WeatherWarmup);
            backdrop.DrawNow(cam);
        }

        static void Shot(Camera cam, OB_Backdrop backdrop, Vector3 position, Vector3 target, string path) {
            cam.transform.position = position;
            cam.transform.LookAt(target);
            PrepareShot(backdrop, cam);
            var rt = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(texture);
        }
    }
}
#endif
