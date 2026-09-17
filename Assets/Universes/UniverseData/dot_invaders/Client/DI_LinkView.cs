using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Client {
    public sealed class DI_LinkView : MonoBehaviour {
#if !UNITY_SERVER
        LineRenderer lineRenderer;
        MaterialPropertyBlock colorBlock;

        void SetColor(Color color) {
            colorBlock ??= new MaterialPropertyBlock();
            lineRenderer.GetPropertyBlock(colorBlock);
            colorBlock.SetColor("_BaseColor", color);
            colorBlock.SetColor("_Color", color);
            lineRenderer.SetPropertyBlock(colorBlock);
        }

        void Awake() {
            lineRenderer = GetComponent<LineRenderer>();
        }

        public void SetLine(Vector3 start, Vector3 end, Color color, float width) {
            lineRenderer ??= GetComponent<LineRenderer>();
            if (lineRenderer == null)
                return;

            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, start);
            lineRenderer.SetPosition(1, end);
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.startWidth = width;
            lineRenderer.endWidth = width;
            SetColor(color);
        }

        public void SetRoute(Vector2[] positions, int[] route, Color color, float width) {
            lineRenderer ??= GetComponent<LineRenderer>();
            if (lineRenderer == null || route == null || route.Length < 2)
                return;
            lineRenderer.positionCount = route.Length;
            for (int i = 0; i < route.Length; i++) {
                Vector2 point = positions[route[i]];
                lineRenderer.SetPosition(i, new Vector3(point.x, 0.22f, point.y));
            }
            lineRenderer.startColor = lineRenderer.endColor = color;
            lineRenderer.startWidth = lineRenderer.endWidth = width;
            SetColor(color);
        }
#endif
    }
}
