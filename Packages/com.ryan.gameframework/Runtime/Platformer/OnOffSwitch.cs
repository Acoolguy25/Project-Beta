#if !UNITY_SERVER
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
#endif
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Flips an <see cref="OnOffChannels"/> channel when the local character enters its trigger: a
    /// floor button stepped on, or a "!" block bumped from below (put the trigger under the block).
    /// It latches until the character leaves the trigger, so standing on it flips once.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnOffSwitch : MonoBehaviour {
        public int channel;
        [Tooltip("Face that shows the channel's state.")]
        public Renderer face;
        [Tooltip("Face material while the channel is unflipped / flipped.")]
        public Material unflippedMaterial, flippedMaterial;
        [Tooltip("Optional visual that dips when the switch is hit.")]
        public Transform pressVisual;
        [Min(0f)] public float pressDepth = 0.15f;
        [Min(0.01f)] public float pressSeconds = 0.2f;
        // Appended fields.
        [Tooltip("Local direction the press visual moves when hit: down for a floor button, up for a \"!\" block bumped from below.")]
        public Vector3 pressDirection = Vector3.down;

        public void Configure(int switchChannel) {
            channel = switchChannel;
#if !UNITY_SERVER
            ShowState();
#endif
        }

#if !UNITY_SERVER
        /// <summary>Raised on the client when the local player hits a switch (games add the sound and flash).</summary>
        public static event System.Action<OnOffSwitch> Pressed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Pressed = null;

        bool latched;
        float pressedAt = float.NegativeInfinity;
        Vector3 pressRest;

        void Awake() {
            if (pressVisual != null) pressRest = pressVisual.localPosition;
        }

        void OnEnable() {
            OnOffChannels.Changed += OnChanged;
            OnOffChannels.Reset += ShowState;
            LocalCharacter.Teleported += OnTeleported;
            ShowState();
        }

        void OnDisable() {
            OnOffChannels.Changed -= OnChanged;
            OnOffChannels.Reset -= ShowState;
            LocalCharacter.Teleported -= OnTeleported;
        }

        void OnTeleported(LocalCharacter character) {
            if (character != LocalPlayer.Character) return;
            latched = false;
            OnOffChannels.ResetAll();
        }

        void OnTriggerEnter(Collider other) {
            if (latched || !LocalBody.TryGet(other, out _)) return;
            latched = true;
            pressedAt = Time.time;
            OnOffChannels.Flip(channel);
            Pressed?.Invoke(this);
        }

        void OnTriggerExit(Collider other) {
            if (LocalBody.TryGet(other, out _)) latched = false;
        }

        void OnChanged(int changed) {
            if (changed == channel) ShowState();
        }

        void ShowState() {
            if (face == null) return;
            Material material = OnOffChannels.IsFlipped(channel) ? flippedMaterial : unflippedMaterial;
            if (material != null) face.sharedMaterial = material;
        }

        void Update() {
            if (pressVisual == null) return;
            float t = Mathf.Clamp01((Time.time - pressedAt) / pressSeconds);
            pressVisual.localPosition = pressRest + pressDirection.normalized * (pressDepth * Mathf.Sin(t * Mathf.PI));
        }
#endif
    }
}
