using System.Collections.Generic;
using FishNet.Managing.Object;
using FishNet.Object;
using RyanAssets.Characters.Shared;
using UnityEditor;
using UnityEngine;

namespace RyanAssets.Editor {
    /// <summary>Shared monster art and catalog; the portrait renders the same face used by the NPC.</summary>
    public static class NPCCharacterAuthoring {
        public const string Root = "Assets/RyanAssets/Characters/Monster";
        static void Folder(string path) {
            if(AssetDatabase.IsValidFolder(path))return;
            string parent=System.IO.Path.GetDirectoryName(path).Replace('\\','/');
            Folder(parent);AssetDatabase.CreateFolder(parent,System.IO.Path.GetFileName(path));
        }
        [MenuItem("Ryan/Characters/Rebuild Monster and NPC Catalog")]
        public static void Build() {
            foreach(string folder in new[]{Root+"/Prefabs",Root+"/Materials",Root+"/Data","Assets/RyanAssets/Characters/Resources"})Folder(folder);
            BuildFace();BuildMonster();
            const string path="Assets/RyanAssets/Characters/Resources/NPCCharacters.asset";
            var catalog=AssetDatabase.LoadAssetAtPath<NPCCharacterCatalog>(path);
            if(catalog==null){catalog=ScriptableObject.CreateInstance<NPCCharacterCatalog>();AssetDatabase.CreateAsset(catalog,path);}
            catalog.robot=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RyanAssets/Characters/RobotNPC.prefab");
            catalog.monster=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/Monster.prefab");
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        }
        static void BuildBody(GameObject root, Material shadow) {
            var flesh=Material("DrownedBody",new Color(0.10f,0.115f,0.085f));
            var animator = root.GetComponent<Animator>();
            void Segment(HumanBodyBones a,HumanBodyBones b,float radius,Material material) {
                var from=animator.GetBoneTransform(a);var to=animator.GetBoneTransform(b);
                if(from==null||to==null)return;
                Vector3 delta=to.position-from.position;
                var obj=Shape("Drowned "+a,PrimitiveType.Capsule,from,from.InverseTransformPoint((from.position+to.position)*0.5f),new Vector3(radius,delta.magnitude*0.5f,radius),material);
                obj.transform.rotation=Quaternion.FromToRotation(Vector3.up,delta.normalized);
            }
            Segment(HumanBodyBones.Hips,HumanBodyBones.Neck,0.34f,flesh);
            Segment(HumanBodyBones.Neck,HumanBodyBones.Head,0.11f,shadow);
            Segment(HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,0.115f,flesh);
            Segment(HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,0.075f,shadow);
            Segment(HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,0.115f,flesh);
            Segment(HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand,0.075f,shadow);
            Segment(HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,0.16f,flesh);
            Segment(HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot,0.09f,shadow);
            Segment(HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,0.16f,flesh);
            Segment(HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot,0.09f,shadow);
            var chest=animator.GetBoneTransform(HumanBodyBones.Neck);
            if(chest!=null)for(int i=0;i<5;i++) {
                var rib=Shape("Exposed rib "+i,PrimitiveType.Capsule,chest,Vector3.zero,new Vector3(0.032f,0.15f-i*0.007f,0.032f),flesh);
                rib.transform.position=chest.position+root.transform.forward*0.14f-root.transform.up*(0.17f+i*0.05f);
                rib.transform.rotation=root.transform.rotation*Quaternion.Euler(0,0,90);
            }
        }
        static Material Material(string name, Color color, bool glow = false, string folder = null) {
            string path = (folder ?? Root + "/Materials") + "/" + name + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null) { result = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(result, path); }
            result.SetColor("_BaseColor", color);
            result.SetFloat("_Smoothness", 0.18f);
            if (glow) { result.EnableKeyword("_EMISSION"); result.SetColor("_EmissionColor", color * 2f); }
            EditorUtility.SetDirty(result);
            return result;
        }

