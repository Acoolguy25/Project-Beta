using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Editor {
    /// <summary>
    /// Renders the full-screen jumpscare picture (Data/EncounterFace.png): the priest's face pushed
    /// right up to the camera, lit hard from below and one side so half of it falls into shadow,
    /// cropped under the chin, over a dark red glow that falls off to black at the edges. The shared
    /// PresencePortrait is an evenly lit bust meant for menus; this one is only for the scare.
    /// </summary>
    public static class CH_EncounterFace {
        public const string Path = CH_Authoring.Root + "/Data/EncounterFace.png";
        const string FacePrefab = "Packages/com.ryan.gameframework/Runtime/Characters/Monster/Prefabs/PresenceFace.prefab";
        const int Width = 1920, Height = 1080;
        // Vertical field of view, and how much of the frame height the face (brow to chin) fills.
        const float FieldOfView = 30f, FaceFill = 0.95f;
        // Where the frame is centred, as a fraction of the head's height from its lowest point
        // (the neck): high enough that the cut neck sits below the bottom edge.
        const float FrameCentre = 0.68f;
        // One hard key light from low on the left, a faint red rim from behind on the right.
        static readonly Vector3 KeyAngles = new(-38f, 140f, 0f), RimAngles = new(10f, -30f, 0f);
        static readonly Color KeyColour = new(1f, 0.82f, 0.66f), RimColour = new(0.8f, 0.08f, 0.05f);
        const float KeyIntensity = 2.6f, RimIntensity = 1.4f;
        // Background: a dark red glow behind the face falling to black at the edges.
        static readonly Color GlowColour = new(0.24f, 0.015f, 0.02f), EdgeColour = Color.black;
        const float GlowRadius = 0.75f;
        // Hand-shaped light: the right of the frame falls into shadow between these widths, the
        // top (brow and hat) by BrowShadow of the way, down to ShadowFloor of the rendered colour.
        const float SideShadowStart = 0.46f, SideShadowEnd = 0.6f, BrowShadow = 0.8f, ShadowFloor = 0.07f;

        public static Texture2D Build() {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FacePrefab);
            if (prefab == null) throw new System.InvalidOperationException("PresenceFace.prefab is missing: " + FacePrefab);
            var root = Object.Instantiate(prefab);
            var bakedMeshes = new System.Collections.Generic.List<Mesh>();
            var preview = new PreviewRenderUtility();
            try {
                Bounds head = default, all = default;
                bool foundHead = false, foundAny = false;
                // Static previews do not deform skinned meshes, so bake each into a plain mesh.
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                    if (!skin.enabled) continue;
                    var mesh = new Mesh();
                    skin.BakeMesh(mesh);
                    bakedMeshes.Add(mesh);
                    var materials = skin.sharedMaterials;
                    var target = skin.gameObject;
                    Object.DestroyImmediate(skin);
                    target.AddComponent<MeshFilter>().sharedMesh = mesh;
                    target.AddComponent<MeshRenderer>().sharedMaterials = materials;
                }
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) {
                    if (!renderer.enabled) continue;
                    if (!foundAny) { all = renderer.bounds; foundAny = true; } else all.Encapsulate(renderer.bounds);
                    if (renderer.name != "Head") continue;
                    if (!foundHead) { head = renderer.bounds; foundHead = true; } else head.Encapsulate(renderer.bounds);
                }
                if (!foundHead) throw new System.InvalidOperationException("PresenceFace has no Head renderer.");

                preview.AddSingleGO(root);
                Camera camera = preview.camera;
                float faceHeight = head.size.y * (1f - FrameCentre) * 2f;
                float distance = faceHeight / FaceFill * 0.5f / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
                Vector3 centre = new(head.center.x, head.min.y + head.size.y * FrameCentre, head.center.z);
                camera.transform.position = centre + Vector3.forward * (distance + head.extents.z);
                camera.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                camera.fieldOfView = FieldOfView;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = distance * 4f + all.size.magnitude;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                preview.ambientColor = Color.black;
                preview.lights[0].intensity = KeyIntensity;
                preview.lights[0].color = KeyColour;
                preview.lights[0].transform.rotation = Quaternion.Euler(KeyAngles);
                preview.lights[1].intensity = RimIntensity;
                preview.lights[1].color = RimColour;
                preview.lights[1].transform.rotation = Quaternion.Euler(RimAngles);
                // Rendered over black and over white: the difference gives the coverage (alpha), since
                // the pipeline writes an opaque background either way.
                Texture2D onBlack = Render(preview, Color.black), onWhite = Render(preview, Color.white);
                Texture2D picture = Composite(onBlack, onWhite);
                Object.DestroyImmediate(onBlack);
                Object.DestroyImmediate(onWhite);
                System.IO.File.WriteAllBytes(Path, picture.EncodeToPNG());
                Object.DestroyImmediate(picture);
            } finally {
                preview.Cleanup();
                Object.DestroyImmediate(root);
                foreach (var mesh in bakedMeshes) Object.DestroyImmediate(mesh);
            }
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(Path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Path);
        }

        static Texture2D Render(PreviewRenderUtility preview, Color background) {
            preview.camera.backgroundColor = background;
            preview.BeginStaticPreview(new Rect(0, 0, Width, Height));
            preview.Render(true);
            return preview.EndStaticPreview();
        }

        /// <summary>
        /// The face over the red glow, with its light shaped by hand: the preview renderer always adds
        /// some flat ambient, so the far side of the face and the brow are pushed into shadow here
        /// (light from low on the left), and the shadows lean red. The result is opaque.
        /// </summary>
        static Texture2D Composite(Texture2D onBlack, Texture2D onWhite) {
            int w = onBlack.width, h = onBlack.height;
            Color[] dark = onBlack.GetPixels(), light = onWhite.GetPixels();
            var pixels = new Color[dark.Length];
            float aspect = (float)w / h;
            for (int y = 0; y < h; y++) {
                float fy = y / (float)h;
                for (int x = 0; x < w; x++) {
                    float fx = x / (float)w;
                    int i = y * w + x;
                    float u = (fx - 0.5f) * 2f * aspect, v = (fy - 0.5f) * 2f;
                    float glow = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Sqrt(u * u + v * v) / (GlowRadius * aspect));
                    Color background = Color.Lerp(EdgeColour, GlowColour, glow);
                    float alpha = Mathf.Clamp01(1f - (light[i].r - dark[i].r + light[i].g - dark[i].g + light[i].b - dark[i].b) / 3f);
                    Color face = alpha > 0.001f ? dark[i] / alpha : Color.black;
                    float side = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SideShadowStart, SideShadowEnd, fx));
                    float below = Mathf.SmoothStep(0f, 1f, fy);
                    float lit = Mathf.Lerp(1.15f, ShadowFloor, Mathf.Max(side, below * BrowShadow));
                    Color shaded = face * lit;
                    // Shadows lean towards the glow's red rather than going grey.
                    shaded = Color.Lerp(shaded, new Color(shaded.r * 1.1f + 0.02f, shaded.g * 0.55f, shaded.b * 0.5f), 1f - lit);
                    pixels[i] = new Color(Mathf.Lerp(background.r, shaded.r, alpha), Mathf.Lerp(background.g, shaded.g, alpha), Mathf.Lerp(background.b, shaded.b, alpha), 1f);
                }
            }
            var result = new Texture2D(w, h, TextureFormat.RGB24, false);
            result.SetPixels(pixels);
            result.Apply();
            return result;
        }
    }
}
