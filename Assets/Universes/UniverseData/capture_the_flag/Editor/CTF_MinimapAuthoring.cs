using System.IO;
using UnityEditor;
using UnityEngine;
#if !UNITY_SERVER
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine.UI;
using Universes.UniverseData.capture_the_flag.Client;
#endif

namespace Universes.UniverseData.capture_the_flag.Editor {
    /// <summary>
    /// The tactical minimap: its HUD panel (built into FlagWarsHUD by <see cref="CTF_HudAuthoring"/>)
    /// and its picture, an orthographic render of the generated arena from straight above, toned down
    /// so the markers read on it. The render needs the arena, so it runs after the start scene is built
    /// and is then set on the HUD prefab. Client Editor only.
    /// </summary>
    internal static class CTF_MinimapAuthoring {
        /// <summary>Map size on the HUD in reference pixels: the arena's 240 x 144 m at 1.25 px a metre.</summary>
        public const float MapWidth = 300f, MapHeight = 180f, Padding = 8f;
        const string TextureName = "MinimapArena";
        const int RenderWidth = 1000, RenderHeight = 600;
        /// <summary>How far the render is pulled toward grey and darkened, so team-coloured markers stand out.</summary>
        const float Desaturate = 0.35f, Darken = 0.78f;
        const float CameraHeight = 120f;
        static string TexturePath => $"{CTF_Authoring.Root}/Textures/{TextureName}.png";

#if !UNITY_SERVER
        /// <summary>
        /// The panel, left column above the level chip (where the challenges panel opens; the map steps
        /// aside while it is open). Returns the component for the controller to reference.
        /// </summary>
        public static CTF_Minimap Build(RectTransform root, CTF_ClientController hud, Vector2 bottomLeft) {
            RectTransform panel = UIKit.Pinned(root, "Minimap", Vector2.zero, bottomLeft, new Vector2(MapWidth + Padding * 2f, MapHeight + Padding * 2f));
            HudKit.Glass(panel, 16f);
            var map = root.gameObject.AddComponent<CTF_Minimap>();
            map.hud = hud;
            map.field = CTF_ArenaAuthoring.FieldRect;

            RectTransform area = UIKit.Node(panel, "Area");
            UIKit.Center(area);
            area.sizeDelta = new Vector2(MapWidth, MapHeight);
            area.gameObject.AddComponent<RectMask2D>();
            map.area = area;

            RectTransform picture = UIKit.Node(area, "Arena");
            UIKit.Center(picture);
            picture.sizeDelta = new Vector2(MapWidth, MapHeight);
            var image = picture.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
            image.color = image.sprite != null ? Color.white : new Color(0.25f, 0.32f, 0.22f, 0.9f);
            image.raycastTarget = false;
            map.background = picture;

            RectTransform ring = UIKit.Node(area, "Sweep");
            UIKit.Center(ring);
            map.sweepRing = ring.gameObject.AddComponent<Image>();
            map.sweepRing.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{CTF_Authoring.Root}/Textures/CrosshairRing.png");
            map.sweepRing.color = CTF_Authoring.PowerUpColor(CTF_PowerUpKind.Radar);
            map.sweepRing.raycastTarget = false;
            map.sweepRing.enabled = false;

            RectTransform label = UIKit.Pinned(panel, "Radar", new Vector2(1f, 1f), new Vector2(-Padding - 6f, -Padding - 4f), new Vector2(120f, 22f));
            map.radarLabel = UIKit.Text(label, "Text", "RADAR 8s", UIText.Caption, UIColorRole.TextPrimary, TextAlignmentOptions.TopRight);
            UIKit.Stretch(map.radarLabel.rectTransform);
            map.radarLabel.fontStyle = FontStyles.Bold;
            Object.DestroyImmediate(map.radarLabel.GetComponent<UIThemeColor>());
            map.radarLabel.color = CTF_Authoring.PowerUpColor(CTF_PowerUpKind.Radar);
            map.radarLabel.outlineWidth = 0.2f;
            map.radarLabel.outlineColor = new Color32(0, 0, 0, 200);
            map.radarLabel.enabled = false;

            map.dotSprite = DotSprite();
            map.arrowSprite = HudKit.Icon("navigation");
            map.flagSprite = HudKit.Icon("flying_flag");
            map.killerSprite = HudKit.Icon("skull");
            int kinds = System.Enum.GetValues(typeof(CTF_PowerUpKind)).Length;
            map.powerUpSprites = new Sprite[kinds];
            map.powerUpColors = new Color[kinds];
            for (int i = 0; i < kinds; i++) {
                map.powerUpSprites[i] = HudKit.Icon(CTF_Authoring.PowerUpIcon((CTF_PowerUpKind)i));
                map.powerUpColors[i] = CTF_Authoring.PowerUpColor((CTF_PowerUpKind)i);
            }
            map.panel = HudKit.Popup(panel, false);
            return map;
        }

