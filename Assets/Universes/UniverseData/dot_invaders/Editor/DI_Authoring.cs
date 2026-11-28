using RyanAssets.Retention.Client;
using RyanAssets.Retention.Editor;
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
        // Commander cluster (rank, perks, Overdrive), bottom right: clear of the kit's level chip
        // (bottom left), the controls hint (bottom centre) and the kit's toasts (top right).
        static readonly Vector2 CommanderAnchor = new(1f, 0f);
        static readonly Vector2 CommanderOffset = new(-16f, 16f);
        const float CommanderChipHeight = 40f;
        const float PerkPanelWidth = 480f;
        const float PerkStatusWidth = 104f;
        // Above the commander cluster: three chip rows (Overdrive, send amount, perks and rank) and their spacing.
        static readonly Vector2 PerkPanelOffset = new(-16f, 168f);
        const float PerkRowHeight = 60f;
        // Board vote: tile width (picture plus padding), tagline box and the card's place, a little above centre.
        const float BoardVoteTileWidth = 240f;
        const float BoardVoteTaglineHeight = 34f;
        static readonly Vector2 BoardVoteOffset = new(0f, 20f);
        static readonly Color PerkAccent = new(0.5f, 0.85f, 1f);
        static readonly Color OverdriveColor = new(1f, 0.82f, 0.25f);

        // Role marks, in DI_Base local units (the body's radius is 2.25, its ring's 2.7).
        const int GearTeeth = 8;
        const float GearRadius = 3.2f;
        static readonly Vector3 GearToothSize = new(0.9f, 0.4f, 0.8f);
        const float FortressHalfSize = 3.25f;
        const float FortressWallThickness = 0.38f;
        const float FortressWallHeight = 0.35f;
        const float FortressTowerSize = 1f;
        const float MarkHeight = 0.25f;
        // Bounty: gold diamonds circling outside the fortress walls' reach.
        const int BountyDiamonds = 10;
        const float BountyRadius = 4.4f;
        static readonly Vector3 BountyDiamondSize = new(0.6f, 0.3f, 0.6f);
        // Match timeline card: right edge, above the commander cluster and below the kit's toasts.
        static readonly Vector2 TimelineAnchor = new(1f, 0f);
        static readonly Vector2 TimelineOffset = new(-16f, 172f);
        static readonly Vector2 TimelineGraphSize = new(340f, 120f);
        static readonly Color TimelineAccent = new(0.5f, 0.85f, 1f);
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

        /// <summary>
        /// The material's own emission, kept just above black so it is never flagged EmissiveIsBlack.
        /// Renderers set the real glow through property blocks.
        /// </summary>
        static readonly Color BaseMaterialEmission = new(1f / 128f, 1f / 128f, 1f / 128f, 1f);

        /// <summary>Bases, troops and the border share DI_Base.mat; their views drive emission per renderer.</summary>
        static void EnableBaseEmission() {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath)
                ?? throw new System.IO.FileNotFoundException("Dot Invaders base material is missing.", BaseMaterialPath);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", BaseMaterialEmission);
            // URP re-derives _EMISSION from these flags whenever it validates the material (reimport,
            // batchmode test runs, Editor quit) and strips the keyword unless a GI emissive flag is set.
            // The board is dynamic, so the realtime flag only keeps the keyword; it bakes nothing.
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
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

                GameObject bounty = Rebuild(root.transform, "BountyMark");
                for (int i = 0; i < BountyDiamonds; i++) {
                    float angle = 360f * i / BountyDiamonds;
                    Quaternion turn = Quaternion.Euler(0f, angle, 0f);
                    // Turned 45 degrees on their own axis, so each block reads as a diamond from above.
                    MarkPart(bounty.transform, body, turn * new Vector3(0f, MarkHeight, BountyRadius),
                        turn * Quaternion.Euler(0f, 45f, 0f), BountyDiamondSize);
                }

                LineRenderer edge = BuildRangeEdge(root.transform, road);

                var view = new SerializedObject(root.GetComponent<DI_BaseView>());
                view.FindProperty("factoryMark").objectReferenceValue = gear;
                view.FindProperty("fortressMark").objectReferenceValue = fortress;
                view.FindProperty("bountyMark").objectReferenceValue = bounty;
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
            Divider(strip);
            HudChip board = Bare(HudKit.Chip(strip, "Board", "explore", "Open Field", Muted, 34f));

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
            Hint(controls, "P", "Perks");
            HudPopup controlsPopup = HudKit.Popup(controls, true);

            HudBanner banner = HudKit.Banner(root, "MatchEnd", new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), 560f, "laurels_trophy", Color.cyan, 88f, "Match over");
            // The result line colours the rating change.
            if (banner.subtitle != null)
                banner.subtitle.richText = true;

            BuildCommander(root, hud);
            Set(hud, "perkPanel", BuildPerkPanel(root));
            Set(hud, "timeline", BuildTimeline(root));
            BuildBoardVote(root, banner);
            // The kit's level chip, challenges, toasts and summary card, at the kit's default placements.
            Set(hud, "retention", RetentionHudAuthoring.AddTo(root));

            Set(hud, "teamDisc", teamDisc);
            Set(hud, "teamName", teamName);
            Set(hud, "clock", clock);
            Set(hud, "bases", bases);
            Set(hud, "troops", troops);
            Set(hud, "board", board);
            Set(hud, "powerBar", power);
            Set(hud, "controls", controlsPopup);
            Set(hud, "banner", banner);
            Set(hud, "victoryIcon", HudKit.Icon("laurels_trophy"));
            Set(hud, "defeatIcon", HudKit.Icon("tombstone"));
            Set(hud, "drawIcon", HudKit.Icon("crossed_swords"));
            UseTruncateOverflow(root);
            return UIKit.SavePrefab(root.gameObject, HudPath).GetComponent<DI_HUD>();
        }

        /// <summary>Bottom right: the Overdrive prompt (X) over the perks button (P) and the rank chip.</summary>
        static void BuildCommander(RectTransform root, DI_HUD hud) {
            RectTransform commander = UIKit.Pinned(root, "Commander", CommanderAnchor, CommanderOffset, new Vector2(320f, 90f));
            UIKit.Column(commander.gameObject, 8f, null, TextAnchor.LowerRight).childForceExpandWidth = false;
            UIKit.FitContent(commander.gameObject, true, true);

            RectTransform ability = HudKit.KeyPrompt(commander, "Overdrive", "X", "Overdrive ready", OverdriveColor,
                out TMP_Text abilityLabel, out Image abilityKey, CommanderChipHeight);
            ability.GetComponent<Image>().raycastTarget = true;
            Button abilityButton = UIKit.Clickable(ability, CommanderChipHeight * 0.5f);
            HudPopup abilityPopup = HudKit.Popup(ability, false);

            // The send amount gets its own row: beside the perks and rank it ran into the controls hint.
            HudChip send = HudKit.Chip(commander, "SendAmount", "rally_troops", "Send all", PerkAccent, CommanderChipHeight);
            send.background.raycastTarget = true;
            HudKit.KeyCap(send.transform, "1-4");
            Button sendButton = UIKit.Clickable((RectTransform)send.transform, CommanderChipHeight * 0.5f);

            RectTransform profile = UIKit.Node(commander, "Profile");
            UIKit.Row(profile.gameObject, 8f, null, TextAnchor.MiddleRight);
            UIKit.FitContent(profile.gameObject, true, false);
            HudChip perks = HudKit.Chip(profile, "Perks", "star_medal", "Perks 0/1", PerkAccent, CommanderChipHeight);
            perks.background.raycastTarget = true;
            HudKit.KeyCap(perks.transform, "P");
            Button perksButton = UIKit.Clickable((RectTransform)perks.transform, CommanderChipHeight * 0.5f);
            HudChip rank = HudKit.Chip(profile, "Rank", "military_tech", "Bronze III  1000", Color.white, CommanderChipHeight);
            rank.label.richText = true;

            Set(hud, "commander", commander);
            Set(hud, "ability", abilityPopup);
            Set(hud, "abilityButton", abilityButton);
            Set(hud, "abilityKey", abilityKey);
            Set(hud, "abilityLabel", abilityLabel);
            Set(hud, "perks", perks);
            Set(hud, "perksButton", perksButton);
            Set(hud, "rank", rank);
            Set(hud, "sendAmount", send);
            Set(hud, "sendAmountButton", sendButton);
        }

        /// <summary>The end-of-match timeline: a card with each team's power share over the match.</summary>
        static DI_MatchTimeline BuildTimeline(RectTransform root) {
            RectTransform card = HudKit.Card(root, "MatchTimeline", TimelineAnchor, TimelineOffset,
                new Vector2(TimelineGraphSize.x + 36f, 220f));
            UIKit.Column(card.gameObject, 8f, new RectOffset(18, 18, 14, 16));
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.AccentStripe(card, TimelineAccent);

            RectTransform header = UIKit.Node(card, "Header");
            UIKit.Row(header.gameObject, 10f, null, TextAnchor.MiddleLeft);
            HudKit.Icon(header, "trending_up", 22f, TimelineAccent);
            TextMeshProUGUI title = UIKit.Text(header, "Title", "How the match went", UIText.Strong);
            UIKit.Size(title, -1f, -1f, 1f);

            RectTransform plot = UIKit.Node(card, "Graph");
            HudKit.Fixed(plot, TimelineGraphSize.x, TimelineGraphSize.y);
            UIKit.Rounded(plot, UIColorRole.SurfaceInset, 6f, raycast: false);
            RectTransform area = UIKit.Fill(plot, "Area", 2f, 2f, 2f, 2f);
            DI_PowerGraph graph = area.gameObject.AddComponent<DI_PowerGraph>();
            graph.raycastTarget = false;

            RectTransform axis = UIKit.Node(card, "Axis");
            UIKit.Row(axis.gameObject, 0f, null, TextAnchor.MiddleLeft);
            TextMeshProUGUI start = UIKit.Text(axis, "Start", "0:00", UIText.Caption, UIColorRole.TextMuted);
            UIKit.Size(start, -1f, -1f, 1f);
            TextMeshProUGUI end = UIKit.Text(axis, "End", "8:00", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.MidlineRight);
            UIKit.Size(end, -1f, -1f, 1f);

            TextMeshProUGUI summary = UIKit.Text(card, "Summary", "Your peak 40% at 3:12", UIText.Small, UIColorRole.TextSecondary);
            summary.richText = true;
            summary.textWrappingMode = TextWrappingModes.Normal;

            DI_MatchTimeline timeline = card.gameObject.AddComponent<DI_MatchTimeline>();
            Set(timeline, "popup", card.GetComponent<HudPopup>());
            Set(timeline, "graph", graph);
            Set(timeline, "endLabel", end);
            Set(timeline, "summary", summary);
            return timeline;
        }

        /// <summary>The perk picker: a card above the commander cluster with one template row, cloned per perk at runtime.</summary>
        // --- Board vote -----------------------------------------------------------------------

        /// <summary>
        /// The between-match board vote: a centred card with one tile per board (picture, name, tagline,
        /// numbers, votes). DI_BoardVote sits on the HUD root, which stays active while the card is hidden.
        /// </summary>
        static void BuildBoardVote(RectTransform root, HudBanner banner) {
            RectTransform card = HudKit.Card(root, "BoardVote", new Vector2(0.5f, 0.5f), BoardVoteOffset, new Vector2(BoardVoteTileWidth * 3f, 420f));
            UIKit.Column(card.gameObject, 12f, new RectOffset(18, 18, 14, 16));
            UIKit.FitContent(card.gameObject, true, true);
            HudKit.AccentStripe(card, PerkAccent);

            RectTransform header = UIKit.Node(card, "Header");
            UIKit.Row(header.gameObject, 10f, null, TextAnchor.MiddleLeft);
            HudKit.Icon(header, "explore", 26f, PerkAccent);
            TextMeshProUGUI title = UIKit.Text(header, "Title", DI_BoardLayouts.VoteTitle, UIText.Title);
            UIKit.Size(title, -1f, -1f, 1f);
            HudChip timer = HudKit.Chip(header, "Timer", "timer", "12s", PerkAccent);

            RectTransform tiles = UIKit.Node(card, "Tiles");
            UIKit.Row(tiles.gameObject, 12f, null, TextAnchor.UpperCenter);
            DI_BoardVoteTile template = BuildBoardVoteTile(tiles);

            TextMeshProUGUI hint = UIKit.Text(card, "Hint", "Click a board to vote.", UIText.Small, UIColorRole.TextSecondary, TextAlignmentOptions.Center);

            DI_BoardVote vote = root.gameObject.AddComponent<DI_BoardVote>();
            Set(vote, "popup", card.GetComponent<HudPopup>());
            Set(vote, "tileTemplate", template);
            Set(vote, "timerText", timer.label);
            Set(vote, "hintText", hint);
            Set(vote, "resultBanner", banner);
        }

        static DI_BoardVoteTile BuildBoardVoteTile(RectTransform parent) {
            RectTransform rect = UIKit.Node(parent, "BoardTemplate");
            UIKit.Size(rect, BoardVoteTileWidth);
            UIKit.Rounded(rect, UIColorRole.SurfaceInset, 12f);
            UIKit.Column(rect.gameObject, 4f, new RectOffset(8, 8, 8, 10), TextAnchor.UpperLeft);

            RectTransform frame = UIKit.Fill(rect, "Selected");
            frame.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Image selected = UIKit.Rounded(frame, UIColorRole.Accent, 12f, 0.22f, raycast: false);
            Object.DestroyImmediate(selected.GetComponent<UIThemeColor>());
            selected.color = new Color(PerkAccent.r, PerkAccent.g, PerkAccent.b, 0.22f);
            Image leader = HudKit.AccentStripe(rect, PerkAccent);

            RectTransform picture = UIKit.Node(rect, "Preview");
            float pictureWidth = BoardVoteTileWidth - 16f;
            HudKit.Fixed(picture, pictureWidth, Mathf.Round(pictureWidth * DI_Arena.HalfHeight / DI_Arena.HalfWidth));
            RawImage preview = picture.gameObject.AddComponent<RawImage>();
            preview.raycastTarget = false;

            TextMeshProUGUI name = UIKit.Text(rect, "Title", "Board", UIText.Strong);
            TextMeshProUGUI tagline = UIKit.Text(rect, "Tagline", "What the board is like.", UIText.Small, UIColorRole.TextSecondary);
            tagline.textWrappingMode = TextWrappingModes.Normal;
            // Two lines for every tagline, so the tiles line up.
            UIKit.Size(tagline, -1f, BoardVoteTaglineHeight);
            TextMeshProUGUI stats = UIKit.Text(rect, "Stats", "32 bases, 8 min, 4 bot teams", UIText.Caption, UIColorRole.TextMuted);
            TextMeshProUGUI votes = UIKit.Text(rect, "Votes", "0 votes", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.MidlineRight);
            Object.DestroyImmediate(votes.GetComponent<UIThemeColor>());
            votes.color = PerkAccent;
            Button button = UIKit.Clickable(rect, 12f);

            DI_BoardVoteTile tile = rect.gameObject.AddComponent<DI_BoardVoteTile>();
            Set(tile, "button", button);
            Set(tile, "preview", preview);
            Set(tile, "title", name);
            Set(tile, "tagline", tagline);
            Set(tile, "stats", stats);
            Set(tile, "votes", votes);
            Set(tile, "selectedFrame", selected);
            Set(tile, "leaderMark", leader);
            return tile;
        }

        static DI_PerkPanel BuildPerkPanel(RectTransform root) {
            RectTransform card = HudKit.Card(root, "PerkPanel", CommanderAnchor, PerkPanelOffset, new Vector2(PerkPanelWidth, 480f));
            UIKit.Column(card.gameObject, 8f, new RectOffset(18, 18, 14, 16));
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.AccentStripe(card, PerkAccent);

            RectTransform header = UIKit.Node(card, "Header");
            UIKit.Row(header.gameObject, 10f, null, TextAnchor.MiddleLeft);
            HudKit.Icon(header, "star_medal", 24f, PerkAccent);
            TextMeshProUGUI title = UIKit.Text(header, "Title", "Commander perks", UIText.Title);
            UIKit.Size(title, -1f, -1f, 1f);
            HudKit.KeyCap(header, "P");
            TextMeshProUGUI slots = UIKit.Text(card, "Slots", "1 / 1 slots", UIText.Small, UIColorRole.TextSecondary);
            slots.textWrappingMode = TextWrappingModes.Normal;

            RectTransform rows = UIKit.Node(card, "Rows");
            UIKit.Column(rows.gameObject, 6f);
            DI_PerkRow template = BuildPerkRow(rows);

            DI_PerkPanel panel = card.gameObject.AddComponent<DI_PerkPanel>();
            Set(panel, "popup", card.GetComponent<HudPopup>());
            Set(panel, "frame", card);
            Set(panel, "rowTemplate", template);
            Set(panel, "slotsText", slots);
            var serialized = new SerializedObject(panel);
            SerializedProperty icons = serialized.FindProperty("icons");
            icons.ClearArray();
            foreach (DI_PerkDefinition perk in DI_Perks.All) {
                icons.InsertArrayElementAtIndex(icons.arraySize);
                SerializedProperty entry = icons.GetArrayElementAtIndex(icons.arraySize - 1);
                entry.FindPropertyRelative("id").stringValue = perk.Id;
                entry.FindPropertyRelative("sprite").objectReferenceValue = HudKit.Icon(perk.Icon);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        static DI_PerkRow BuildPerkRow(RectTransform parent) {
            RectTransform rect = UIKit.Node(parent, "PerkTemplate");
            // A minimum height only: a long description wraps and the row grows with it.
            rect.gameObject.AddComponent<LayoutElement>().minHeight = PerkRowHeight;
            UIKit.Rounded(rect, UIColorRole.SurfaceInset, 10f);
            UIKit.Row(rect.gameObject, 12f, new RectOffset(14, 12, 6, 6), TextAnchor.MiddleLeft);
            CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
            Image mark = HudKit.AccentStripe(rect, PerkAccent, 4f, left: true);

            Image icon = HudKit.Icon(rect, "bolt", 30f, PerkAccent);
            RectTransform text = UIKit.Node(rect, "Text");
            UIKit.Column(text.gameObject, 2f, null, TextAnchor.MiddleLeft);
            UIKit.Size(text, -1f, -1f, 1f);
            TextMeshProUGUI title = UIKit.Text(text, "Title", "Perk", UIText.Strong);
            TextMeshProUGUI description = UIKit.Text(text, "Description", "What it does.", UIText.Small, UIColorRole.TextSecondary);
            description.textWrappingMode = TextWrappingModes.Normal;
            TextMeshProUGUI status = UIKit.Text(rect, "Status", "Level 2", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.MidlineRight);
            Object.DestroyImmediate(status.GetComponent<UIThemeColor>());
            UIKit.Size(status, PerkStatusWidth);
            Button button = UIKit.Clickable(rect, 10f);

            DI_PerkRow row = rect.gameObject.AddComponent<DI_PerkRow>();
            Set(row, "button", button);
            Set(row, "icon", icon);
            Set(row, "title", title);
            Set(row, "description", description);
            Set(row, "status", status);
            Set(row, "equippedMark", mark);
            Set(row, "group", group);
            return row;
        }

        /// <summary>
        /// The HUD font has no ellipsis glyph, so TMP fell back to Truncate at runtime with a console warning
        /// for every ellipsis label. Truncate is set up front instead.
        /// </summary>
        static void UseTruncateOverflow(Component root) {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                if (text.overflowMode == TextOverflowModes.Ellipsis)
                    text.overflowMode = TextOverflowModes.Truncate;
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
