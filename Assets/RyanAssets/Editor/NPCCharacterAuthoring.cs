using FishNet.Managing.Object;
using FishNet.Object;
using RyanAssets.Characters.Shared;
using UnityEditor;
using UnityEngine;

namespace RyanAssets.Editor {
    /// <summary>Builds the networked priest and its portrait from the same Humanoid model.</summary>
    public static class NPCCharacterAuthoring {
        public const string Root = "Assets/RyanAssets/Characters/Monster";
        const string PriestPath = "Assets/Cursed Priest/Prefab/Priest.prefab";
        const string RobotPath = "Assets/RyanAssets/Characters/RobotNPC.prefab";

        static void Folder(string path) {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        [MenuItem("Ryan/Characters/Rebuild Monster and NPC Catalog")]
        public static void Build() {
            var priest = AssetDatabase.LoadAssetAtPath<GameObject>(PriestPath);
            var avatar = priest != null ? priest.GetComponent<Animator>()?.avatar : null;
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new System.InvalidOperationException("Cursed Priest requires a valid Humanoid avatar before rebuilding the monster.");
            foreach (string folder in new[] { Root + "/Prefabs", Root + "/Materials", Root + "/Data", "Assets/RyanAssets/Characters/Resources" }) Folder(folder);
            BuildMonster();
            BuildFace();
            const string path = "Assets/RyanAssets/Characters/Resources/NPCCharacters.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<NPCCharacterCatalog>(path);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<NPCCharacterCatalog>(); AssetDatabase.CreateAsset(catalog, path); }
            catalog.robot = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPath);
            catalog.monster = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Monster.prefab");
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        static GameObject CreatePriest() {
            var priest = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PriestPath));
            PrefabUtility.UnpackPrefabInstance(priest, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var renderer in priest.GetComponentsInChildren<Renderer>(true)) {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = PriestMaterial(materials[i]);
                renderer.sharedMaterials = materials;
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            return priest;
        }

