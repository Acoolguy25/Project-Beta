using System.Collections;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RyanAssets.Core {
    public static class TransformHelper {
        public static Transform FindChildRecursive(Transform self, string instance) {
            Transform target = self.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == instance);
            return target;
        }

        public static Transform MkDirRecursive(string path, Scene? scene = null) {
            string[] directoryNames = path?
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(directoryName => directoryName.Trim())
                .Where(directoryName => directoryName.Length > 0)
                .ToArray();

            if (directoryNames == null || directoryNames.Length == 0)
                throw new ArgumentException("A hierarchy path is required.", nameof(path));

            Scene targetScene = scene ?? SceneManager.GetSceneByName("DontDestroyOnLoad");
            if (scene.HasValue && (!targetScene.IsValid() || !targetScene.isLoaded))
                throw new ArgumentException("The target scene must be valid and loaded.", nameof(scene));

            Transform parent = FindRoot(targetScene, directoryNames[0]);
            if (parent == null) {
                GameObject root = new(directoryNames[0]);
                if (scene.HasValue)
                    SceneManager.MoveGameObjectToScene(root, targetScene);
                else
                    UnityEngine.Object.DontDestroyOnLoad(root);

                parent = root.transform;
            }

            for (int i = 1; i < directoryNames.Length; i++)
                parent = FindOrCreateChild(parent, directoryNames[i]);

            return parent;
        }

        private static Transform FindRoot(Scene scene, string name) {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            foreach (GameObject rootObject in scene.GetRootGameObjects()) {
                if (rootObject.name == name)
                    return rootObject.transform;
            }

            return null;
        }

        private static Transform FindOrCreateChild(Transform parent, string name) {
            for (int i = 0; i < parent.childCount; i++) {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                    return child;
            }

            GameObject childObject = new(name);
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }
    }
}
