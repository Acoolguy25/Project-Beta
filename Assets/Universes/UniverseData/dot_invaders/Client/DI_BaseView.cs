using UnityEngine;
#if !UNITY_SERVER
using TMPro;
#endif

namespace Universes.UniverseData.dot_invaders.Client {
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
        bool neighbor;
        bool hovered;
        Transform turret;
        Transform turretAim;
        Renderer turretRenderer;
        Renderer[] turretIconRenderers;
        Renderer rangeRenderer;
        Transform capacityIcon;
        LineRenderer shotLine;
        float shotUntil;
        int lastShotSequence = -1;
        MaterialPropertyBlock colorBlock;

        public float PickRadius => bodyRenderer != null
            ? Mathf.Max(bodyRenderer.bounds.extents.x, bodyRenderer.bounds.extents.z) : 3.7f;

        public int BaseId { get; private set; } = -1;

        void Awake() {
            bodyRenderer = transform.Find("Body")?.GetComponent<Renderer>();
            glowRenderer = transform.Find("Glow")?.GetComponent<Renderer>();
            troopLabel = transform.Find("TroopLabel")?.GetComponent<TextMeshPro>();
            turret = transform.Find("Turret");
            turretAim = turret != null ? turret.Find("Aim") : null;
            turretRenderer = turret != null ? turret.GetComponent<Renderer>() : null;
            turretIconRenderers = turret != null ? turret.GetComponentsInChildren<Renderer>(true) : null;
            rangeRenderer = transform.Find("TurretRange")?.GetComponent<Renderer>();
            capacityIcon = transform.Find("CapacityIcon");
            shotLine = transform.Find("TurretShot")?.GetComponent<LineRenderer>();
        }

        public void SetState(int baseId, Vector3 position, int troops, int pendingTroops, bool isOwned, Color color) {
            BaseId = baseId;
            transform.position = position;
            owned = isOwned;
            teamColor = color;
            SetRendererColor(bodyRenderer, color);

            if (troopLabel != null) {
                troopLabel.text = troops.ToString();
                troopLabel.color = Color.white;
            }
            RefreshGlow();
        }

        public void SetSelected(bool value) {
            selected = value;
            RefreshGlow();
        }

        public void SetInteraction(bool isSelected, bool isNeighbor, bool isHovered) {
            if (selected == isSelected && neighbor == isNeighbor && hovered == isHovered)
                return;
            selected = isSelected;
            neighbor = isNeighbor;
            hovered = isHovered;
            RefreshGlow();
        }

        public void SetDefense(bool isTurret, int troops, int shotSequence, Vector3 shotPosition,
            float turretRange, int maxCapacity) {
            if (capacityIcon != null)
                capacityIcon.gameObject.SetActive(!isTurret && troops >= maxCapacity);
            if (turret != null)
                turret.gameObject.SetActive(isTurret);
            if (rangeRenderer != null) {
                rangeRenderer.gameObject.SetActive(isTurret);
                // The range is measured in board units, independent of base art scale.
                rangeRenderer.transform.localScale = new Vector3(
                    turretRange * 2f / transform.lossyScale.x, 0.01f,
                    turretRange * 2f / transform.lossyScale.z);
                Color tint = teamColor;
                tint.a = 0.035f;
                SetRendererColor(rangeRenderer, tint);
            }
            SetRendererColor(turretRenderer, teamColor);
            if (turretIconRenderers != null)
                foreach (Renderer icon in turretIconRenderers)
                    SetRendererColor(icon, Color.Lerp(teamColor, Color.white, 0.35f));
            // A newly joined client must not replay a historical shot.
            if (isTurret && lastShotSequence >= 0 && shotSequence > lastShotSequence) {
                Vector3 start = turret != null ? turret.position : transform.position + Vector3.up * 2f;
                Vector3 direction = shotPosition - start;
                direction.y = 0f;
                if (turretAim != null && direction.sqrMagnitude > 0.001f)
                    turretAim.rotation = Quaternion.LookRotation(direction);
                if (shotLine != null) {
                    shotLine.gameObject.SetActive(true);
                    shotLine.SetPosition(0, start);
                    shotLine.SetPosition(1, shotPosition);
                    shotLine.startColor = shotLine.endColor = Color.Lerp(teamColor, Color.white, 0.65f);
                    SetRendererColor(shotLine, shotLine.startColor);
                    shotUntil = Time.unscaledTime + 0.16f;
                }
            }
            lastShotSequence = shotSequence;
        }

        void RefreshGlow() {
            if (glowRenderer == null)
                return;

            glowRenderer.gameObject.SetActive(owned || selected || neighbor || hovered);
            SetRendererColor(glowRenderer, selected || hovered ? Color.white :
                neighbor ? new Color(1f, 0.8f, 0.2f) : teamColor);
        }

        void LateUpdate() {
            if (troopLabel != null && Camera.main != null)
                troopLabel.transform.rotation = Camera.main.transform.rotation;
            if (shotLine != null && Time.unscaledTime >= shotUntil)
                shotLine.gameObject.SetActive(false);
        }

        void SetRendererColor(Renderer target, Color color) {
            if (target == null)
                return;

            colorBlock ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(colorBlock);
            colorBlock.SetColor(BaseColorId, color);
            colorBlock.SetColor(ColorId, color);
            target.SetPropertyBlock(colorBlock);
        }
#endif
    }
}
