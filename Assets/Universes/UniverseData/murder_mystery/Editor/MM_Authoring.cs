using RyanAssets.GameFeel;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.murder_mystery.Client;

namespace Universes.UniverseData.murder_mystery.Editor {
    /// <summary>
    /// Rebuilds the murder mystery presentation prefabs (HUD and body marker), makes sure the HUD is in the
    /// start scene, and applies the mansion's atmosphere (fog, ambient, warm lights) to that scene.
    /// Run from Ryan/Murder Mystery in a client (non-server) Editor.
    /// </summary>
    public static class MM_Authoring {
        public const string Root = "Assets/Universes/UniverseData/murder_mystery";
        const string HudPath = Root + "/Prefabs/MM_HUD.prefab";
        const string MarkerPath = Root + "/Prefabs/MM_BodyMarker.prefab";
        const string StartScene = Root + "/murder_mystery_start.unity";
        const string QuickEffects = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/";
        const string Sounds = "Assets/Universes/UniverseData/classic_horror/Flooded_Grounds/Content/Sounds/";
        const string OwnSounds = Root + "/Audio/";

        static readonly Color Blood = new(0.9f, 0.08f, 0.06f, 0.85f);

        [MenuItem("Ryan/Murder Mystery/Rebuild HUD and Markers")]
        public static void BuildAll() {
            EnsureFolders();
            GameObject marker = BuildMarker();
            BuildHud(marker);
            AddHudToStartScene();
            AssetDatabase.SaveAssets();
        }

        // ---------- atmosphere ----------

        // A lamp-lit mansion at night rather than grey concrete under white light: warm, dark fog that hides
        // the void outside the walls, a warm ambient, and tungsten-coloured fill lights. The post-processing
        // look (MoodPreset.WarmIndoor) is applied at runtime by MM_ClientHUD.
        static readonly Color FogColor = new(0.085f, 0.066f, 0.052f);
        const float FogDensity = 0.012f;
        static readonly Color AmbientSky = new(0.22f, 0.175f, 0.13f);
        static readonly Color AmbientEquator = new(0.18f, 0.137f, 0.098f);
        static readonly Color AmbientGround = new(0.058f, 0.045f, 0.036f);
        static readonly Color TungstenLight = new(1f, 0.78f, 0.55f);
        const string FillLightPrefix = "Warm Fill";
        const string CeilingLightPrefix = "Ceiling Light";
        // Room lights authored in map-local coordinates. An earlier pass gave their containers a transform
        // that cancelled the Map's offset and scale, which parked every one of them near the world origin,
        // far outside the house; ApplyAtmosphere puts them back in the rooms.
        // Each container's fills get one intensity: the dense grid is many small pools, the additional
        // lights are the few brighter ones at doorways and halls.
        static readonly (string container, float intensity)[] RoomLightContainers = {
            ("Dense Warm Fill Lights", 37.5f),
            ("Additional Warm Lights", 62.5f),
        };
        // Light range is not scaled by the Map's 3x transform, so fills authored for the unscaled map need
        // a world-space range that reaches across a room.
        const float RoomFillRange = 34f;
        const string VisibilityFillName = "Global Visibility Fill";
        // Faint warm moonlight: keeps corners readable without washing out the lamps.
        static readonly Color VisibilityFillColor = new(0.85f, 0.72f, 0.6f);
        const float VisibilityFillIntensity = 0.5f;

