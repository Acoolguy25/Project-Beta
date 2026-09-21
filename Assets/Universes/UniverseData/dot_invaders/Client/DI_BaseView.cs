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
        TextMeshPro productionLabel;
        TextMeshPro speedLabel;
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

        // Super base visual enhancement
        private float superBasePulseTimer;
        private bool isSuperBase;
        private Vector3 originalScale;

        // Speed base visual enhancement
        private bool isSpeedBase;

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
            speedLabel = transform.Find("SpeedLabel")?.GetComponent<TextMeshPro>();

            // Store original scale for super base pulsing
            originalScale = transform.localScale;
        }

        public void SetState(int baseId, Vector3 position, int troops, int pendingTroops, bool isOwned, Color color, bool isSpeedBase) {
            BaseId = baseId;
            transform.position = position;
            owned = isOwned;
            teamColor = color;
            this.isSpeedBase = isSpeedBase;
            SetRendererColor(bodyRenderer, color);

            if (troopLabel != null) {
                troopLabel.text = troops.ToString();
                troopLabel.color = Color.white;
            }

            if (speedLabel != null) {
                speedLabel.gameObject.SetActive(isSpeedBase);
                if (isSpeedBase) {
                    speedLabel.text = ">> SPEED";
                    speedLabel.color = new Color(0.55f, 0.95f, 1f);
                }
            }

            RefreshGlow();
        }

        public void SetSelected(bool value) {
            selected = value;
            RefreshGlow();
        }

        public void SetProduction(bool isSuperProducer, float charge, float rate, bool active, bool full) {
            // Track if this is a super base for visual enhancement
            isSuperBase = isSuperProducer;

            if (isSuperProducer && productionLabel == null && troopLabel != null) {
                productionLabel = Instantiate(troopLabel, transform);
                productionLabel.name = "SuperProductionIndicator";
                productionLabel.transform.localPosition = troopLabel.transform.localPosition + new Vector3(0f, 0f, -4f);
                productionLabel.fontSize = troopLabel.fontSize * 0.3f;
                productionLabel.enableAutoSizing = false;
                productionLabel.textWrappingMode = TextWrappingModes.NoWrap;
                productionLabel.rectTransform.sizeDelta = new Vector2(9f, 2.2f);
                productionLabel.alignment = TextAlignmentOptions.Center;
            }
            if (productionLabel == null) return;
            productionLabel.gameObject.SetActive(isSuperProducer);
            if (!isSuperProducer) return;
            int filled = Mathf.RoundToInt(Mathf.Clamp01(charge) * 5f);
            string meter = new string('|', filled) + new string('.', 5 - filled);
            string status = full ? "FULL" : rate > 0f ? $"{rate:0.0}/s" : "PAUSED";
            productionLabel.text = active ? $"SUPER [{meter}]\n{status}" : "SUPER [.....]";
            productionLabel.color = Color.Lerp(new Color(1f, 0.72f, 0.22f), Color.white, charge);
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

            // Enhanced glow for super bases
            if (isSuperBase) {
                // Super bases have a more intense, pulsating glow
                float glowIntensity = Mathf.PingPong(Time.unscaledTime * 0.5f, 0.3f) + 0.7f;
                if (selected || hovered) {
                    SetRendererColor(glowRenderer, Color.white * glowIntensity);
                } else if (neighbor) {
                    SetRendererColor(glowRenderer, new Color(1f, 0.8f, 0.2f) * glowIntensity);
                } else {
                    SetRendererColor(glowRenderer, teamColor * glowIntensity);
                }
            }
            // Enhanced glow for speed bases
            else if (isSpeedBase) {
                // Speed bases have a distinct cyan-tinted glow
                float glowIntensity = Mathf.PingPong(Time.unscaledTime * 0.4f, 0.2f) + 0.6f;
                if (selected || hovered) {
                    SetRendererColor(glowRenderer, Color.white * glowIntensity);
                } else if (neighbor) {
                    SetRendererColor(glowRenderer, new Color(1f, 0.8f, 0.2f) * glowIntensity);
                } else {
                    SetRendererColor(glowRenderer, new Color(0.6f, 0.8f, 1f) * glowIntensity);
                }
            } else {
                SetRendererColor(glowRenderer, selected || hovered ? Color.white :
                    neighbor ? new Color(1f, 0.8f, 0.2f) : teamColor);
            }
        }

        void LateUpdate() {
            Camera camera = Camera.main;
            if (troopLabel != null && camera != null)
                troopLabel.transform.rotation = camera.transform.rotation;
            if (productionLabel != null && troopLabel != null && camera != null) {
                productionLabel.transform.rotation = camera.transform.rotation;
                productionLabel.transform.position = troopLabel.transform.position -
                    camera.transform.up * (3.4f * transform.lossyScale.x);
            }
            if (speedLabel != null && troopLabel != null && camera != null) {
                speedLabel.transform.rotation = camera.transform.rotation;
                speedLabel.transform.position = troopLabel.transform.position +
                    camera.transform.up * (3.4f * transform.lossyScale.x);
            }
            if (shotLine != null && Time.unscaledTime >= shotUntil)
                shotLine.gameObject.SetActive(false);

            // Super base visual enhancement: pulsing and size increase
            if (isSuperBase) {
                // Pulsing effect (scale between 1.0 and 1.2)
                superBasePulseTimer += Time.unscaledDeltaTime;
                float pulseFactor = Mathf.Sin(superBasePulseTimer * 2f) * 0.1f + 1.1f;
                transform.localScale = originalScale * pulseFactor;
            } else {
                // Reset to original scale if not a super base
                transform.localScale = originalScale;
            }
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
