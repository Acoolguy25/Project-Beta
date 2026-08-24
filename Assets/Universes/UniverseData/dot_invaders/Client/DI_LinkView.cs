using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    public sealed class DI_LinkView : MonoBehaviour {
#if !UNITY_SERVER
        LineRenderer lineRenderer;

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
        }
#endif
    }
}
