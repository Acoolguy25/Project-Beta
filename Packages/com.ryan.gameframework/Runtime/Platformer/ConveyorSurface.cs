using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A belt that pushes whoever stands on it along <see cref="localDirection"/>. Stepping off keeps
    /// the push as fading momentum. The belt texture scrolls to match on clients.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ConveyorSurface : MonoBehaviour, ISurfaceVelocity {
        public Vector3 localDirection = Vector3.forward;
        [Min(0f)] public float speed = 4f;
        [Tooltip("Renderer whose base map scrolls with the belt. Optional.")]
        public Renderer beltRenderer;
        [Tooltip("Texture repeats per metre along the belt, used to match the scroll speed.")]
        [Min(0.01f)] public float textureTilesPerMetre = 0.5f;

        public void Configure(Vector3 direction, float beltSpeed) {
            localDirection = direction;
            speed = Mathf.Max(0f, beltSpeed);
        }

        public Vector3 GetSurfaceVelocity(Vector3 characterPosition) =>
            transform.TransformDirection(localDirection).normalized * speed;

#if !UNITY_SERVER
        Material beltMaterial;
        void Start() {
            if (beltRenderer == null) return;
            beltMaterial = beltRenderer.material;
            // Keep arrows a constant size on stretched belts.
            Vector3 scale = beltRenderer.transform.lossyScale;
            beltMaterial.mainTextureScale = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) * textureTilesPerMetre;
        }
        void Update() {
            if (beltMaterial == null) return;
            // The belt mesh's V axis runs along the conveyor's local Z.
            Vector3 dir = localDirection.normalized;
            Vector2 offset = beltMaterial.mainTextureOffset;
            float delta = speed * textureTilesPerMetre * Time.deltaTime;
            offset += new Vector2(-dir.x * delta, -dir.z * delta);
            offset.x %= 1f; offset.y %= 1f;
            beltMaterial.mainTextureOffset = offset;
        }
        void OnDestroy() {
            if (beltMaterial != null) Destroy(beltMaterial);
        }
#endif
    }
}
