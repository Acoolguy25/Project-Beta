using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Server.ServerFeatures {
    /// <summary>
    /// An editor-only switch: authored in the Inspector and honoured only in the Editor, so a shipped
    /// server never runs with a debug shortcut someone left enabled in a scene.
    /// </summary>
    [Serializable]
    public class DebugBool {
#pragma warning disable CS0414
        [SerializeField]
        private bool _editor_value = true;
        //[SerializeField]
        //private bool _runtime_value = false;
#pragma warning restore CS0414

        public DebugBool() {
        }

        /// <summary>
        /// Starts the switch in a chosen state. The parameterless form defaults to on, which suits
        /// the timer switches it was written for but not a cheat such as infinite funds, which
        /// should never be live until a tester deliberately ticks it.
        /// </summary>
        public DebugBool(bool editorValue) {
            _editor_value = editorValue;
        }
        public bool Value {
            get {
#if UNITY_EDITOR
                return _editor_value;
#else
                //return _runtime_value;
                return false;
#endif
            }
        }
    }
}