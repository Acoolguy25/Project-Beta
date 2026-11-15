#if !UNITY_SERVER
using RyanAssets.Characters.Client;
#endif
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A block that shakes once the local character stands on it, drops away, and comes back.
    /// Each client crumbles its own copy, so one player cannot knock the path out from under another.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrumbleBlock : MonoBehaviour {
        [Min(0f)] public float crumbleDelay = 0.6f;
        [Min(0.1f)] public float respawnDelay = 3f;
        [Tooltip("Visual child that shakes and falls. The solid collider stays on this object.")]
        public Transform visual;
        public AudioSource crumbleAudio;

        public void Configure(float delay, float respawn) {
            crumbleDelay = Mathf.Max(0f, delay);
            respawnDelay = Mathf.Max(0.1f, respawn);
        }

#if !UNITY_SERVER
        enum State { Solid, Shaking, Gone }
        State state;
        float stateUntil;
        Collider[] solids;
        Renderer[] renderers;
        Vector3 visualPosition;

        void Awake() {
            solids = GetComponents<Collider>();
            renderers = GetComponentsInChildren<Renderer>(true);
            if (visual != null) visualPosition = visual.localPosition;
        }

        void OnCollisionStay(Collision collision) {
            if (state != State.Solid || !LocalBody.TryGet(collision.rigidbody, out CharacterMovement movement)) return;
            // Only standing on top starts the crumble, not brushing a side.
            if (!movement.Grounded || collision.rigidbody.position.y < GetComponent<Collider>().bounds.max.y - 0.25f) return;
            state = State.Shaking;
            stateUntil = Time.time + crumbleDelay;
            if (crumbleAudio != null) crumbleAudio.Play();
        }

        void Update() {
            switch (state) {
                case State.Shaking:
                    if (visual != null)
                        visual.localPosition = visualPosition + new Vector3(Mathf.Sin(Time.time * 70f), 0f, Mathf.Cos(Time.time * 55f)) * 0.06f;
                    if (Time.time >= stateUntil) SetGone(true);
                    break;
                case State.Gone:
                    if (visual != null) visual.localPosition += Vector3.down * (9f * Time.deltaTime);
                    if (Time.time >= stateUntil) SetGone(false);
                    break;
            }
        }

        void SetGone(bool gone) {
            state = gone ? State.Gone : State.Solid;
            stateUntil = Time.time + respawnDelay;
            foreach (var solid in solids) solid.enabled = !gone;
            if (!gone) {
                if (visual != null) visual.localPosition = visualPosition;
                foreach (var r in renderers) r.enabled = true;
            } else {
                // Let the visual fall for a moment before hiding it.
                Invoke(nameof(HideRenderers), 0.6f);
            }
        }

        void HideRenderers() {
            if (state != State.Gone) return;
            foreach (var r in renderers) r.enabled = false;
        }
#endif
    }
}
