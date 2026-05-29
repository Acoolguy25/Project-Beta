using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RyanAssets.Core {
    public class StaticPersistence : MonoBehaviour {
        static HashSet<string> _inits;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            _inits = new();
        }
        void Awake() {
            if (_inits.Contains(gameObject.name)) {
                Destroy(gameObject);
                return;
            }
            _inits.Add(gameObject.name);
            DontDestroyOnLoad(gameObject);
        }
    }
}
