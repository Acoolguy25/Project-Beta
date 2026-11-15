using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Keeps a flat marker - the selection ring - on the terrain under its parent instead of at the
    /// parent's own height. A selected jet at cruise altitude shows its ring on the ground below it,
    /// where every other unit's ring is, rather than as a disc floating in the sky.
    /// <para>
    /// Only terrain counts, the same rule <see cref="WV_RangeIndicator"/> uses, so a unit passing
    /// over a building never lifts its ring onto the roof.
    /// </para>
    /// </summary>
    public sealed class WV_GroundDecal : MonoBehaviour {
        [Tooltip("Height above the ground the marker floats at, to avoid z-fighting with the terrain.")]
        [SerializeField, Min(0f)] float groundOffset = 0.08f;

        int groundMask;
        Quaternion flatRotation;

        void Awake() {
            groundMask = LayerMask.GetMask("Default", "Ground");
            // The marker is authored lying flat; keep that orientation however the parent banks.
            flatRotation = transform.rotation;
        }

        void LateUpdate() {
            Transform parent = transform.parent;
            if (parent == null)
                return;
            Vector3 origin = parent.position;
            Vector3 position = origin;
            if (Physics.Raycast(origin + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 1000f,
                    groundMask, QueryTriggerInteraction.Ignore))
                position.y = hit.point.y;
            position.y += groundOffset;
            transform.SetPositionAndRotation(position, Quaternion.Euler(flatRotation.eulerAngles.x, parent.eulerAngles.y, flatRotation.eulerAngles.z));
        }
    }
}