        static GameObject Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, Vector3 angles = default) {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localEulerAngles = angles;
            obj.transform.localScale = scale;
            Object.DestroyImmediate(obj.GetComponent<Collider>());
            obj.GetComponent<Renderer>().sharedMaterial = material;
            return obj;
        }

        static void Register(GameObject prefab) {
            var catalog = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>("Assets/DefaultPrefabObjects.asset");
            catalog.AddObject(prefab.GetComponent<NetworkObject>(), true);
            EditorUtility.SetDirty(catalog);
        }

        static void SetPrefabHash(GameObject root, string path) {
            // A cloned NetworkObject carries its source's hash. Assign the new
            // identity before saving, including when replacing an existing prefab.
            var key = new System.Text.StringBuilder();
            foreach (char c in (path + root.name).Trim().ToLowerInvariant())
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') key.Append(c);
            root.GetComponent<NetworkObject>().SetAssetPathHash(GameKit.Dependencies.Utilities.Hashing.GetStableHashU64(key.ToString()));
        }

        static void BuildFace() {
            var skin = Material("DrownedSkin", new Color(0.31f,0.33f,0.27f));
            var bone = Material("DrownedTeeth", new Color(0.62f,0.57f,0.39f));
            var voidMat = Material("DrownedVoid", new Color(0.001f,0.001f,0.001f));
            var scar = Material("DrownedScars", new Color(0.12f,0.009f,0.012f));
            var eye = Material("PresenceEyes", new Color(0.48f,0.18f,0.12f), true);
            var root = new GameObject("PresenceFace");
            Shape("Collapsed skull", PrimitiveType.Sphere, root.transform, new Vector3(0,0.09f,0), new Vector3(0.65f,0.92f,0.5f),skin,new Vector3(0,0,-9));
            Shape("Left cheek", PrimitiveType.Sphere, root.transform,new Vector3(-0.25f,-0.1f,0.13f),new Vector3(0.22f,0.62f,0.29f),skin,new Vector3(0,0,-14));
            Shape("Right cheek", PrimitiveType.Sphere, root.transform,new Vector3(0.25f,-0.06f,0.13f),new Vector3(0.18f,0.65f,0.26f),skin,new Vector3(0,0,19));
            Shape("Hanging jaw", PrimitiveType.Sphere, root.transform,new Vector3(0,-0.41f,0.12f),new Vector3(0.45f,0.27f,0.3f),skin);
            Shape("Mouth", PrimitiveType.Sphere, root.transform,new Vector3(0,-0.19f,0.265f),new Vector3(0.39f,0.63f,0.16f),voidMat);
            for(int side=-1;side<=1;side+=2) {
                Shape("Sunken socket",PrimitiveType.Sphere,root.transform,new Vector3(side*0.155f,0.205f,0.235f),new Vector3(0.22f,0.17f,0.13f),voidMat,new Vector3(0,0,side*17));
                Shape("Pinpoint eye",PrimitiveType.Sphere,root.transform,new Vector3(side*0.155f,0.205f,0.305f),new Vector3(0.032f,0.043f,0.022f),eye);
                for(int i=0;i<3;i++) Shape("Tear",PrimitiveType.Cube,root.transform,new Vector3(side*(0.12f+i*0.036f),0.065f-i*0.02f,0.281f),new Vector3(0.012f,0.2f,0.018f),scar,new Vector3(0,0,side*(i*7+8)));
            }
            for(int i=0;i<7;i++) {
                float x=(i-3)*0.049f;
                Shape("Upper tooth",PrimitiveType.Capsule,root.transform,new Vector3(x,0.035f-Mathf.Abs(i-3)*0.012f,0.348f),new Vector3(0.027f,0.065f+(i%2)*0.015f,0.035f),bone,new Vector3(0,0,(i-3)*5));
                Shape("Lower tooth",PrimitiveType.Capsule,root.transform,new Vector3(x,-0.39f+Mathf.Abs(i-3)*0.014f,0.337f),new Vector3(0.024f,0.08f,0.028f),bone,new Vector3(0,0,(i-3)*-6));
            }
            Shape("Nose cavity",PrimitiveType.Sphere,root.transform,new Vector3(0,0.11f,0.283f),new Vector3(0.068f,0.1f,0.05f),voidMat);
            PrefabUtility.SaveAsPrefabAsset(root,Root + "/Prefabs/PresenceFace.prefab");
            var preview = new PreviewRenderUtility();
            try {
                preview.AddSingleGO(root);
                preview.camera.transform.position = new Vector3(0,0.015f,2.4f);
                preview.camera.transform.rotation = Quaternion.Euler(0,180,0);
                preview.camera.fieldOfView=32; preview.camera.nearClipPlane=0.01f;
                preview.camera.clearFlags=CameraClearFlags.SolidColor; preview.camera.backgroundColor=new Color(0,0,0,0);
                preview.lights[0].intensity=2.5f; preview.lights[0].transform.rotation=Quaternion.Euler(35,-140,0);
                preview.lights[1].intensity=0.4f; preview.lights[1].color=new Color(0.65f,0.16f,0.12f);
                preview.BeginStaticPreview(new Rect(0,0,1024,1024));
                preview.Render(true);
                var picture=preview.EndStaticPreview();
                string path=Root + "/Data/PresencePortrait.png";
                System.IO.File.WriteAllBytes(path,picture.EncodeToPNG());
                Object.DestroyImmediate(picture);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaIsTransparency=true; importer.mipmapEnabled=false; importer.SaveAndReimport();
            } finally { preview.Cleanup(); }
        }

        static void BuildMonster() {
            var body = Material("PresenceBody", new Color(0.018f, 0.013f, 0.017f));
            var root = PrefabUtility.LoadPrefabContents("Assets/RyanAssets/Characters/RobotNPC.prefab");
            try {
                root.name = "Monster";
                root.transform.localScale = Vector3.one;
                root.GetComponent<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var characterData = new SerializedObject(root.GetComponent<GameCharacter>());
                characterData.FindProperty("showNameTag").boolValue = false;
                characterData.ApplyModifiedPropertiesWithoutUndo();
                var agent = root.GetComponent<UnityEngine.AI.NavMeshAgent>();
                for (int i = 0; agent != null && i < UnityEngine.AI.NavMesh.GetSettingsCount(); i++) {
                    int id = UnityEngine.AI.NavMesh.GetSettingsByIndex(i).agentTypeID;
                    if (UnityEngine.AI.NavMesh.GetSettingsNameFromID(id) == "HorrorPresence") agent.agentTypeID = id;
                }
                if (agent != null) { agent.radius = 0.3f; agent.height = 2.05f; agent.baseOffset = 0; }

                var robotColor = root.GetComponent<RobotColor>();
                if (robotColor != null) Object.DestroyImmediate(robotColor);
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = body;
                    renderer.sharedMaterials = materials;
                    renderer.enabled = false;
                }
                // This prefab also contains a separate ragdoll skeleton with
                // duplicate bone names. Bind art to the animated avatar explicitly.
                var animator = root.GetComponent<Animator>();
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) {
                    var face = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PresenceFace.prefab"), head);
                    face.transform.position = head.position + root.transform.up * 0.08f + root.transform.forward * 0.025f;
                    face.transform.localRotation = Quaternion.Inverse(head.rotation) * root.transform.rotation;
                    face.transform.localScale = Vector3.one * 0.42f;
                    // Long, uneven fingers keep the animated silhouette recognizable in darkness.
                    foreach (var hand in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand }) {
                        var bone = animator.GetBoneTransform(hand);
                        if (bone == null) continue;
                        for (int finger = 0; finger < 4; finger++)
                            Shape("Finger" + finger, PrimitiveType.Capsule, bone, new Vector3((finger-1.5f)*0.033f,-0.15f,0.035f), new Vector3(0.025f,0.15f + finger*0.018f,0.025f), body, new Vector3(0,0,(finger-1.5f)*9));
                    }
                }
                BuildBody(root, body);
                root.transform.localScale = Vector3.one * 1.1f;
                SetPrefabHash(root, Root + "/Prefabs/Monster.prefab");
                var saved = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Monster.prefab");
                Register(saved);
            } finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
