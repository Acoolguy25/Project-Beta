using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Lays out a turntable spawned at runtime: sizes its disc and stands bumper posts around it. The
    /// spin itself comes from the <c>NetworkPlatform</c> on the same object (configured by the game), and
    /// <c>GroundMotionTransfer</c> carries riders round; put a <see cref="KnockbackSurface"/> on the post
    /// prefab so the posts shove players they sweep into.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Turntable : MonoBehaviour {
        [Tooltip("The disc: a unit-diameter, unit-thick cylinder child, scaled to the table's size.")]
        public Transform disc;
        [Tooltip("Bumper post placed around the disc.")]
        public GameObject postPrefab;
        [Tooltip("Post distance from the centre, as a fraction of the radius.")]
        [Range(0f, 1f)] public float postRadius = 0.62f;
        [Tooltip("Post height (m).")]
        [Min(0.1f)] public float postHeight = 3f;

        /// <summary>Sizes the disc to <paramref name="diameter"/> by <paramref name="thickness"/> (its top at this object's origin) and adds <paramref name="posts"/> evenly spaced posts.</summary>
        public void Build(float diameter, float thickness, int posts) {
            if (disc != null) {
                disc.localScale = new Vector3(diameter, thickness, diameter);
                disc.localPosition = Vector3.down * (thickness * 0.5f);
            }
            if (postPrefab == null) return;
            float r = diameter * 0.5f * postRadius;
            for (int i = 0; i < posts; i++) {
                float angle = i * Mathf.PI * 2f / posts;
                var post = Instantiate(postPrefab, transform);
                post.transform.localPosition = new Vector3(Mathf.Cos(angle) * r, postHeight * 0.5f, Mathf.Sin(angle) * r);
                post.transform.localRotation = Quaternion.identity;
                Vector3 scale = post.transform.localScale;
                post.transform.localScale = new Vector3(scale.x, postHeight, scale.z);
            }
        }
    }
}