        [MenuItem("Ryan/Murder Mystery/Apply Atmosphere")]
        public static void ApplyAtmosphere() {
            EditInStartScene(scene => {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = FogColor;
                RenderSettings.fogDensity = FogDensity;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = AmbientSky;
                RenderSettings.ambientEquatorColor = AmbientEquator;
                RenderSettings.ambientGroundColor = AmbientGround;
                foreach (GameObject rootObject in scene.GetRootGameObjects()) {
                    foreach (Transform container in rootObject.GetComponentsInChildren<Transform>(true))
                        foreach ((string containerName, float intensity) in RoomLightContainers) {
                            if (container.name != containerName)
                                continue;
                            Undo.RecordObject(container, "Move murder mystery room lights");
                            container.localPosition = Vector3.zero;
                            container.localRotation = Quaternion.identity;
                            container.localScale = Vector3.one;
                            foreach (Light fill in container.GetComponentsInChildren<Light>(true)) {
                                Undo.RecordObject(fill, "Warm murder mystery lights");
                                fill.range = RoomFillRange;
                                fill.intensity = intensity;
                                fill.color = TungstenLight;
                            }
                        }
                    foreach (Light light in rootObject.GetComponentsInChildren<Light>(true)) {
                        string name = light.gameObject.name;
                        if (name == VisibilityFillName) {
                            Undo.RecordObject(light, "Dim murder mystery visibility fill");
                            light.color = VisibilityFillColor;
                            light.intensity = VisibilityFillIntensity;
                        } else if (name.StartsWith(FillLightPrefix) || name.StartsWith(CeilingLightPrefix)) {
                            Undo.RecordObject(light, "Warm murder mystery lights");
                            light.color = TungstenLight;
                        }
                        // The few ceiling lights cast soft shadows so rooms read as lit by real fixtures.
                        if (name.StartsWith(CeilingLightPrefix))
                            light.shadows = LightShadows.Soft;
                    }
                }
                return true;
            });
        }

        /// <summary>Runs <paramref name="edit"/> on the start scene (opening it if needed) and saves it when the edit says so.</summary>
        static void EditInStartScene(System.Func<UnityEngine.SceneManagement.Scene, bool> edit) {
            var scene = EditorSceneManager.GetSceneByPath(StartScene);
            bool opened = !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(StartScene, OpenSceneMode.Additive);
            var previousActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            try {
                // RenderSettings belong to the active scene.
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                if (edit(scene)) {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            } finally {
                if (previousActive.IsValid() && previousActive.isLoaded && previousActive != scene)
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousActive);
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
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
            // A texture that failed to import (e.g. an LFS pointer that was never pulled) must not wipe the
            // one the material already has.
            if (source.mainTexture == null)
                throw new System.InvalidOperationException("Effect texture missing (run git lfs pull?): " + source.name);
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

        // Role colours mirror MM_Text.RoleColor, so the HUD, name tags and results agree.
        static Color RoleColor(MM_Role role) => MM_Text.RoleColor(role);
        static readonly Color Alarm = new(0.95f, 0.3f, 0.25f);

        /// <summary>
        /// The HUD is made from the shared HudKit pieces so it matches the top bar and menus: a quiet
        /// role chip in the corner, and cards that pop in only when something happens (role reveal,
        /// body nearby, meeting, ejection, results).
        /// </summary>
        static void BuildHud(GameObject marker) {
            // Above the spectate bar (5) and vote window (20), below the top bar (50).
            RectTransform root = HudKit.Canvas("MM_HUD", 30);
            MM_ClientHUD hud = root.gameObject.AddComponent<MM_ClientHUD>();
            hud.innocentIcon = HudKit.Icon("innocent");
            hud.sheriffIcon = HudKit.Icon("sheriff");
            hud.murdererIcon = HudKit.Icon("assassin");
            hud.spectatorIcon = HudKit.Icon("visibility");
            hud.caughtIcon = HudKit.Icon("handcuffs");
            hud.wrongIcon = HudKit.Icon("tombstone");
            hud.nobodyIcon = HudKit.Icon("ballot");
            hud.diedIcon = HudKit.Icon("skull");
            hud.survivedIcon = HudKit.Icon("check_circle");
            hud.ejectedIcon = HudKit.Icon("gavel");

            // Role reveal, centre: who you are and what to do.
            hud.roleReveal = HudKit.Banner(root, "RoleReveal", new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), 640f,
                "innocent", RoleColor(MM_Role.Innocent), 96f, MM_ClientHUD.RoundStartKicker);

            BuildRoleChip(root, hud);

            // Body prompt, lower centre.
            RectTransform prompt = HudKit.KeyPrompt(root, "ReportPrompt", "E", "Report the body", Alarm, out hud.reportText, out hud.reportKey, 48f);
            prompt.anchorMin = prompt.anchorMax = prompt.pivot = new Vector2(0.5f, 0f);
            prompt.anchoredPosition = new Vector2(0f, 230f);
            HudKit.Icon(prompt, "dead_head", 26f, new Color(1f, 1f, 1f, 0.7f)).transform.SetSiblingIndex(prompt.childCount - 2);
            hud.reportPrompt = HudKit.Popup(prompt, false);

            BuildMeetingCard(root, hud);

            // Ejection outcome, centre.
            hud.ejection = HudKit.Banner(root, "Ejection", new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), 620f,
                "ballot", Alarm, 88f, "The town has voted");

            BuildResults(root, hud);

            hud.bodyMarkerPrefab = marker;
            hud.mood = MoodPreset.WarmIndoor;
            AssignSounds(hud);

            UIKit.SavePrefab(root.gameObject, HudPath);
        }

