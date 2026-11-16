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

        // A lamp-lit house at night rather than grey concrete under white light. Two hues: cool moonlight
        // (the directional "Global Visibility Fill", the ambient and the fog) for the shadows, and warm
        // tungsten only from the standing lamps, one per room, so each room has a visible light source
        // with a pool of light around it and darker gaps between. The room fills stay a dim, nearly white
        // bounce with a short range, so they lift the corners without washing the pools out.
        // The post-processing look (MoodPreset.WarmIndoor; Night was tried and crushed the rooms to black) is
        // applied at runtime by MM_ClientHUD; judge all of this in Play mode. Values were tuned by eye in Play
        // mode from the hall and entrance camera spots in the README.
        //
        // Rendering limits (StarterAssetsURPAsset, the pipeline every game uses): Forward, 8 additional lights
        // per object and no additional-light shadows. So point lights cast no shadows (they are set to None
        // rather than claiming soft shadows that never render), each room keeps only a few lights, and the
        // only shadows come from the directional moonlight.
        static readonly Color FogColor = new(0.045f, 0.052f, 0.07f);
        const float FogDensity = 0.011f;
        static readonly Color AmbientSky = new(0.21f, 0.24f, 0.32f);
        static readonly Color AmbientEquator = new(0.176f, 0.184f, 0.208f);
        static readonly Color AmbientGround = new(0.096f, 0.096f, 0.112f);
        static readonly Color TungstenLight = new(1f, 0.74f, 0.45f);
        // The fills are a bounce, not a source: almost white, so characters keep their own colours.
        static readonly Color FillLight = new(1f, 0.93f, 0.84f);
        const string FillLightPrefix = "Warm Fill";
        const string CeilingLightPrefix = "Ceiling Light";
        // Room lights authored in map-local coordinates. An earlier pass gave their containers a transform
        // that cancelled the Map's offset and scale, which parked every one of them near the world origin,
        // far outside the house; ApplyAtmosphere puts them back in the rooms.
        static readonly string[] RoomLightContainers = { "Dense Warm Fill Lights", "Additional Warm Lights" };
        // About one room across (the map is 3x scale, so a 4 m plan room is 12 m): the pools stay separate.
        const float RoomFillRange = 13f;
        const float RoomFillIntensity = 24f;
        // The ceiling lights were 80 / 50 m, one per room, flooding it; now a soft top light over each lamp.
        const float CeilingLightRange = 24f;
        const float CeilingLightIntensity = 18f;
        const string VisibilityFillName = "Global Visibility Fill";
        // Cool moonlight. It is the one light that can cast shadows here (the main light).
        static readonly Color VisibilityFillColor = new(0.62f, 0.72f, 0.95f);
        const float VisibilityFillIntensity = 0.8f;

        // Standing lamps: one beside the nearest wall under each ceiling light.
        const string LampModelPath = "Assets/ThirdParty/KayKit/FurnitureBits/Models/lamp_standing.fbx";
        const string LampPrefabPath = Root + "/Prefabs/MM_StandingLamp.prefab";
        const string LampGlowMaterialPath = Root + "/Materials/MM_LampGlow.mat";
        const string LampsRoot = "MM_Lamps";
        // The model is 2.5 m tall; the robots are 5.6 m, so a lamp at 2.2x stands at about head height.
        const float LampScale = 2.2f;
        // The bulb sits inside the shade, near the top of the 2.5 m model (model units).
        const float LampBulbHeight = 2.15f;
        const float LampLightRange = 22f;
        const float LampLightIntensity = 90f;
        // The shade glows: emission is the model's own palette texture tinted tungsten, so the light shade
        // glows brightly and the dark pole barely at all.
        const float LampGlowStrength = 3.5f;
        // Lamps stand this far from the wall they are put against, and look for one within this distance.
        const float LampWallGap = 2.5f;
        const float LampWallSearch = 60f;
        const float LampFloorSearch = 80f;

        [MenuItem("Ryan/Murder Mystery/Apply Atmosphere")]
        public static void ApplyAtmosphere() {
            EnsureFolders();
            GameObject lampPrefab = BuildLampPrefab();
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
                    foreach (Transform container in rootObject.GetComponentsInChildren<Transform>(true)) {
                        if (System.Array.IndexOf(RoomLightContainers, container.name) < 0)
                            continue;
                        Undo.RecordObject(container, "Move murder mystery room lights");
                        container.localPosition = Vector3.zero;
                        container.localRotation = Quaternion.identity;
                        container.localScale = Vector3.one;
                        foreach (Light fill in container.GetComponentsInChildren<Light>(true)) {
                            Undo.RecordObject(fill, "Murder mystery room fills");
                            fill.range = RoomFillRange;
                            fill.intensity = RoomFillIntensity;
                            fill.color = FillLight;
                            fill.shadows = LightShadows.None;
                        }
                    }
                    foreach (Light light in rootObject.GetComponentsInChildren<Light>(true)) {
                        string name = light.gameObject.name;
                        if (name == VisibilityFillName) {
                            Undo.RecordObject(light, "Murder mystery moonlight");
                            light.color = VisibilityFillColor;
                            light.intensity = VisibilityFillIntensity;
                            // The main light is the one that can cast shadows: characters and lamps get one.
                            light.shadows = LightShadows.Soft;
                        } else if (name.StartsWith(CeilingLightPrefix)) {
                            Undo.RecordObject(light, "Murder mystery ceiling lights");
                            light.color = FillLight;
                            light.range = CeilingLightRange;
                            light.intensity = CeilingLightIntensity;
                            // Additional lights cannot cast shadows in this pipeline (see above).
                            light.shadows = LightShadows.None;
                        }
                    }
                }
                PlaceLamps(scene, lampPrefab);
                return true;
            });
        }

        /// <summary>The standing lamp: KayKit model with a glowing shade and its tungsten bulb.</summary>
        static GameObject BuildLampPrefab() {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(LampModelPath);
            if (model == null)
                throw new System.InvalidOperationException("Missing lamp model: " + LampModelPath);
            Material source = model.GetComponentInChildren<MeshRenderer>().sharedMaterial;

            Material glow = AssetDatabase.LoadAssetAtPath<Material>(LampGlowMaterialPath);
            if (glow == null) {
                glow = new Material(source);
                AssetDatabase.CreateAsset(glow, LampGlowMaterialPath);
            } else {
                glow.CopyPropertiesFromMaterial(source);
            }
            // Colour and map first, keyword last: URP turns _EMISSION off for a material whose emission is
            // black, and CopyPropertiesFromMaterial brings the source's (non-emissive) keywords along.
            glow.SetTexture("_EmissionMap", source.mainTexture);
            glow.SetColor("_EmissionColor", TungstenLight * LampGlowStrength);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            glow.SetKeyword(new UnityEngine.Rendering.LocalKeyword(glow.shader, "_EMISSION"), true);
            EditorUtility.SetDirty(glow);
            AssetDatabase.SaveAssetIfDirty(glow);

            GameObject root = new("MM_StandingLamp");
            try {
                GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                body.name = "Model";
                body.transform.localScale = Vector3.one * LampScale;
                foreach (MeshRenderer renderer in body.GetComponentsInChildren<MeshRenderer>())
                    renderer.sharedMaterial = glow;

                GameObject bulb = new("Bulb");
                bulb.transform.SetParent(root.transform, false);
                bulb.transform.localPosition = Vector3.up * (LampBulbHeight * LampScale);
                Light light = bulb.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = TungstenLight;
                light.range = LampLightRange;
                light.intensity = LampLightIntensity;
                light.shadows = LightShadows.None;
                return PrefabUtility.SaveAsPrefabAsset(root, LampPrefabPath);
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// One lamp per ceiling light: straight down to the floor, then against the nearest wall, so it
        /// stands at the edge of the room instead of in the walkway. Re-running replaces the set.
        /// </summary>
        static void PlaceLamps(UnityEngine.SceneManagement.Scene scene, GameObject lampPrefab) {
            foreach (GameObject rootObject in scene.GetRootGameObjects())
                if (rootObject.name == LampsRoot)
                    Undo.DestroyObjectImmediate(rootObject);
            GameObject lamps = new(LampsRoot);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lamps, scene);
            Physics.SyncTransforms();
            PhysicsScene physics = scene.GetPhysicsScene();
            foreach (GameObject rootObject in scene.GetRootGameObjects())
                foreach (Light light in rootObject.GetComponentsInChildren<Light>(true)) {
                    if (!light.gameObject.name.StartsWith(CeilingLightPrefix))
                        continue;
                    if (!physics.Raycast(light.transform.position, Vector3.down, out RaycastHit floor, LampFloorSearch)) {
                        Debug.LogWarning($"[MM] No floor under {light.name}; no lamp placed there.", light);
                        continue;
                    }
                    Vector3 probe = floor.point + Vector3.up * (LampBulbHeight * LampScale * 0.5f);
                    Vector3 spot = floor.point;
                    float nearest = LampWallSearch;
                    for (int i = 0; i < 8; i++) {
                        Vector3 direction = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                        if (physics.Raycast(probe, direction, out RaycastHit wall, nearest) && wall.distance > LampWallGap) {
                            nearest = wall.distance;
                            spot = floor.point + direction * (wall.distance - LampWallGap);
                        }
                    }
                    GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(lampPrefab, lamps.transform);
                    lamp.name = "Lamp - " + light.name.Substring(CeilingLightPrefix.Length).TrimStart(' ', '-');
                    lamp.transform.position = spot;
                }
            Undo.RegisterCreatedObjectUndo(lamps, "Place murder mystery lamps");
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
