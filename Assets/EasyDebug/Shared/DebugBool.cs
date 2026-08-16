using System;
using System.Collections;
using UnityEngine;

namespace EasyDebug.Shared {
    [Serializable]
    public class DebugBool {
#pragma warning disable CS0414
        [SerializeField]
        private bool _editor_value = true;
        [SerializeField]
        private bool _runtime_value = false;
#pragma warning restore CS0414
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