        /// <summary>The round's sounds (Kenney CC0, see CREDITS.md) played through the shared GameSfx kit.</summary>
        static void AssignSounds(MM_ClientHUD hud) {
            hud.roleRevealSound = Sound(0.8f, OwnSounds + "role_reveal.ogg");
            hud.killSting = Sound(0.75f, OwnSounds + "kill_sting.ogg");
            hud.killSting.minDistance = 8f;
            hud.killSting.maxDistance = 55f;
            hud.bodyFoundSound = Sound(0.6f, Sounds + "Horn.mp3");
            hud.gavelSound = Sound(1f, OwnSounds + "gavel_1.ogg", OwnSounds + "gavel_2.ogg", OwnSounds + "gavel_3.ogg");
            hud.gavelSound.minInterval = 0.05f;
            hud.drumSound = Sound(0.8f, OwnSounds + "drum_1.ogg", OwnSounds + "drum_2.ogg", OwnSounds + "drum_3.ogg", OwnSounds + "drum_4.ogg", OwnSounds + "drum_5.ogg");
            hud.drumSound.minInterval = 0.02f;
            hud.drumSound.pitchJitterSemitones = 0.4f;
            hud.ejectionBell = Sound(0.85f, OwnSounds + "bell_toll.ogg");
        }

        static SfxSound Sound(float volume, params string[] paths) {
            AudioClip[] clips = new AudioClip[paths.Length];
            for (int i = 0; i < paths.Length; i++) {
                clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
                if (clips[i] == null)
                    throw new System.InvalidOperationException("Missing murder mystery sound: " + paths[i]);
            }
            return new SfxSound { clips = clips, volume = volume };
        }

