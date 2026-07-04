using System.Collections;
using System.Linq;
using UnityEngine;

namespace RyanAssets.Core {
    public static class TransformHelper {
        public static Transform FindChildRecursive(Transform self, string instance) {
            Transform target = self.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == instance);
            return target;
        }
    }
}