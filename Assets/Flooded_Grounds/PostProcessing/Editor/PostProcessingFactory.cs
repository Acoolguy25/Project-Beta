using UnityEngine;
using UnityEngine.PostProcessing;
using UnityEditor.ProjectWindowCallback;
using System.IO;

namespace UnityEditor.PostProcessing
{
    public class PostProcessingFactory
    {
        [MenuItem("Assets/Create/Post-Processing Profile", priority = 201)]
        static void MenuCreatePostProcessingProfile()
        {
            var profile = ScriptableObject.CreateInstance<PostProcessingProfile>();
            profile.fog.enabled = true;
            ProjectWindowUtil.CreateAsset(profile, "New Post-Processing Profile.asset");
        }

        internal static PostProcessingProfile CreatePostProcessingProfileAtPath(string path)
        {
            var profile = ScriptableObject.CreateInstance<PostProcessingProfile>();
            profile.name = Path.GetFileName(path);
            profile.fog.enabled = true;
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }
    }

}
