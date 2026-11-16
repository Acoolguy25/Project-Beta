using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>
    /// A power-up spot in the map scene. The server reads <see cref="kind"/> and the position and decides
    /// who takes it; clients show or hide <see cref="visual"/> from the state broadcast.
    /// </summary>
    public sealed class CTF_PowerUp : MonoBehaviour {
        public CTF_PowerUpKind kind;
        [Tooltip("Hidden while the power-up is recharging.")]
        public GameObject visual;
        [Tooltip("Spins and bobs while available (the icon).")]
        public Transform spinner;
#pragma warning disable CS0414 // read only by the client's Update; the server compiles it out
        [SerializeField, Min(0f)] float spinDegreesPerSecond = 120f;
        [SerializeField, Min(0f)] float bobHeight = 0.35f;
        [SerializeField, Min(0f)] float bobSpeed = 2.2f;
#pragma warning restore CS0414

        Vector3 spinnerHome;

        public bool Available { get; private set; } = true;
        public Vector3 Position => transform.position;

        void Awake() {
            if (spinner != null) spinnerHome = spinner.localPosition;
        }

        public void SetAvailable(bool available) {
            Available = available;
            if (visual != null && visual.activeSelf != available) visual.SetActive(available);
        }

#if !UNITY_SERVER
        void Update() {
            if (spinner == null || !Available) return;
            float t = Time.time;
            spinner.localRotation = Quaternion.Euler(0f, t * spinDegreesPerSecond, 0f);
            spinner.localPosition = spinnerHome + Vector3.up * (bobHeight * Mathf.Sin(t * bobSpeed));
        }
#endif
    }
}
