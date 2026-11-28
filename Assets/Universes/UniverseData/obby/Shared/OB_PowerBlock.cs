using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// The look of a "?" block: gold with question marks while the local player can still bump it, a plain
    /// spent block once they have, with a little hop when bumped. Presentation only; the server decides
    /// bumps and power-ups. Lives in the shared assembly because the prefab is also built on the server.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OB_PowerBlock : MonoBehaviour {
        [Tooltip("The drawn block (body and marks) that hops when bumped; the solid collider stays on the root.")]
        public Transform visual;
        public Renderer body;
        [Tooltip("Body material while the block can be bumped, and once it is spent.")]
        public Material freshMaterial, usedMaterial;
        [Tooltip("The question marks, hidden once the block is spent.")]
        public GameObject[] marks;
        [Min(0f)] public float bumpHeight = 0.45f;
        [Min(0.01f)] public float bumpSeconds = 0.28f;
        [Tooltip("Degrees per second the fresh block idly turns, so it catches the eye.")]
        public float idleSpin = 40f;

        public OB_PowerUp Power { get; private set; }
        public bool Used { get; private set; }

        float bumpedAt = float.NegativeInfinity;
        Vector3 rest;

        public void Configure(OB_PowerUp power) {
            Power = power;
            SetUsed(false, false);
        }

        void Awake() {
            if (visual != null) rest = visual.localPosition;
        }

        /// <summary>Shows the block fresh or spent; <paramref name="bump"/> plays the hop (the local player just hit it).</summary>
        public void SetUsed(bool used, bool bump) {
            if (bump) bumpedAt = Time.time;
            if (Used == used && body != null && body.sharedMaterial == (used ? usedMaterial : freshMaterial)) return;
            Used = used;
            if (body != null) {
                Material material = used ? usedMaterial : freshMaterial;
                if (material != null) body.sharedMaterial = material;
            }
            if (marks != null)
                foreach (var mark in marks)
                    if (mark != null) mark.SetActive(!used);
        }

#if !UNITY_SERVER
        void Update() {
            if (visual == null) return;
            float t = (Time.time - bumpedAt) / bumpSeconds;
            float hop = t is >= 0f and < 1f ? bumpHeight * Mathf.Sin(t * Mathf.PI) : 0f;
            visual.localPosition = rest + Vector3.up * hop;
            if (!Used && idleSpin != 0f) visual.localRotation = Quaternion.Euler(0f, Time.time * idleSpin % 360f, 0f);
        }
#endif
    }
}
