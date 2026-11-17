using RyanAssets.UI;
using RyanAssets.UI.Editor;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Universes.UniverseData.dot_invaders.Client;

namespace Universes.UniverseData.dot_invaders.Editor {
    /// <summary>
    /// Generates the Dot Invaders screen HUD (DI_HUD prefab) from the shared HudKit and the board's
    /// neon look (road and backdrop materials, bloom volume profile, emission on the base material),
    /// the base's role marks, range outline and outlined labels, then wires the HUD, look, effects and
    /// sounds into the board and road prefabs. Run from Ryan/Dot Invaders in a client (non-server)
    /// Editor, or batchmode <c>-executeMethod Universes.UniverseData.dot_invaders.Editor.DI_Authoring.BuildAll</c>;
    /// safe to re-run. A rebuild keeps tuning done in the Inspector; "Reset Board Tuning" puts the
    /// view components and generated materials back to their script and shader defaults.
    /// </summary>
    public static class DI_Authoring {
        const string Root = "Assets/Universes/UniverseData/dot_invaders";
        const string Prefabs = Root + "/Client/Prefabs";
        const string Audio = Root + "/Client/Audio";
        const string HudPath = Prefabs + "/DI_HUD.prefab";
        const string BoardPath = Prefabs + "/DI_Board.prefab";
        const string LinkPath = Prefabs + "/DI_Link.prefab";
        const string BasePath = Prefabs + "/DI_Base.prefab";
        const string LabelMaterialPath = Prefabs + "/DI_Label.mat";
        const string BaseMaterialPath = Prefabs + "/DI_Base.mat";
        const string RoadMaterialPath = Prefabs + "/DI_Road.mat";
        const string BackdropMaterialPath = Prefabs + "/DI_Backdrop.mat";
        const string NeonProfilePath = Prefabs + "/DI_NeonProfile.asset";
        const string NeonLineShader = "Dot Invaders/Neon Line";
        const string BackdropShader = "Dot Invaders/Backdrop";
        const string NeonVolumeName = "NeonVolume";
        // Below the kit's moods (20), so a MoodVolume.Apply still takes over.
        const float NeonVolumePriority = 5f;
        static readonly Color Muted = new(0.71f, 0.74f, 0.78f);

        // Role marks, in DI_Base local units (the body's radius is 2.25, its ring's 2.7).
        const int GearTeeth = 8;
        const float GearRadius = 3.2f;
        static readonly Vector3 GearToothSize = new(0.9f, 0.4f, 0.8f);
        const float FortressHalfSize = 3.25f;
        const float FortressWallThickness = 0.38f;
        const float FortressWallHeight = 0.35f;
        const float FortressTowerSize = 1f;
        const float MarkHeight = 0.25f;
        const int RangeEdgeSegments = 72;
        // Outlined, dark-backed board labels, so counts read over bloom and roads.
        const float LabelOutlineWidth = 0.22f;
        static readonly Color LabelOutlineColor = new(0.02f, 0.03f, 0.06f, 1f);
        static readonly Color LabelUnderlayColor = new(0f, 0f, 0f, 0.8f);
        const float LabelUnderlayDilate = 0.65f;
        const float LabelUnderlaySoftness = 0.55f;