        static Material PriestMaterial(Material source) {
            if (source == null) throw new System.InvalidOperationException("Missing priest material.");
            string path = Root + "/Materials/CursedPriest" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            // Preserve the supplied prefab's textures in project-owned URP materials.
            bool standard = source.shader.name == "Standard";
            string baseMap = standard ? "_MainTex" : "_BaseMap";
            material.SetTexture("_BaseMap", source.GetTexture(baseMap));
            material.SetColor("_BaseColor", source.GetColor(standard ? "_Color" : "_BaseColor"));
            material.SetTextureScale("_BaseMap", source.GetTextureScale(baseMap));
            material.SetTextureOffset("_BaseMap", source.GetTextureOffset(baseMap));
            material.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
            material.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
            material.SetTexture("_MetallicGlossMap", source.GetTexture("_MetallicGlossMap"));
            material.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            material.SetFloat("_Smoothness", source.GetFloat(standard ? "_Glossiness" : "_Smoothness"));
            if (material.GetTexture("_BumpMap") != null) material.EnableKeyword("_NORMALMAP");
            if (material.GetTexture("_MetallicGlossMap") != null) material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        static void BuildMonster() {
            var root = PrefabUtility.LoadPrefabContents(RobotPath);
            GameObject priest = null;
            try {
                root.name = "Monster";
                root.transform.localScale = Vector3.one;
                var animator = root.GetComponent<Animator>();
                // Keep the robot Animator, controller, NetworkAnimator reference,
                // event receiver and audio. Retarget with the original priest skeleton.
                priest = CreatePriest();
                var priestAnimator = priest.GetComponent<Animator>();
                animator.avatar = priestAnimator.avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Object.DestroyImmediate(priestAnimator);
                var robotColor = root.GetComponent<RobotColor>();
                if (robotColor != null) Object.DestroyImmediate(robotColor);
                var drivers = root.GetComponents<CharacterAnimator>();
                for (int i = 1; i < drivers.Length; i++) Object.DestroyImmediate(drivers[i]);
                // Retain Geometry's sibling slot for shared ragdoll initialization,
                // but discard both robot skeletons and all robot renderers.
                Transform geometry = root.transform.Find("Geometry");
                while (geometry.childCount > 0) Object.DestroyImmediate(geometry.GetChild(0).gameObject);
                Object.DestroyImmediate(root.transform.Find("Skeleton").gameObject);
                while (priest.transform.childCount > 0) priest.transform.GetChild(0).SetParent(root.transform, false);
                Object.DestroyImmediate(priest);
                priest = null;
                foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = root.layer;
                animator.Rebind();
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head == null) throw new System.InvalidOperationException("Priest head is not bound to the robot Animator.");
                var character = root.GetComponent<GameCharacter>();
                character.CharacterCamera.position = head.position;
                var characterData = new SerializedObject(character);
                characterData.FindProperty("showNameTag").boolValue = false;
                characterData.ApplyModifiedPropertiesWithoutUndo();
                var agent = root.GetComponent<UnityEngine.AI.NavMeshAgent>();
                for (int i = 0; i < UnityEngine.AI.NavMesh.GetSettingsCount(); i++) {
                    int id = UnityEngine.AI.NavMesh.GetSettingsByIndex(i).agentTypeID;
                    if (UnityEngine.AI.NavMesh.GetSettingsNameFromID(id) == "HorrorPresence") agent.agentTypeID = id;
                }
                agent.radius = 0.1f; agent.height = 1.05f; agent.baseOffset = 0; agent.autoBraking = false;
                var hitbox = root.GetComponent<BoxCollider>();
                hitbox.size = new Vector3(0.2f, 0.9f, 0.2f);
                hitbox.center = new Vector3(0, 0.45f, 0);
                root.transform.localScale = Vector3.one * 1.1f;
                string path = Root + "/Prefabs/Monster.prefab";
                SetPrefabHash(root, path);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                var networkCatalog = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>("Assets/DefaultPrefabObjects.asset");
                networkCatalog.AddObject(saved.GetComponent<NetworkObject>(), true);
                EditorUtility.SetDirty(networkCatalog);
            } finally {
                if (priest != null) Object.DestroyImmediate(priest);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void SetPrefabHash(GameObject root, string path) {
            var key = new System.Text.StringBuilder();
            foreach (char c in (path + root.name).Trim().ToLowerInvariant())
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') key.Append(c);
            root.GetComponent<NetworkObject>().SetAssetPathHash(GameKit.Dependencies.Utilities.Hashing.GetStableHashU64(key.ToString()));
        }

        static void BuildFace() {
            var root = CreatePriest();
            root.name = "PresenceFace";
            Object.DestroyImmediate(root.GetComponent<Animator>());
            Bounds bounds = default;
            bool found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) {
                renderer.enabled = renderer.name is "Head" or "Hat";
                if (!renderer.enabled) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found) { Object.DestroyImmediate(root); throw new System.InvalidOperationException("Priest face renderers are missing."); }
            PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/PresenceFace.prefab");
            // Static previews do not tick a SkinnedMeshRenderer's deformation.
            // Bake only the preview copy so the saved face retains the source rig.
            var previewMeshes = new System.Collections.Generic.List<Mesh>();
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                if (!skin.enabled) continue;
                var mesh = new Mesh();
                skin.BakeMesh(mesh);
                previewMeshes.Add(mesh);
                var materials = skin.sharedMaterials;
                var target = skin.gameObject;
                Object.DestroyImmediate(skin);
                target.AddComponent<MeshFilter>().sharedMesh = mesh;
                target.AddComponent<MeshRenderer>().sharedMaterials = materials;
            }
            var preview = new PreviewRenderUtility();
            try {
                preview.AddSingleGO(root);
                float distance = Mathf.Max(bounds.extents.y, bounds.extents.x) / Mathf.Tan(16f * Mathf.Deg2Rad) * 1.2f + bounds.extents.z;
                preview.camera.transform.position = bounds.center + Vector3.forward * distance;
                preview.camera.transform.rotation = Quaternion.Euler(0, 180, 0);
                preview.camera.fieldOfView = 32;
                preview.camera.nearClipPlane = 0.01f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = Color.clear;
                preview.lights[0].intensity = 2.5f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35, -140, 0);
                preview.lights[1].intensity = 0.7f;
                preview.lights[1].color = new Color(0.65f, 0.16f, 0.12f);
                preview.BeginStaticPreview(new Rect(0, 0, 1024, 1024));
                preview.Render(true);
                var picture = preview.EndStaticPreview();
                string path = Root + "/Data/PresencePortrait.png";
                System.IO.File.WriteAllBytes(path, picture.EncodeToPNG());
                Object.DestroyImmediate(picture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            } finally {
                preview.Cleanup();
                foreach (var mesh in previewMeshes) Object.DestroyImmediate(mesh);
            }
        }
    }
}