        /// <summary>Bottom-left: your role as a small emblem and name, and the meetings you have left.</summary>
        static void BuildRoleChip(RectTransform root, MM_ClientHUD hud) {
            RectTransform chip = UIKit.Pinned(root, "RoleChip", Vector2.zero, new Vector2(20f, 200f), new Vector2(260f, 64f));
            HudKit.Glass(chip, 16f);
            UIKit.Row(chip.gameObject, 12f, new RectOffset(10, 16, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(chip.gameObject, true, false);
            HudKit.Emblem(chip, "Emblem", "innocent", 46f, RoleColor(MM_Role.Innocent), out hud.roleChipDisc, out hud.roleChipIcon);

            RectTransform text = UIKit.Node(chip, "Text");
            UIKit.Column(text.gameObject, 0f, null, TextAnchor.MiddleLeft);
            UIKit.Text(text, "Caption", "Your role", UIText.Caption, UIColorRole.TextMuted);
            hud.roleChipName = UIKit.Text(text, "Role", "Innocent", UIText.Title);
            hud.roleChipName.fontSize = 20f;

            RectTransform divider = UIKit.Node(chip, "Divider");
            UIKit.Size(divider, 1f, 36f);
            UIKit.Paint(divider.gameObject.AddComponent<Image>(), UIColorRole.Stroke).raycastTarget = false;

            hud.meetingsLeft = HudKit.Chip(chip, "Meetings", "megaphone", "2", Alarm, 30f);
            hud.meetingsLeft.background.enabled = false;
            // Murderers only: the dagger is not ready yet (grace period or kill cooldown).
            hud.killCooldown = HudKit.Chip(chip, "KillCooldown", "dagger", "25s", RoleColor(MM_Role.Murderer), 30f);
            hud.killCooldown.background.enabled = false;
            hud.killCooldown.SetVisible(false);
            hud.roleChip = HudKit.Popup(chip, false);
        }

        /// <summary>Top centre, under the top bar: who found whom, who was seen nearby and the vote countdown.</summary>
        static void BuildMeetingCard(RectTransform root, MM_ClientHUD hud) {
            RectTransform card = HudKit.Card(root, "MeetingCard", new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(640f, 150f));
            HudKit.AccentStripe(card, Alarm);
            UIKit.Row(card.gameObject, 18f, new RectOffset(22, 22, 20, 20), TextAnchor.MiddleLeft);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.Emblem(card, "Emblem", "megaphone", 76f, Alarm, out _, out _);

            RectTransform text = UIKit.Node(card, "Text");
            UIKit.Size(text, -1f, -1f, 1f);
            UIKit.Column(text.gameObject, 4f, null, TextAnchor.MiddleLeft);
            TextMeshProUGUI title = UIKit.Text(text, "Title", "Emergency meeting", UIText.Title);
            title.color = Color.Lerp(Alarm, Color.white, 0.25f);
            Object.DestroyImmediate(title.GetComponent<UIThemeColor>());
            hud.meetingBody = UIKit.Text(text, "Body", string.Empty, UIText.Body);
            hud.meetingBody.richText = true;
            hud.meetingBody.textWrappingMode = TextWrappingModes.Normal;
            hud.meetingSuspects = UIKit.Text(text, "Suspects", string.Empty, UIText.Small, UIColorRole.TextSecondary);
            hud.meetingSuspects.richText = true;
            hud.meetingSuspects.textWrappingMode = TextWrappingModes.Normal;

            RectTransform footer = UIKit.Node(text, "Footer");
            UIKit.Row(footer.gameObject, 8f, new RectOffset(0, 0, 6, 0), TextAnchor.MiddleLeft);
            hud.meetingTimer = HudKit.Chip(footer, "Timer", "timer", "Voting in 15s", Alarm, 30f, UIText.Small);
            hud.meetingTimer.label.font = UIKit.Theme.semiBoldFont;
            HudKit.Chip(footer, "Hint", "chat_bubble", "Discuss in chat", UIKit.Theme.textSecondary, 30f, UIText.Small);
            hud.meetingCard = card.GetComponent<RyanAssets.UI.GameHud.HudPopup>();
        }

        /// <summary>The end-of-round reveal: the winner, how it ended, and everyone's role and fate.</summary>
        static void BuildResults(RectTransform root, MM_ClientHUD hud) {
            RectTransform card = HudKit.Card(root, "Results", new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(880f, 700f));
            hud.resultsStripe = HudKit.AccentStripe(card, RoleColor(MM_Role.Innocent), 5f);
            UIKit.Column(card.gameObject, 8f, new RectOffset(36, 36, 28, 26), TextAnchor.UpperCenter);
            UIKit.FitContent(card.gameObject, false, true);

            HudKit.Emblem(card, "Emblem", "laurels_trophy", 84f, RoleColor(MM_Role.Innocent), out hud.resultsDisc, out _);
            UIKit.Text(card, "Kicker", "Round over · roles revealed", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            hud.resultsTitle = UIKit.Text(card, "Winner", "Innocents win", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            Object.DestroyImmediate(hud.resultsTitle.GetComponent<UIThemeColor>());
            hud.resultsSummary = UIKit.Text(card, "Summary", string.Empty, UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            hud.resultsSummary.textWrappingMode = TextWrappingModes.Normal;

            RectTransform gap = UIKit.Node(card, "Gap");
            UIKit.Size(gap, -1f, 6f);
            hud.resultsRows = HudKit.Table(card, "Table", new[] {
                new HudKit.Column("Name"),
                new HudKit.Column("Role", 150f),
                new HudKit.Column("Fate", 150f),
                new HudKit.Column("Kills", 70f, TextAlignmentOptions.MidlineRight),
            }, 36f);
            hud.resultsMore = UIKit.Text(card, "More", string.Empty, UIText.Small, UIColorRole.TextMuted, TextAlignmentOptions.Center);

            RectTransform footer = UIKit.Node(card, "Footer");
            UIKit.Row(footer.gameObject, 0f, new RectOffset(0, 0, 8, 0), TextAnchor.MiddleCenter);
            hud.resultsTimer = HudKit.Chip(footer, "NextRound", "hourglass_bottom", "Next round in 12s", UIKit.Theme.textSecondary, 32f, UIText.Small);
            hud.results = card.GetComponent<RyanAssets.UI.GameHud.HudPopup>();
        }

        static void AddHudToStartScene() {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            EditInStartScene(scene => {
                foreach (GameObject rootObject in scene.GetRootGameObjects())
                    if (PrefabUtility.GetCorrespondingObjectFromSource(rootObject) == prefab)
                        return false;
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = "MM_HUD";
                return true;
            });
        }
    }
}
