using UnityEngine;

namespace RyanAssets.Characters.Shared {
    public class RobotColorController : MonoBehaviour {
        private static readonly int PrimaryColorId = Shader.PropertyToID("_RobotPrimaryColor");
        private static readonly int SecondaryColorId = Shader.PropertyToID("_RobotSecondaryColor");

        [SerializeField] private Renderer[] renderers;
        [SerializeField] private Color primaryColor = Color.white;
        [SerializeField] private Color secondaryColor = new Color(1f, 0.18f, 0.12f, 1f);

        private MaterialPropertyBlock _propertyBlock;

        public Color PrimaryColor => primaryColor;
        public Color SecondaryColor => secondaryColor;

        private void Reset() {
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void Awake() {
            EnsureRenderers();
            ApplyColors();
        }

        private void OnValidate() {
            EnsureRenderers();
            ApplyColors();
        }

        public void SetPrimaryColor(Color color) {
            primaryColor = color;
            ApplyColors();
        }

        public void SetSecondaryColor(Color color) {
            secondaryColor = color;
            ApplyColors();
        }

        public void SetColors(Color primary, Color secondary) {
            primaryColor = primary;
            secondaryColor = secondary;
            ApplyColors();
        }

        private void EnsureRenderers() {
            if (renderers != null && renderers.Length > 0) return;
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void ApplyColors() {
            if (renderers == null) return;

            _propertyBlock ??= new MaterialPropertyBlock();

            foreach (Renderer targetRenderer in renderers) {
                if (targetRenderer == null) continue;

                targetRenderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(PrimaryColorId, primaryColor);
                _propertyBlock.SetColor(SecondaryColorId, secondaryColor);
                targetRenderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
