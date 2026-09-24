using UnityEngine;

namespace RyanAssets.Shared.Combat {
    /// <summary>
    /// Throws an object's visuals into the air on a blast and burns them away.
    /// <para>
    /// The debris is a copy of the authored model, stripped to renderers alone, rather than the
    /// object's own hierarchy: the original still carries network components, colliders, and scripts
    /// that must not be handed to the physics scene while the server is still despawning it. The
    /// original visuals are hidden in the same frame, so the swap is not visible.
    /// </para>
    /// <para>
    /// The burn-away shrinks the pieces to nothing rather than fading their alpha. The project's
    /// structures use opaque URP Lit materials, on which writing alpha does nothing at all; fading
    /// them would mean swapping every material to a transparent surface at the worst possible
    /// moment. Shrinking reads as the wreck being consumed and costs one transform write per piece.
    /// </para>
    /// </summary>
    public static class ExplosionDebris {
        /// <summary>
        /// Launches a copy of <paramref name="source"/> and hides the original. Does nothing when
        /// the source is null or has no renderers to throw.
        /// </summary>
        public static void Launch(
            Transform source,
            Vector3 blastPosition,
            float upwardForce,
            float outwardForce,
            float spin,
            float burnSeconds) {
            if (source == null)
                return;

            Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            GameObject debris = Object.Instantiate(source.gameObject, source.position, source.rotation);
            debris.name = $"{source.name} Debris";
            debris.transform.localScale = source.lossyScale;
            Strip(debris);

            // The original is only hidden, never destroyed: the server may still be running its
            // despawn delay, and a client that hid a live object would be guessing.
            foreach (Renderer renderer in renderers) {
                if (renderer != null)
                    renderer.enabled = false;
            }

            // The root is given its own burn-away first so it is cleaned up even when every visible
            // piece has already been detached from it.
            BurnAway.Attach(debris, burnSeconds);
            LaunchPieces(debris, blastPosition, upwardForce, outwardForce, spin, burnSeconds);
        }

        /// <summary>
        /// Reduces the copy to renderers and their transforms. Anything else on it - scripts,
        /// colliders, network behaviours, audio - belongs to the live object, not to its wreckage.
        /// </summary>
        static void Strip(GameObject debris) {
            foreach (UnityEngine.Component component in debris.GetComponentsInChildren<UnityEngine.Component>(true)) {
                if (component == null)
                    continue;
                if (component is Transform || component is Renderer || component is MeshFilter)
                    continue;
                Object.Destroy(component);
            }

            foreach (Renderer renderer in debris.GetComponentsInChildren<Renderer>(true)) {
                renderer.enabled = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>
        /// Breaks the copy into its top-level parts and throws each one, so a building comes apart
        /// instead of rising as a single intact model.
        /// </summary>
        static void LaunchPieces(
            GameObject debris,
            Vector3 blastPosition,
            float upwardForce,
            float outwardForce,
            float spin,
            float burnSeconds) {
            Transform root = debris.transform;
            Transform[] pieces = CollectPieces(root);

            foreach (Transform piece in pieces) {
                if (piece != root)
                    piece.SetParent(null, true);

                var body = piece.gameObject.AddComponent<Rigidbody>();
                body.useGravity = true;
                // Debris is decoration; it must never push a unit or block a build slot.
                body.detectCollisions = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;

                Vector3 outward = piece.position - blastPosition;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.0001f)
                    outward = Random.insideUnitSphere;
                outward.y = 0f;

                body.AddForce(
                    Vector3.up * upwardForce + outward.normalized * outwardForce,
                    ForceMode.VelocityChange);
                body.AddTorque(Random.onUnitSphere * spin, ForceMode.VelocityChange);

                if (piece != root)
                    BurnAway.Attach(piece.gameObject, burnSeconds);
            }
        }

        /// <summary>
        /// The parts thrown separately: the root's own children when it has any, otherwise the root
        /// itself, so a one-piece fence section still gets launched.
        /// </summary>
        static Transform[] CollectPieces(Transform root) {
            int childCount = root.childCount;
            if (childCount == 0)
                return new[] { root };

            var pieces = new Transform[childCount];
            for (int i = 0; i < childCount; i++)
                pieces[i] = root.GetChild(i);
            return pieces;
        }

        /// <summary>Shrinks a piece of debris away and destroys it.</summary>
        sealed class BurnAway : MonoBehaviour {
            /// <summary>Set by <see cref="Attach"/> before the first Update runs.</summary>
            float seconds = 3.5f;
            float elapsed;
            Vector3 startScale;

            public static BurnAway Attach(GameObject target, float seconds) {
                var burn = target.AddComponent<BurnAway>();
                burn.seconds = Mathf.Max(0.1f, seconds);
                burn.startScale = target.transform.localScale;
                return burn;
            }

            void Awake() {
                if (startScale == Vector3.zero)
                    startScale = transform.localScale;
            }

            void Update() {
                elapsed += Time.deltaTime;
                float remaining = 1f - Mathf.Clamp01(elapsed / seconds);
                // Hold full size for the first part of the burn so the wreck is readable in the air,
                // then collapse over the tail of it.
                transform.localScale = startScale * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, remaining * 2f));

                if (elapsed >= seconds)
                    Destroy(gameObject);
            }
        }
    }
}
