using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    public sealed class DI_DotView : MonoBehaviour {
#if !UNITY_SERVER
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        Renderer dotRenderer;
        Vector3 targetPosition;
        bool initialized;

        void Awake() {
            dotRenderer = GetComponentInChildren<Renderer>();
        }

        public void SetState(Vector3 position, Color color) {
            targetPosition = position;
            if (!initialized) {
                transform.position = position;
                initialized = true;
            }

            if (dotRenderer != null) {
                var block = new MaterialPropertyBlock();
                dotRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                dotRenderer.SetPropertyBlock(block);
            }
        }

        void Update() {
            transform.position = Vector3.Lerp(transform.position, targetPosition, 1f - Mathf.Exp(-18f * Time.deltaTime));
        }
#endif
    }
}