        [MenuItem("Ryan/Dot Invaders/Rebuild HUD and Board")]
        public static void BuildAll() {
            DI_HUD hud = BuildHud();
            EnableBaseEmission();
            Material road = BuildMaterial(RoadMaterialPath, NeonLineShader);
            Material backdrop = BuildMaterial(BackdropMaterialPath, BackdropShader);
            VolumeProfile neon = BuildNeonProfile();
            ConfigureLink(road);
            ConfigureBase(road);

            GameObject board = PrefabUtility.LoadPrefabContents(BoardPath);
            try {
                DI_ClientController controller = board.GetComponent<DI_ClientController>();
                Set(controller, "hudPrefab", hud);
                Set(controller, "backdrop", ConfigureBackdrop(board, backdrop));
                Set(controller, "boardFx", ConfigureEffects(board));
                ConfigureNeonLook(board, neon);
                PrefabUtility.SaveAsPrefabAsset(board, BoardPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(board);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Dot Invaders: rebuilt the HUD and the board's look.");
        }

        // --- Board look ---------------------------------------------------------------------

        /// <summary>Bases, troops and the border share DI_Base.mat; their views drive emission per renderer.</summary>
        static void EnableBaseEmission() {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath)
                ?? throw new System.IO.FileNotFoundException("Dot Invaders base material is missing.", BaseMaterialPath);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
        }

        /// <summary>Creates a generated material from its shader the first time; later rebuilds keep its tuned values.</summary>
        static Material BuildMaterial(string path, string shaderName) {
            Shader shader = FindShader(shaderName);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            } else {
                material.shader = shader;
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Shader FindShader(string shaderName) => Shader.Find(shaderName)
            ?? throw new System.InvalidOperationException($"Shader '{shaderName}' was not found; check its compile errors.");

        /// <summary>
        /// Puts the board's view components (controller, effects, backdrop, road, base) and the generated materials
        /// back to their script and shader defaults, then rebuilds. Use after changing those defaults in code.
        /// </summary>
        [MenuItem("Ryan/Dot Invaders/Reset Board Tuning to Defaults")]
        public static void ResetTuningToDefaults() {
            ResetMaterial(RoadMaterialPath, NeonLineShader);
            ResetMaterial(BackdropMaterialPath, BackdropShader);
            EditPrefab(BoardPath, board => {
                ResetComponent(board.GetComponent<DI_ClientController>());
                ResetComponent(board.GetComponent<DI_BoardFx>());
                ResetComponent(board.GetComponent<DI_BoardBackdrop>());
            });
            EditPrefab(LinkPath, link => ResetComponent(link.GetComponent<DI_LinkView>()));
            EditPrefab(BasePath, view => ResetComponent(view.GetComponent<DI_BaseView>()));
            BuildAll();
        }

        static void ResetMaterial(string path, string shaderName) {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
                return;
            var defaults = new Material(FindShader(shaderName)) { name = material.name };
            EditorUtility.CopySerialized(defaults, material);
            Object.DestroyImmediate(defaults);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Copies a fresh instance's tuning defaults over the component. Fields that hold (or contain)
        /// object references keep their current wiring.
        /// </summary>
        static void ResetComponent<T>(T component) where T : MonoBehaviour {
            if (component == null)
                return;
            var holder = new GameObject("Defaults") { hideFlags = HideFlags.HideAndDontSave };
            try {
                var defaults = new SerializedObject(holder.AddComponent<T>());
                var target = new SerializedObject(component);
                SerializedProperty property = defaults.GetIterator();
                for (bool more = property.NextVisible(true); more; more = property.NextVisible(false))
                    if (property.name != "m_Script" && !HoldsReference(property))
                        target.CopyFromSerializedProperty(property);
                target.ApplyModifiedPropertiesWithoutUndo();
            } finally {
                Object.DestroyImmediate(holder);
            }
        }

        static bool HoldsReference(SerializedProperty property) {
            if (property.propertyType == SerializedPropertyType.ObjectReference)
                return true;
            if (!property.hasVisibleChildren)
                return false;
            SerializedProperty child = property.Copy(), end = property.GetEndProperty();
            for (bool more = child.NextVisible(true); more && !SerializedProperty.EqualContents(child, end); more = child.NextVisible(false))
                if (HoldsReference(child))
                    return true;
            return false;
        }

        static void EditPrefab(string path, System.Action<GameObject> edit) {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try {
                edit(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            } finally {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // --- Base ---------------------------------------------------------------------------

        /// <summary>
        /// Role marks (factory gear teeth, fortress walls and towers), the turret range outline and the
        /// outlined label material on DI_Base.prefab, wired into DI_BaseView. Rebuilt from scratch each run.
        /// </summary>
        static void ConfigureBase(Material road) {
            Material body = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath);
            EditPrefab(BasePath, root => {
                TextMeshPro troops = root.transform.Find("TroopLabel")?.GetComponent<TextMeshPro>()
                    ?? throw new MissingReferenceException("DI_Base has no TroopLabel.");
                Material label = BuildLabelMaterial(troops.fontSharedMaterial);
                troops.fontSharedMaterial = label;
                if (root.transform.Find("SpeedLabel")?.GetComponent<TextMeshPro>() is { } role)
                    role.fontSharedMaterial = label;

                GameObject gear = Rebuild(root.transform, "FactoryMark");
                for (int i = 0; i < GearTeeth; i++) {
                    float angle = 360f * i / GearTeeth;
                    Quaternion turn = Quaternion.Euler(0f, angle, 0f);
                    MarkPart(gear.transform, body, turn * new Vector3(0f, MarkHeight, GearRadius), turn, GearToothSize);
                }

                GameObject fortress = Rebuild(root.transform, "FortressMark");
                float wallLength = FortressHalfSize * 2f;
                for (int side = 0; side < 4; side++) {
                    Quaternion turn = Quaternion.Euler(0f, 90f * side, 0f);
                    MarkPart(fortress.transform, body, turn * new Vector3(0f, MarkHeight, FortressHalfSize), turn,
                        new Vector3(wallLength, FortressWallHeight, FortressWallThickness));
                    MarkPart(fortress.transform, body, turn * new Vector3(FortressHalfSize, MarkHeight, FortressHalfSize), turn,
                        Vector3.one * FortressTowerSize);
                }

                LineRenderer edge = BuildRangeEdge(root.transform, road);

                var view = new SerializedObject(root.GetComponent<DI_BaseView>());
                view.FindProperty("factoryMark").objectReferenceValue = gear;
                view.FindProperty("fortressMark").objectReferenceValue = fortress;
                view.FindProperty("rangeEdge").objectReferenceValue = edge;
                view.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        static Material BuildLabelMaterial(Material font) {
            var material = AssetDatabase.LoadAssetAtPath<Material>(LabelMaterialPath);
            if (material == null) {
                material = new Material(font);
                AssetDatabase.CreateAsset(material, LabelMaterialPath);
            }
            // Same font atlas, plus a dark outline and a soft dark underlay behind every glyph.
            material.shader = font.shader;
            material.SetTexture(ShaderUtilities.ID_MainTex, font.GetTexture(ShaderUtilities.ID_MainTex));
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, LabelOutlineWidth);
            material.SetColor(ShaderUtilities.ID_OutlineColor, LabelOutlineColor);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, LabelUnderlayColor);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, LabelUnderlayDilate);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, LabelUnderlaySoftness);
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject Rebuild(Transform parent, string name) {
            Transform old = parent.Find(name);
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var mark = new GameObject(name);
            mark.transform.SetParent(parent, false);
            mark.SetActive(false);
            return mark;
        }

        /// <summary>One flat block of a role mark, on the base's emissive material (the view sets its colour).</summary>
        static void MarkPart(Transform parent, Material material, Vector3 position, Quaternion rotation, Vector3 size) {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.name = "Part";
            part.transform.SetParent(parent, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.transform.localScale = size;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>A closed world-space line; DI_BaseView places its points on the turret's reach.</summary>
        static LineRenderer BuildRangeEdge(Transform root, Material road) {
            Transform oldEdge = root.Find("TurretRange/Edge");
            if (oldEdge != null)
                Object.DestroyImmediate(oldEdge.gameObject);
            GameObject edgeObject = Rebuild(root, "TurretRangeEdge");
            var edge = edgeObject.AddComponent<LineRenderer>();
            edge.useWorldSpace = true;
            edge.loop = true;
            edge.positionCount = RangeEdgeSegments;
            edge.alignment = LineAlignment.View;
            edge.textureMode = LineTextureMode.Stretch;
            edge.sharedMaterial = road;
            edge.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            edge.receiveShadows = false;
            return edge;
        }

        /// <summary>
        /// The game's own post-processing: a dark board with bright HDR lines wants far more bloom
        /// intensity than the kit's general moods (Night is the closest), and neutral tonemapping
        /// so team colours stay saturated instead of washing to white.
        /// </summary>
        static VolumeProfile BuildNeonProfile() {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(NeonProfilePath);
            if (profile == null) {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, NeonProfilePath);
            }
            SetOverride<Bloom>(profile, b => {
                // Above 1 plain white (troop counts, labels) stays crisp; only HDR neon blooms.
                b.threshold.Override(1.1f);
                b.intensity.Override(1.3f);
                // A tight glow: wide scatter turned every white base into a grey haze.
                b.scatter.Override(0.55f);
                b.highQualityFiltering.Override(true);
            });
            SetOverride<Tonemapping>(profile, t => t.mode.Override(TonemappingMode.Neutral));
            SetOverride<ColorAdjustments>(profile, c => {
                c.contrast.Override(8f);
                c.saturation.Override(12f);
            });
            SetOverride<Vignette>(profile, v => {
                v.intensity.Override(0.28f);
                v.smoothness.Override(0.5f);
            });
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static void SetOverride<T>(VolumeProfile profile, System.Action<T> edit) where T : VolumeComponent {
            if (!profile.TryGet(out T component)) {
                component = profile.Add<T>();
                component.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            edit(component);
            EditorUtility.SetDirty(component);
        }

        /// <summary>Roads, routes and the order arrow draw with the neon line shader (uv.x along the whole line).</summary>
        static void ConfigureLink(Material road) {
            GameObject link = PrefabUtility.LoadPrefabContents(LinkPath);
            try {
                LineRenderer line = link.GetComponent<LineRenderer>()
                    ?? throw new MissingComponentException("DI_Link has no LineRenderer.");
                line.sharedMaterial = road;
                line.textureMode = LineTextureMode.Stretch;
                line.numCapVertices = 4;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                PrefabUtility.SaveAsPrefabAsset(link, LinkPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(link);
            }
        }

        static DI_BoardBackdrop ConfigureBackdrop(GameObject board, Material backdrop) {
            Transform surface = board.transform.Find("Surface")
                ?? throw new MissingReferenceException("DI_Board has no Surface child.");
            surface.GetComponent<Renderer>().sharedMaterial = backdrop;

            var borders = new System.Collections.Generic.List<Renderer>();
            foreach (Transform child in board.transform)
                if (child.name.StartsWith("Border_") && child.TryGetComponent(out Renderer border))
                    borders.Add(border);

            DI_BoardBackdrop component = GetOrAdd<DI_BoardBackdrop>(board);
            var serialized = new SerializedObject(component);
            SerializedProperty list = serialized.FindProperty("borders");
            list.arraySize = borders.Count;
            for (int i = 0; i < borders.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = borders[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        static void ConfigureNeonLook(GameObject board, VolumeProfile profile) {
            Transform child = board.transform.Find(NeonVolumeName);
            if (child == null) {
                child = new GameObject(NeonVolumeName).transform;
                child.SetParent(board.transform, false);
            }
            Volume volume = GetOrAdd<Volume>(child.gameObject);
            volume.isGlobal = true;
            volume.priority = NeonVolumePriority;
            volume.weight = 1f;
            volume.sharedProfile = profile;
            Set(GetOrAdd<DI_NeonLook>(board), "volume", volume);
        }

        /// <summary>Game sounds (Kenney Digital Audio, CC0; see CREDITS.md) on the board's effects component.</summary>
        static DI_BoardFx ConfigureEffects(GameObject board) {
            DI_BoardFx fx = GetOrAdd<DI_BoardFx>(board);
            var serialized = new SerializedObject(fx);
            Sound(serialized, "sendSound", 0.55f, 0.3f, 0.05f, "di_send_1", "di_send_2", "di_send_3");
            Sound(serialized, "captureSound", 0.8f, 0.5f, 0.08f, "di_capture");
            Sound(serialized, "baseLostSound", 0.85f, 0.3f, 0.15f, "di_base_lost");
            // A long minimum interval: a brawl across the board must not zap ten times a second.
            Sound(serialized, "clashSound", 0.5f, 1.5f, 0.2f, "di_clash_1", "di_clash_2");
            Sound(serialized, "eliminationSound", 0.9f, 0.3f, 0.3f, "di_elimination");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return fx;
        }

        static void Sound(SerializedObject owner, string field, float volume, float pitchJitterSemitones,
            float minInterval, params string[] clipNames) {
            SerializedProperty sound = owner.FindProperty(field)
                ?? throw new System.MissingFieldException(owner.targetObject.GetType().Name, field);
            SerializedProperty clips = sound.FindPropertyRelative("clips");
            clips.arraySize = clipNames.Length;
            for (int i = 0; i < clipNames.Length; i++) {
                string path = $"{Audio}/{clipNames[i]}.ogg";
                clips.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(path)
                    ?? throw new System.IO.FileNotFoundException("Dot Invaders sound is missing.", path);
            }
            sound.FindPropertyRelative("volume").floatValue = volume;
            sound.FindPropertyRelative("pitchJitterSemitones").floatValue = pitchJitterSemitones;
            sound.FindPropertyRelative("minInterval").floatValue = minInterval;
        }

        static T GetOrAdd<T>(GameObject target) where T : Component =>
            target.TryGetComponent(out T existing) ? existing : target.AddComponent<T>();


        // --- HUD ----------------------------------------------------------------------------

        static DI_HUD BuildHud() {
            // Below the shared top bar (50); the board is the game, so the HUD stays thin.
            RectTransform root = HudKit.Canvas("DI_HUD", 15);
            DI_HUD hud = root.gameObject.AddComponent<DI_HUD>();

            // Status strip under the top bar: team, clock, bases, troops.
            RectTransform strip = UIKit.Pinned(root, "Status", new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(560f, 44f));
            HudKit.Glass(strip, 22f);
            UIKit.Row(strip.gameObject, 4f, new RectOffset(6, 10, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(strip.gameObject, true, false);
            HudKit.Emblem(strip, "Team", "hub", 34f, Color.cyan, out Image teamDisc, out _);
            TextMeshProUGUI teamName = UIKit.Text(strip, "TeamName", "Cyan team", UIText.Strong);
            UIKit.Size(teamName, 150f, -1f);
            Divider(strip);
            HudChip clock = Bare(HudKit.Chip(strip, "Clock", "timer", "3:00", Muted, 34f));
            Divider(strip);
            HudChip bases = Bare(HudKit.Chip(strip, "Bases", "castle", "0", Muted, 34f));
            HudChip troops = Bare(HudKit.Chip(strip, "Troops", "meeple_army", "0", Muted, 34f));

            DI_PowerBar power = BuildPowerBar(root);

            // Controls reminder, bottom centre, quiet.
            RectTransform controls = UIKit.Pinned(root, "Controls", new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(600f, 36f));
            HudKit.Glass(controls, 18f, 0.8f);
            UIKit.Row(controls.gameObject, 14f, new RectOffset(12, 16, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(controls.gameObject, true, false);
            Hint(controls, "Drag", "Route troops");
            Hint(controls, "Drag empty", "Select");
            Hint(controls, "RMB", "Stop");
            Hint(controls, "Esc", "Cancel");
            Hint(controls, "Gear", "Factory trains faster");
            Hint(controls, "Walls", "Fortress needs more attackers");
            HudPopup controlsPopup = HudKit.Popup(controls, true);

            HudBanner banner = HudKit.Banner(root, "MatchEnd", new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), 560f, "laurels_trophy", Color.cyan, 88f, "Match over");

            Set(hud, "teamDisc", teamDisc);
            Set(hud, "teamName", teamName);
            Set(hud, "clock", clock);
            Set(hud, "bases", bases);
            Set(hud, "troops", troops);
            Set(hud, "powerBar", power);
            Set(hud, "controls", controlsPopup);
            Set(hud, "banner", banner);
            Set(hud, "victoryIcon", HudKit.Icon("laurels_trophy"));
            Set(hud, "defeatIcon", HudKit.Icon("tombstone"));
            Set(hud, "drawIcon", HudKit.Icon("crossed_swords"));
            return UIKit.SavePrefab(root.gameObject, HudPath).GetComponent<DI_HUD>();
        }

        /// <summary>Power shares under the status strip, with the hover card under the bar.</summary>
        static DI_PowerBar BuildPowerBar(RectTransform root) {
            RectTransform frame = UIKit.Pinned(root, "Power", new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(760f, 30f));
            DI_PowerBar power = frame.gameObject.AddComponent<DI_PowerBar>();
            Image icon = UIKit.Icon(frame, "bolt", 20f, UIColorRole.Currency);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = new Vector2(0f, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(-28f, 0f);

            RectTransform track = UIKit.Fill(frame, "Track");
            HudKit.Glass(track, 15f);
            RectTransform lanes = UIKit.Fill(track, "Segments", 3f, 3f, 3f, 3f);
            RectTransform template = UIKit.Fill(lanes, "SegmentTemplate");
            Image fill = UIKit.Rounded(template, UIColorRole.Accent, 12f, raycast: false);
            Object.DestroyImmediate(fill.GetComponent<UIThemeColor>());
            TextMeshProUGUI percent = UIKit.Text(template, "Percent", "50%", UIText.Caption, UIColorRole.OnAccent, TextAlignmentOptions.Center);
            Object.DestroyImmediate(percent.GetComponent<UIThemeColor>());
            percent.characterSpacing = 0f;
            percent.overflowMode = TextOverflowModes.Masking;
            UIKit.Stretch(percent.rectTransform);

            RectTransform card = HudKit.Card(root, "PowerDetails", new Vector2(0.5f, 1f), new Vector2(0f, -152f), new Vector2(380f, 170f), popup: false);
            UIKit.Column(card.gameObject, 8f, new RectOffset(18, 18, 14, 16));
            UIKit.FitContent(card.gameObject, false, true);
            RectTransform header = UIKit.Node(card, "Header");
            UIKit.Row(header.gameObject, 10f, null, TextAnchor.MiddleLeft);
            RectTransform swatch = UIKit.Node(header, "Swatch");
            UIKit.Size(swatch, 14f, 14f);
            Image swatchImage = UIKit.Rounded(swatch, UIColorRole.Accent, 7f, raycast: false);
            Object.DestroyImmediate(swatchImage.GetComponent<UIThemeColor>());
            TextMeshProUGUI title = UIKit.Text(header, "Team", "Cyan team", UIText.Strong);
            title.richText = true;
            TextMeshProUGUI body = UIKit.Text(card, "Details", string.Empty, UIText.Body);
            body.richText = true;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.lineSpacing = 6f;

            Set(power, "bar", frame);
            Set(power, "segmentTemplate", fill);
            Set(power, "tooltip", card);
            Set(power, "tooltipSwatch", swatchImage);
            Set(power, "tooltipTitle", title);
            Set(power, "tooltipBody", body);
            return power;
        }

        static void Hint(Transform parent, string key, string action) {
            RectTransform hint = UIKit.Node(parent, key);
            UIKit.Row(hint.gameObject, 6f, null, TextAnchor.MiddleLeft);
            HudKit.KeyCap(hint, key);
            UIKit.Text(hint, "Action", action, UIText.Small, UIColorRole.TextSecondary);
        }

        static void Divider(Transform parent) {
            RectTransform line = UIKit.Node(parent, "Divider");
            UIKit.Size(line, 1f, 24f);
            UIKit.Paint(line.gameObject.AddComponent<Image>(), UIColorRole.Stroke).raycastTarget = false;
        }

        /// <summary>A chip without its own background, for use inside a strip that already has one.</summary>
        static HudChip Bare(HudChip chip) {
            chip.background.enabled = false;
            return chip;
        }

        static void Set(Object target, string field, Object value) {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new System.MissingFieldException(target.GetType().Name, field);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
