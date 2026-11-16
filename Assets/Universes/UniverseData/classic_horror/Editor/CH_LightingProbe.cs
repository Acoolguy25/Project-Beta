using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Universes.UniverseData.classic_horror.Editor {
    /// <summary>
    /// Renders the horror map from the arrival jetty without entering Play mode or starting a server,
    /// with and without post-processing, plus one practical lamp, and logs what the renderer and RenderSettings actually were.
    /// Separates "the GPU draws this scene wrongly" from "the scene is lit wrongly" from "the live
    /// client is not using the map's RenderSettings". Batchmode-safe (needs a display, not -nographics):
    /// <c>-executeMethod Universes.UniverseData.classic_horror.Editor.CH_LightingProbe.Capture</c>
    /// </summary>
    public static class CH_LightingProbe {
        const string ScenePath = CH_Authoring.Root + "/Scenes/classic_horror_start.unity";
        const int Width = 960, Height = 540;
        const float EyeHeight = 1.7f, LampViewDistance = 7f;

        [MenuItem("Ryan/Classic Horror/Diagnostics/Capture Lighting Probe")]
        public static void Capture() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode before capturing the lighting probe.");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            // Hidden and never saved, so the probe leaves no scene marked as modified.
            var camera = new GameObject("CH Lighting Probe") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
            try {
                SceneManager.SetActiveScene(scene);
                CH_Map map = Object.FindAnyObjectByType<CH_Map>();
                if (map == null || map.arrival == null) throw new System.InvalidOperationException("The horror map has no CH_Map arrival point.");
                Vector3 eye = map.arrival.position + Vector3.up * EyeHeight;
                Vector3 target = map.extraction != null && (map.extraction.position - map.arrival.position).sqrMagnitude > 1f
                    ? map.extraction.position + Vector3.up * EyeHeight
                    : eye + map.arrival.forward;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.allowHDR = true;
                camera.farClipPlane = 600f;
                var data = camera.GetUniversalAdditionalCameraData();

                var report = new StringBuilder();
                report.AppendLine($"GPU: {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceVersion}");
                report.AppendLine($"Active scene: {SceneManager.GetActiveScene().name}");
                report.AppendLine($"Skybox: {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")}, ambient {RenderSettings.ambientMode} {RenderSettings.ambientLight}, "
                    + $"fog {RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogColor} density {RenderSettings.fogDensity}");
                report.AppendLine($"Sun: {(RenderSettings.sun != null ? $"{RenderSettings.sun.color} x{RenderSettings.sun.intensity}" : "none")}");
                foreach (bool post in new[] { false, true }) {
                    data.renderPostProcessing = post;
                    report.AppendLine(Render(camera, post ? "post" : "nopost"));
                }
                // A practical lamp's pool of warm light against the cool night, seen from a few steps away.
                Light lamp = map.practicalLights != null ? System.Array.Find(map.practicalLights, l => l != null) : null;
                if (lamp != null) {
                    Vector3 spot = lamp.transform.parent != null ? lamp.transform.parent.position : lamp.transform.position;
                    Vector3 away = (spot - lamp.transform.position); away.y = 0f;
                    if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                    Vector3 lampEye = spot + away.normalized * LampViewDistance + Vector3.up * EyeHeight * 1.4f;
                    camera.transform.SetPositionAndRotation(lampEye, Quaternion.LookRotation(lamp.transform.position + Vector3.down * 1.2f - lampEye));
                    data.renderPostProcessing = true;
                    report.AppendLine(Render(camera, "lamp"));
                    report.AppendLine($"Lamp at {lamp.transform.parent?.name}: {lamp.color} x{lamp.intensity} range {lamp.range}");
                }
                Debug.Log("Classic Horror lighting probe\n" + report);
            } finally {
                Object.DestroyImmediate(camera.gameObject);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>Renders into a float target, saves an sRGB PNG and returns linear statistics per screen band.</summary>
        static string Render(Camera camera, string label) {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(Width, Height, TextureFormat.RGBAHalf, false, true);
            try {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                readback.Apply();
                Color[] pixels = readback.GetPixels();
                string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", $"ch-lighting-probe-{label}.png");
                File.WriteAllBytes(path, Encode(pixels));
                return $"{label}: sky {Band(pixels, 2f / 3f, 1f)} | horizon {Band(pixels, 1f / 3f, 2f / 3f)} | ground {Band(pixels, 0f, 1f / 3f)} -> {path}";
            } finally {
                camera.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(readback);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>Mean and maximum linear colour of the rows between two heights (0 = bottom).</summary>
        static string Band(Color[] pixels, float from, float to) {
            Color sum = Color.black, max = Color.black;
            int count = 0;
            for (int y = Mathf.FloorToInt(from * Height); y < Mathf.FloorToInt(to * Height); y++)
                for (int x = 0; x < Width; x++) {
                    Color c = pixels[y * Width + x];
                    sum += c;
                    max = new Color(Mathf.Max(max.r, c.r), Mathf.Max(max.g, c.g), Mathf.Max(max.b, c.b));
                    count++;
                }
            Color mean = sum / Mathf.Max(1, count);
            return $"mean ({mean.r:0.000}, {mean.g:0.000}, {mean.b:0.000}) max ({max.r:0.00}, {max.g:0.00}, {max.b:0.00})";
        }

        static byte[] Encode(Color[] linear) {
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try {
                var srgb = new Color32[linear.Length];
                for (int i = 0; i < linear.Length; i++) srgb[i] = (Color32)linear[i].gamma;
                image.SetPixels32(srgb);
                image.Apply();
                return image.EncodeToPNG();
            } finally { Object.DestroyImmediate(image); }
        }
    }
}
