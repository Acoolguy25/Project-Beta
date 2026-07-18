using System;
using System.Collections;
using UnityEngine;

namespace EasyDebug.Shared {
    [Serializable]
    public class DebugBool {
        [SerializeField]
        private bool _editor_value;
        [SerializeField]
        private bool _runtime_value = false;
        public bool Value {
            get {
#if UNITY_EDITOR
                return _editor_value;
#else
                return _runtime_value;
#endif
            }
        }
    }
}