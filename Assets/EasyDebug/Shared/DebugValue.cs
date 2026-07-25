using System;
using System.Collections;
using UnityEngine;

namespace EasyDebug.Shared {
    [Serializable]
    public class DebugValue<T> {
        [SerializeField]
        private T _editor_value;
        [SerializeField]
        private T _runtime_value;
        public T Value {
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