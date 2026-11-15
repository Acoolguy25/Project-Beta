using System;
using UnityEngine;

namespace RyanAssets.Server.ServerFeatures {
    /// <summary>
    /// An editor-only multiplier for debugging pace: how fast a timer counts down, how long a
    /// build takes, anything measured in seconds.
    /// <para>
    /// The companion to <see cref="DebugBool"/>, and it hides the same way: the value is authored in
    /// the Inspector and honoured only in the Editor, so a shipped server always runs at the tuned
    /// pace no matter what someone left in the scene. It cannot simply be a <see cref="DebugBool"/>
    /// because the inert value here is 1 rather than false.
    /// </para>
    /// </summary>
    [Serializable]
    public class DebugFloat {
#pragma warning disable CS0414
        [SerializeField]
        [Tooltip("Debug multiplier, honoured in the Editor only. 1 is the authored pace.")]
        private float _editor_value = 1f;
#pragma warning restore CS0414

        public DebugFloat() {
        }

        /// <summary>
        /// Starts the knob at something other than the neutral 1, for a field whose whole point is
        /// to be non-neutral once it is switched on.
        /// </summary>
        public DebugFloat(float editorValue) {
            _editor_value = editorValue;
        }

        /// <summary>
        /// The multiplier to apply. A zero or negative authored value reads as 1 rather than as a
        /// stalled or reversed clock, so a field left at its default - or one Unity has not
        /// deserialized because it was only just added to the script - cannot wedge a countdown.
        /// </summary>
        public float Value {
            get {
#if UNITY_EDITOR
                return _editor_value > 0f ? _editor_value : 1f;
#else
                return 1f;
#endif
            }
        }
    }
}
