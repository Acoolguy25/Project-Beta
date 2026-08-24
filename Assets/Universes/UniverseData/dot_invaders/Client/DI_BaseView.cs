using UnityEngine;
#if !UNITY_SERVER
using TMPro;
#endif

namespace Universes.UniverseData.dot_invaders {
    public sealed class DI_BaseView : MonoBehaviour {
#if !UNITY_SERVER
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Renderer bodyRenderer;
        Renderer glowRenderer;
        TextMeshPro troopLabel;
        Color teamColor;
        bool owned;
        bool selected;

        public int BaseId { get; private set; } = -1;

        void Awake() {
            bodyRenderer = transform.Find("Body")?.GetComponent<Renderer>();
            glowRenderer = transform.Find("Glow")?.GetComponent<Renderer>();
            troopLabel = transform.Find("TroopLabel")?.GetComponent<TextMeshPro>();
        }

        public void SetState(int baseId, Vector3 position, int troops, int pendingTroops, bool isOwned, Color color) {
            BaseId = baseId;
            transform.position = position;
            owned = isOwned;
            teamColor = color;
            SetRendererColor(bodyRenderer, color);

            if (troopLabel != null) {
                troopLabel.text = pendingTroops > 0 ? $"{troops}\n<size=55%>-{pendingTroops}</size>" : troops.ToString();
                troopLabel.color = Color.white;
            }
            RefreshGlow();
        }

        public void SetSelected(bool value) {
            selected = value;
            RefreshGlow();
        }

        void RefreshGlow() {
            if (glowRenderer == null)
                return;

            glowRenderer.gameObject.SetActive(owned || selected);
            SetRendererColor(glowRenderer, selected ? Color.white : teamColor);
        }

        void LateUpdate() {
            if (troopLabel != null && Camera.main != null)
                troopLabel.transform.rotation = Camera.main.transform.rotation;
        }

        static void SetRendererColor(Renderer target, Color color) {
            if (target == null)
                return;

            var block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            target.SetPropertyBlock(block);
        }
#endif
    }
}