        /// <summary>A soft white disc for player dots.</summary>
        static Sprite DotSprite() {
            const string name = "MinimapDot";
            CTF_Authoring.Kit.SaveTexture(name, (u, v) => {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
                return new Color(1f, 1f, 1f, Mathf.Clamp01((0.46f - d) / 0.04f + 0.5f));
            }, 64);
            return ImportSprite($"{CTF_Authoring.Root}/Textures/{name}.png");
        }
#endif

        static Sprite ImportSprite(string path) {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Renders the open start scene's arena from straight above into Textures/MinimapArena.png and
        /// sets it on the HUD prefab's minimap. Needs a graphics device (skipped in -nographics batch runs,
        /// which keep the last render).
        /// </summary>
        public static void RenderInto(CTF_Map map, string hudPrefabPath) {
#if !UNITY_SERVER
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) {
                Debug.LogWarning("[FlagWars] No graphics device: the minimap keeps its last render.");
                return;
            }
            Rect field = CTF_ArenaAuthoring.FieldRect;
            bool fog = RenderSettings.fog;
            // The title and base signs float above the field: keep them out of the picture.
            var labels = map.lobbyLabels;
            var wasActive = new bool[labels.Length];
            for (int i = 0; i < labels.Length; i++) {
                if (labels[i] == null) continue;
                wasActive[i] = labels[i].activeSelf;
                labels[i].SetActive(false);
            }
            RenderSettings.fog = false;
            var cam = new GameObject("MinimapCamera", typeof(Camera)).GetComponent<Camera>();
            try {
                cam.orthographic = true;
                cam.orthographicSize = field.height * 0.5f;
                cam.aspect = field.width / field.height;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = CameraHeight + 20f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.2f, 0.26f, 0.18f);
                // Straight down with +z at the top of the picture and +x to the right (CTF_MapMath.ToMap).
                cam.transform.SetPositionAndRotation(new Vector3(field.center.x, CameraHeight, field.center.y), Quaternion.Euler(90f, 0f, 0f));
                File.WriteAllBytes(TexturePath, Render(cam));
            } finally {
                Object.DestroyImmediate(cam.gameObject);
                RenderSettings.fog = fog;
                for (int i = 0; i < labels.Length; i++) if (labels[i] != null) labels[i].SetActive(wasActive[i]);
            }
            AssetDatabase.ImportAsset(TexturePath);
            Sprite sprite = ImportSprite(TexturePath);

            GameObject prefab = PrefabUtility.LoadPrefabContents(hudPrefabPath);
            try {
                var minimap = prefab.GetComponent<CTF_Minimap>();
                var image = minimap != null && minimap.background != null ? minimap.background.GetComponent<Image>() : null;
                if (image == null) throw new System.InvalidOperationException($"[FlagWars] {hudPrefabPath} has no minimap picture.");
                image.sprite = sprite;
                image.color = Color.white;
                PrefabUtility.SaveAsPrefabAsset(prefab, hudPrefabPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
            Debug.Log("[FlagWars] Minimap rendered.");
#endif
        }

        static byte[] Render(Camera cam) {
            var rt = new RenderTexture(RenderWidth, RenderHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(RenderWidth, RenderHeight, TextureFormat.RGB24, false);
            try {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, RenderWidth, RenderHeight), 0, 0);
                var pixels = tex.GetPixels();
                for (int i = 0; i < pixels.Length; i++) {
                    Color c = pixels[i];
                    float grey = c.grayscale;
                    pixels[i] = Color.Lerp(c, new Color(grey, grey, grey), Desaturate) * Darken;
                }
                tex.SetPixels(pixels);
                tex.Apply();
                return tex.EncodeToPNG();
            } finally {
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }
    }
}
