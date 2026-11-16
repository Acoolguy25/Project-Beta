using System.Collections.Generic;
using System.IO;
using System.Linq;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace RyanAssets.GameFeel.Editor {
    /// <summary>
    /// Generates every asset of the game-feel kit: textures, particle prefabs and their library, the
    /// floating text and screen overlay prefabs, the mood volume profiles, the sound cue library and
    /// <c>Resources/GameFeelSettings</c>. Menu Ryan &gt; Game Feel &gt; Rebuild, or batchmode
    /// <c>-executeMethod RyanAssets.GameFeel.Editor.GameFeelAuthoring.Build</c>.
    /// Re-running is safe: prefabs are regenerated, while hand-tuned settings numbers and cue
    /// entries that already have clips are kept.
    /// </summary>
    public static class GameFeelAuthoring {
        public const string Root = "Packages/com.ryan.gameframework/Runtime/GameFeel";
        public const string Generated = Root + "/Generated";
        const string AudioFolder = Root + "/Audio";
        const string UiSoundFolder = "Packages/com.ryan.ui/Runtime/UI/Theme/Sounds";
        const string SettingsPath = Root + "/Resources/" + GameFeelSettings.ResourcePath + ".asset";
        const string CuesPath = Generated + "/GameFeelCues.asset";
        const string FxLibraryPath = Generated + "/GameFxLibrary.asset";
        const string MoodFolder = Generated + "/Moods";
        const string MixerPath = "Packages/com.ryan.ui/Runtime/Client/ClientAudio/MainMixer.mixer";
        const string SfxGroupName = "SFXVolume";

        // Under the shared HUD panels (10) and top bar (50), so effects never hide the UI; the
        // countdown text has its own canvas above them.
        const int OverlaySortingOrder = -10; // below every HUD canvas (the lowest is 0)
        const int CenterTextSortingOrder = 60;
        const float CenterFontSize = 220f;
        const float SpeedLinesSize = 2600f; // covers a 1920x1080 reference screen at any rotation
        const float FloatingFontSize = 10f;  // TextMeshPro: 10 points is about one metre tall at scale 1

        [MenuItem("Ryan/Game Feel/Rebuild")]
        public static void Build() {
            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            UIThemeAssets.Ensure();
            GameFeelTextures.BuildAll();
            Dictionary<FxKind, ParticleSystem> effects = GameFeelParticles.BuildAll();
            GameFxLibrary fxLibrary = BuildFxLibrary(effects);
            SfxCueLibrary cues = BuildCues();
            FloatingTextLabel floatingText = BuildFloatingText();
            ScreenFxOverlay overlay = BuildOverlay();

            GameFeelSettings settings = LoadOrCreate<GameFeelSettings>(SettingsPath);
            settings.cues = cues;
            settings.effects = fxLibrary;
            settings.floatingText.prefab = floatingText;
            settings.screenOverlay = overlay;
            settings.audio.mixerGroup = SfxGroup();
            BuildMoods(settings.moods);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("Game feel kit rebuilt.");
        }

        // --- Libraries --------------------------------------------------------------

        static GameFxLibrary BuildFxLibrary(Dictionary<FxKind, ParticleSystem> effects) {
            GameFxLibrary library = LoadOrCreate<GameFxLibrary>(FxLibraryPath);
            var existing = library.Entries.Where(e => e != null).ToDictionary(e => e.kind);
            var entries = new List<GameFxLibrary.Entry>();
            foreach (FxKind kind in System.Enum.GetValues(typeof(FxKind))) {
                if (!existing.TryGetValue(kind, out GameFxLibrary.Entry entry))
                    entry = new GameFxLibrary.Entry { kind = kind, poolSize = DefaultPool(kind) };
                entry.prefab = effects.TryGetValue(kind, out ParticleSystem prefab) ? prefab : null;
                entries.Add(entry);
            }
            library.Entries = entries.ToArray();
            EditorUtility.SetDirty(library);
            return library;
        }

        static int DefaultPool(FxKind kind) => kind switch {
            FxKind.DustPuff => 24,
            FxKind.Sparks => 12,
            FxKind.CoinBurst => 10,
            FxKind.ExplosionLarge => 4,
            FxKind.Confetti => 3,
            FxKind.BeaconBeam => 4,
            FxKind.Trail => 6,
            _ => 6,
        };

        /// <summary>A cue's default clips (file names in Audio/, or UI sounds) and mix.</summary>
        readonly struct CueDefault {
            public readonly string[] files;
            public readonly float volume, volumeJitter, pitchJitter, minInterval;

            public CueDefault(float volume, float pitchJitter, params string[] files) {
                this.files = files;
                this.volume = volume;
                this.pitchJitter = pitchJitter;
                volumeJitter = 0.1f;
                minInterval = 0.04f;
            }
        }

        static readonly Dictionary<SfxCue, CueDefault> CueDefaults = new() {
            [SfxCue.UiClick] = new(0.6f, 0.4f, "ui:ui_click"),
            [SfxCue.UiHover] = new(0.3f, 0.6f, "ui:ui_hover"),
            [SfxCue.Coin] = new(0.7f, 0.3f, "coin"),
            [SfxCue.Pickup] = new(0.7f, 0.5f, "pickup"),
            [SfxCue.Purchase] = new(0.85f, 0.5f, "purchase_1", "purchase_2"),
            [SfxCue.Build] = new(0.85f, 1f, "build_1", "build_2", "build_3", "build_4", "build_5"),
            [SfxCue.Hit] = new(0.8f, 1f, "hit_1", "hit_2", "hit_3", "hit_4", "hit_5"),
            [SfxCue.Kill] = new(1f, 0.6f, "kill_1", "kill_2", "kill_3"),
            [SfxCue.Explosion] = new(1f, 1.2f, "explosion_1", "explosion_2", "explosion_3", "explosion_4", "explosion_5"),
            [SfxCue.ExplosionLarge] = new(1f, 0.8f, "explosion_large_1", "explosion_large_2"),
            [SfxCue.Whoosh] = new(0.7f, 1.5f, "whoosh_1", "whoosh_2"),
            [SfxCue.CountdownBeep] = new(0.7f, 0f, "countdown_beep"),
            [SfxCue.Go] = new(0.9f, 0f, "go"),
            [SfxCue.Win] = new(0.8f, 0f, "win"),
            [SfxCue.Lose] = new(0.8f, 0f, "lose"),
            [SfxCue.LevelUp] = new(0.7f, 0f, "level_up"),
            [SfxCue.Error] = new(0.6f, 0.2f, "error"),
            [SfxCue.Alarm] = new(0.8f, 0f, "alarm"),
            [SfxCue.Footstep] = new(0.5f, 1f, "footstep_1", "footstep_2", "footstep_3", "footstep_4", "footstep_5"),
            [SfxCue.Jump] = new(0.3f, 1f, "jump"),
            [SfxCue.Land] = new(0.6f, 1f, "land_1", "land_2", "land_3"),
            [SfxCue.Horn] = new(0.8f, 0f, "horn"),
        };

        static SfxCueLibrary BuildCues() {
            SfxCueLibrary library = LoadOrCreate<SfxCueLibrary>(CuesPath);
            var existing = library.Entries.Where(e => e != null).GroupBy(e => e.cue).ToDictionary(g => g.Key, g => g.First());
            var entries = new List<SfxCueLibrary.Entry>();
            foreach (SfxCue cue in System.Enum.GetValues(typeof(SfxCue))) {
                if (existing.TryGetValue(cue, out SfxCueLibrary.Entry entry) && entry.sound != null && entry.sound.HasClips && entry.sound.clips.All(c => c != null)) {
                    entries.Add(entry);
                    continue;
                }
                entry = new SfxCueLibrary.Entry { cue = cue, sound = new SfxSound() };
                if (CueDefaults.TryGetValue(cue, out CueDefault defaults)) {
                    entry.sound.clips = defaults.files.Select(LoadClip).Where(c => c != null).ToArray();
                    entry.sound.volume = defaults.volume;
                    entry.sound.volumeJitter = defaults.volumeJitter;
                    entry.sound.pitchJitterSemitones = defaults.pitchJitter;
                    entry.sound.minInterval = defaults.minInterval;
                }
                if (!entry.sound.HasClips)
                    Debug.LogWarning($"GameFeel: no clips found for cue {cue}.");
                entries.Add(entry);
            }
            library.Entries = entries.ToArray();
            EditorUtility.SetDirty(library);
            return library;
        }

        static AudioClip LoadClip(string file) {
            string path = file.StartsWith("ui:") ? $"{UiSoundFolder}/{file.Substring(3)}.ogg" : $"{AudioFolder}/{file}.ogg";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
                Debug.LogWarning("GameFeel: missing clip " + path);
            return clip;
        }

        static AudioMixerGroup SfxGroup() {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            AudioMixerGroup group = mixer != null ? mixer.FindMatchingGroups(SfxGroupName).FirstOrDefault() : null;
            if (group == null)
                Debug.LogWarning($"GameFeel: mixer group {SfxGroupName} not found in {MixerPath}; kit sounds bypass the SFX volume.");
            return group;
        }

        // --- Floating text ------------------------------------------------------------

        static FloatingTextLabel BuildFloatingText() {
            UITheme theme = UIThemeAssets.Theme;
            var go = new GameObject("FloatingText");
            var text = go.AddComponent<TextMeshPro>();
            text.font = theme.boldFont;
            text.fontSharedMaterial = FloatingTextMaterial(theme.boldFont);
            text.fontSize = FloatingFontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = new Vector2(20f, 4f);
            text.text = "+$120";
            text.richText = true;
            text.raycastTarget = false;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var label = go.AddComponent<FloatingTextLabel>();
            SerializedFields.Set(label, "text", text);
            return SavePrefab(go, Generated + "/Prefabs/FloatingText.prefab").GetComponent<FloatingTextLabel>();
        }

        /// <summary>The bold theme font drawn over the world (no depth test) with an outline and soft shadow.</summary>
        static Material FloatingTextMaterial(TMP_FontAsset font) {
            string path = Generated + "/Materials/FloatingTextOverlay.mat";
            Shader overlay = Shader.Find("TextMeshPro/Distance Field Overlay")
                ?? throw new System.InvalidOperationException("TextMeshPro/Distance Field Overlay shader not found (TMP Essentials).");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                material = new Material(font.material) { name = "FloatingTextOverlay" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = overlay;
            material.SetTexture("_MainTex", font.atlasTexture);
            material.SetFloat("_FaceDilate", 0.15f);
            material.SetFloat("_OutlineWidth", 0.22f);
            material.SetColor("_OutlineColor", new Color(0.05f, 0.05f, 0.08f, 1f));
            material.EnableKeyword("UNDERLAY_ON");
            material.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.6f));
            material.SetFloat("_UnderlayOffsetX", 0.3f);
            material.SetFloat("_UnderlayOffsetY", -0.4f);
            material.SetFloat("_UnderlaySoftness", 0.4f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // --- Screen overlay -----------------------------------------------------------

        static ScreenFxOverlay BuildOverlay() {
            UITheme theme = UIThemeAssets.Theme;
            RectTransform root = UIKit.Node(null, "ScreenFxOverlay");
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySortingOrder;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            // No GraphicRaycaster: the overlay never takes clicks.

            RectTransform lines = UIKit.Pinned(root, "SpeedLines", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SpeedLinesSize, SpeedLinesSize));
            Image linesImage = FullImage(lines, GameFeelTextures.SpeedLines);
            Image lowHealth = FullImage(UIKit.Fill(root, "LowHealthVignette"), GameFeelTextures.Vignette);
            Image danger = FullImage(UIKit.Fill(root, "DangerVignette"), GameFeelTextures.DangerVignette);
            Image damage = FullImage(UIKit.Fill(root, "DamageVignette"), GameFeelTextures.Vignette);
            Image flash = FullImage(UIKit.Fill(root, "Flash"), null);

            RectTransform centerRoot = UIKit.Fill(root, "CenterCanvas");
            var centerCanvas = centerRoot.gameObject.AddComponent<Canvas>();
            centerCanvas.overrideSorting = true;
            centerCanvas.sortingOrder = CenterTextSortingOrder;
            RectTransform centerRect = UIKit.Fill(centerRoot, "CenterText");
            // 60% of the screen width at most; the overlay auto-sizes longer text down.
            centerRect.anchorMin = new Vector2(0.2f, 0f);
            centerRect.anchorMax = new Vector2(0.8f, 1f);
            var center = centerRect.gameObject.AddComponent<TextMeshProUGUI>();
            center.font = theme.boldFont;
            center.fontSharedMaterial = UIThemeAssets.ShadowMaterial(theme.boldFont);
            center.fontSize = CenterFontSize;
            center.enableAutoSizing = true;
            center.fontSizeMin = 48f;
            center.fontSizeMax = CenterFontSize;
            center.alignment = TextAlignmentOptions.Center;
            center.textWrappingMode = TextWrappingModes.NoWrap;
            center.overflowMode = TextOverflowModes.Overflow;
            center.raycastTarget = false;
            center.text = "3";
            centerRect.gameObject.SetActive(false);

            var overlay = root.gameObject.AddComponent<ScreenFxOverlay>();
            SerializedFields.Set(overlay, "flash", flash);
            SerializedFields.Set(overlay, "damageVignette", damage);
            SerializedFields.Set(overlay, "lowHealthVignette", lowHealth);
            SerializedFields.Set(overlay, "dangerVignette", danger);
            SerializedFields.Set(overlay, "speedLines", lines);
            SerializedFields.Set(overlay, "speedLinesGraphic", linesImage);
            SerializedFields.Set(overlay, "centerText", center);
            return SavePrefab(root.gameObject, Generated + "/Prefabs/ScreenFxOverlay.prefab").GetComponent<ScreenFxOverlay>();
        }

        static Image FullImage(RectTransform rect, Sprite sprite) {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = false;
            return image;
        }

        // --- Moods ----------------------------------------------------------------------

        static void BuildMoods(GameFeelSettings.MoodTuning moods) {
            Directory.CreateDirectory(MoodFolder);
            moods.arcadeBright = Mood("ArcadeBright", p => {
                // No tonemapping: scenes are authored without it, and a curve here greys out bright worlds.
                Set<Tonemapping>(p, t => t.mode.Override(TonemappingMode.None));
                Set<ColorAdjustments>(p, c => {
                    c.postExposure.Override(0f);
                    c.contrast.Override(14f);
                    c.saturation.Override(30f);
                });
                // A high threshold: bright worlds (obby's white floors) would bloom all over.
                Set<Bloom>(p, b => {
                    b.threshold.Override(1.3f);
                    b.intensity.Override(0.35f);
                    b.scatter.Override(0.65f);
                });
                Set<Vignette>(p, v => {
                    v.intensity.Override(0.15f);
                    v.smoothness.Override(0.5f);
                });
            });
            moods.warmIndoor = Mood("WarmIndoor", p => {
                Set<Tonemapping>(p, t => t.mode.Override(TonemappingMode.ACES));
                Set<WhiteBalance>(p, w => {
                    w.temperature.Override(22f);
                    w.tint.Override(4f);
                });
                Set<ColorAdjustments>(p, c => {
                    c.postExposure.Override(0.35f);
                    c.contrast.Override(8f);
                    c.saturation.Override(5f);
                });
                Set<Bloom>(p, b => {
                    b.threshold.Override(0.95f);
                    b.intensity.Override(0.45f);
                    b.tint.Override(new Color(1f, 0.85f, 0.7f));
                });
                Set<Vignette>(p, v => {
                    v.intensity.Override(0.28f);
                    v.smoothness.Override(0.45f);
                });
            });
            moods.night = Mood("Night", p => {
                Set<Tonemapping>(p, t => t.mode.Override(TonemappingMode.ACES));
                Set<WhiteBalance>(p, w => w.temperature.Override(-28f));
                Set<ColorAdjustments>(p, c => {
                    c.postExposure.Override(-0.6f);
                    c.contrast.Override(18f);
                    c.saturation.Override(-18f);
                    c.colorFilter.Override(new Color(0.78f, 0.86f, 1f));
                });
                Set<Bloom>(p, b => {
                    b.threshold.Override(0.8f);
                    b.intensity.Override(1.1f);
                    b.scatter.Override(0.7f);
                });
                Set<Vignette>(p, v => {
                    v.intensity.Override(0.35f);
                    v.smoothness.Override(0.5f);
                });
            });
            moods.horror = Mood("Horror", p => {
                Set<Tonemapping>(p, t => t.mode.Override(TonemappingMode.ACES));
                Set<ColorAdjustments>(p, c => {
                    c.postExposure.Override(-0.45f);
                    c.contrast.Override(28f);
                    c.saturation.Override(-55f);
                    c.colorFilter.Override(new Color(0.88f, 0.95f, 0.9f));
                });
                Set<Vignette>(p, v => {
                    v.intensity.Override(0.5f);
                    v.smoothness.Override(0.55f);
                    v.color.Override(new Color(0.03f, 0f, 0f));
                });
                Set<FilmGrain>(p, g => {
                    g.type.Override(FilmGrainLookup.Medium3);
                    g.intensity.Override(0.45f);
                });
                Set<Bloom>(p, b => {
                    b.threshold.Override(1.1f);
                    b.intensity.Override(0.3f);
                });
            });
        }

        static VolumeProfile Mood(string name, System.Action<VolumeProfile> fill) {
            string path = $"{MoodFolder}/Mood{name}.asset";
            VolumeProfile profile = LoadOrCreate<VolumeProfile>(path);
            fill(profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>Gets or adds override <typeparamref name="T"/> on the profile (stored as a sub-asset) and edits it.</summary>
        static void Set<T>(VolumeProfile profile, System.Action<T> edit) where T : VolumeComponent {
            if (!profile.TryGet(out T component)) {
                component = profile.Add<T>();
                component.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            edit(component);
            EditorUtility.SetDirty(component);
        }

        // --- Helpers -----------------------------------------------------------------------

        static T LoadOrCreate<T>(string path) where T : ScriptableObject {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static GameObject SavePrefab(GameObject go, string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
