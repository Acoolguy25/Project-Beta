using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Shared.Combat {
    /// <summary>
    /// Wrecks an object's visuals on a blast and burns them away.
    /// <para>
    /// The debris is a copy of the authored model, stripped to renderers alone, rather than the
    /// object's own hierarchy: the original still carries network components, colliders, and scripts
    /// that must not be handed to the physics scene while the server is still despawning it. The
    /// original visuals are hidden in the same frame, so the swap is not visible.
    /// </para>
    /// <para>
    /// How the copy is wrecked depends on what it is made of. A model built from several parts comes
    /// apart: each part is thrown up and away from the blast's centre - the middle of the model, not
    /// its pivot, which on imported art can sit anywhere - harder the further out it sits, so the
    /// middle rises while the edges fly clear. A model that is a single mesh, as most of the imported
    /// buildings are, has nothing to come apart into; throwing it whole sent the entire building
    /// tumbling off in whichever direction its pivot happened to be offset. It collapses in place
    /// instead, sinking and leaning as it burns.
    /// </para>
    /// <para>
    /// The burn-away shrinks the pieces to nothing rather than fading their alpha. The project's
    /// structures use opaque URP Lit materials, on which writing alpha does nothing at all; fading
    /// them would mean swapping every material to a transparent surface at the worst possible
    /// moment. Shrinking reads as the wreck being consumed and costs one transform write per piece.
    /// </para>
    /// </summary>
    public static class ExplosionDebris {
        /// <summary>How far a collapsing wreck leans over as it goes down, in degrees.</summary>
        const float CollapseLeanDegrees = 14f;

        /// <summary>
        /// The combined bounds of <paramref name="source"/>'s visible renderers - the centre an
        /// explosion should be drawn at and the size it should be drawn to.
        /// </summary>
        public static bool TryGetVisualBounds(Transform source, out Bounds bounds) {
            bounds = default;
            if (source == null)
                return false;

            bool found = false;
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(false)) {
                if (!renderer.enabled)
                    continue;
                if (!found) {
                    bounds = renderer.bounds;
                    found = true;
                } else {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }

        /// <summary>
        /// Wrecks a copy of <paramref name="source"/> and hides the original. Does nothing when the
        /// source is null or has no renderers to throw.
        /// </summary>
        /// <param name="blastCentre">Where the blast is, normally the middle of the model's bounds.</param>
        public static void Launch(
            Transform source,
            Vector3 blastCentre,
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

            List<Renderer> pieces = CollectPieces(debris);
            if (pieces.Count <= 1) {
                Collapse.Attach(debris, burnSeconds);
                return;
            }

            // The root is given its own burn-away first so it is cleaned up even when every visible
            // piece has already been detached from it.
            BurnAway.Attach(debris, burnSeconds);
            BurstPieces(debris.transform, pieces, blastCentre, upwardForce, outwardForce, spin, burnSeconds);
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

        /// <summary>Every renderer in the wreck with a transform of its own: the parts it can come apart into.</summary>
        static List<Renderer> CollectPieces(GameObject debris) {
            var pieces = new List<Renderer>();
            var seen = new HashSet<Transform>();
            foreach (Renderer renderer in debris.GetComponentsInChildren<Renderer>(true)) {
                if (renderer != null && seen.Add(renderer.transform))
                    pieces.Add(renderer);
            }
            return pieces;
        }

        /// <summary>
        /// Throws each part up and away from the blast centre. The sideways push grows with how far
        /// out the part sits, so a part at the middle goes straight up rather than being flung at
        /// full strength in an arbitrary direction.
        /// </summary>
        static void BurstPieces(
            Transform root,
            List<Renderer> pieces,
            Vector3 blastCentre,
            float upwardForce,
            float outwardForce,
            float spin,
            float burnSeconds) {
            // Measured before anything moves, so every part is judged against the intact model.
            var centres = new Vector3[pieces.Count];
            float reach = 0.01f;
            for (int i = 0; i < pieces.Count; i++) {
                centres[i] = pieces[i].bounds.center;
                Vector3 flat = centres[i] - blastCentre;
                flat.y = 0f;
                reach = Mathf.Max(reach, flat.magnitude);
            }

            for (int i = 0; i < pieces.Count; i++) {
                Transform piece = pieces[i].transform;
                if (piece != root)
                    piece.SetParent(null, true);

                var body = piece.gameObject.AddComponent<Rigidbody>();
                body.useGravity = true;
                // Debris is decoration; it must never push a unit or block a build slot.
                body.detectCollisions = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;

                Vector3 outward = centres[i] - blastCentre;
                outward.y = 0f;
                float distance = outward.magnitude;
                Vector3 direction = distance > 0.0001f ? outward / distance : Vector3.zero;
                float push = outwardForce * Mathf.Clamp01(distance / reach);

                body.AddForce(
                    Vector3.up * (upwardForce * Random.Range(0.7f, 1f)) + direction * push,
                    ForceMode.VelocityChange);
                body.AddTorque(Random.onUnitSphere * (spin * Random.Range(0.3f, 1f)), ForceMode.VelocityChange);

                if (piece != root)
                    BurnAway.Attach(piece.gameObject, burnSeconds);
            }
        }

        /// <summary>
        /// Takes a single-piece wreck down where it stood: it sinks by its own height and leans a
        /// little to one side, then burns away with the rest.
        /// </summary>
        sealed class Collapse : MonoBehaviour {
            float seconds = 3.5f;
            float elapsed;
            float drop;
            Vector3 startPosition;
            Vector3 startScale;
            Quaternion startRotation;
            Quaternion leanRotation;

            public static void Attach(GameObject target, float seconds) {
                var collapse = target.AddComponent<Collapse>();
                collapse.seconds = Mathf.Max(0.1f, seconds);
                collapse.startPosition = target.transform.position;
                collapse.startRotation = target.transform.rotation;
                collapse.startScale = target.transform.localScale;
                collapse.drop = TryGetVisualBounds(target.transform, out Bounds bounds) ? bounds.size.y : 1f;

                Vector2 axis = Random.insideUnitCircle.normalized;
                if (axis == Vector2.zero)
                    axis = Vector2.right;
                collapse.leanRotation = Quaternion.AngleAxis(CollapseLeanDegrees, new Vector3(axis.x, 0f, axis.y));
            }

            void Update() {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / seconds);
                // Slow to give way, then going fast: the way a burnt-out frame goes down.
                float fall = t * t;
                transform.SetPositionAndRotation(
                    startPosition + Vector3.down * (drop * fall),
                    Quaternion.Slerp(Quaternion.identity, leanRotation, Mathf.SmoothStep(0f, 1f, t)) * startRotation);
                // The last of it is consumed rather than left poking out of the ground.
                transform.localScale = startScale * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, (1f - t) * 4f));

                if (elapsed >= seconds)
                    Destroy(gameObject);
            }
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
