using System;
using UnityEngine;
#if !UNITY_SERVER
using RyanAssets.GameFeel;
using TMPro;
#endif

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// One base on the board: team-coloured neon body and ring, troop count, role label (speed,
    /// factory, fortress), super-base meter, turret and its shots. Presentation only; fed by
    /// <see cref="DI_ClientController"/> from each server snapshot.
    /// </summary>
    public sealed class DI_BaseView : MonoBehaviour {
#if !UNITY_SERVER
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>How a special base labels itself and tints its ring.</summary>
        [Serializable]
        public sealed class RoleStyle {
            public string label = string.Empty;
            public Color labelColor = Color.white;
            [Tooltip("Ring colour of the base while neutral; a held base shows its team colour, the label keeps its role.")]
            public Color ringTint = Color.white;
            [Tooltip("Ring brightness swing, 0 = steady.")]
            [Range(0f, 1f)] public float pulseAmount = 0.2f;
            [Min(0f)] public float pulseSpeed = 0.4f;
        }

        [Header("Glow")]
        [Tooltip("Body emission as a multiple of the team colour. Needs _EMISSION on DI_Base.mat (DI_Authoring).")]
        [SerializeField, Min(0f)] float bodyGlow = 0.9f;
        [Tooltip("Body emission of a neutral base, so held bases stand out.")]
        [SerializeField, Min(0f)] float neutralBodyGlow = 0.15f;
        [Tooltip("Ring brightness multiplier; above 1 blooms.")]
        [SerializeField, Min(0f)] float ringGlow = 2.2f;
        [SerializeField] Color selectedRingColor = Color.white;
        [SerializeField] Color neighborRingColor = new(1f, 0.8f, 0.2f);

        [Header("Roles")]
        [SerializeField] RoleStyle speedStyle = new() {
            label = ">> SPEED", labelColor = new Color(0.55f, 0.95f, 1f), ringTint = new Color(0.6f, 0.8f, 1f),
            pulseAmount = 0.2f, pulseSpeed = 0.4f,
        };
        [SerializeField] RoleStyle factoryStyle = new() {
            label = "FACTORY x2", labelColor = new Color(1f, 0.78f, 0.35f), ringTint = new Color(1f, 0.7f, 0.3f),
            pulseAmount = 0.3f, pulseSpeed = 0.9f,
        };
        [SerializeField] RoleStyle fortressStyle = new() {
            label = "FORTRESS x2", labelColor = new Color(0.8f, 0.88f, 1f), ringTint = new Color(0.78f, 0.85f, 1f),
            pulseAmount = 0f, pulseSpeed = 0f,
        };

        [Header("Labels")]
        [Tooltip("Font size of the role label (SPEED, FACTORY, FORTRESS); readable at the board's zoom.")]
        [SerializeField, Min(1f)] float roleLabelFontSize = 14f;
        [Tooltip("Font size of the super base's charge meter.")]
        [SerializeField, Min(1f)] float superLabelFontSize = 11f;
        [Tooltip("Role label box size in board units (width, height).")]
        [SerializeField] Vector2 labelBoxSize = new(16f, 3f);
        [Tooltip("Super meter box size in board units; two lines (meter, rate) must fit.")]
        [SerializeField] Vector2 superLabelBoxSize = new(18f, 6f);
        [Tooltip("Steps the super meter counts in (shown as SUPER 3/5).")]
        [SerializeField, Range(1, 10)] int superMeterSegments = 5;
        [Tooltip("Meter colour while charging; it whitens as the charge fills.")]
        [SerializeField] Color superChargingColor = new(1f, 0.72f, 0.22f);
        [Tooltip("Board units between the troop count and the role/meter labels above and below it.")]
        [SerializeField, Min(0f)] float labelSpacing = 3.6f;

        [Header("Super base")]
        [Tooltip("Scale swing of a super base (0.1 = between 1.0x and 1.2x).")]
        [SerializeField, Range(0f, 0.5f)] float superPulseScale = 0.1f;
        [SerializeField, Min(0f)] float superPulseSpeed = 2f;
        [SerializeField, Range(0f, 1f)] float superGlowSwing = 0.3f;
        [SerializeField, Min(0f)] float superGlowSpeed = 0.5f;

        [Header("Turret")]
        [SerializeField, Min(0f)] float shotSeconds = 0.16f;
        [Tooltip("Size of the sparks where a turret shot lands.")]
        [SerializeField, Min(0f)] float shotSparkScale = 0.8f;

        Renderer bodyRenderer;
        Renderer glowRenderer;
        TextMeshPro troopLabel;
        TextMeshPro productionLabel;
        TextMeshPro roleLabel;
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
        DI_BaseRole role;
        bool isSuperBase;
        const int MeterFull = -1, MeterPaused = -2, MeterIdle = -3;
        readonly System.Text.StringBuilder meterText = new(32);
        int shownMeter = -1;
        int shownStatus = int.MinValue;
        float superBasePulseTimer;
        Vector3 originalScale;

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
            roleLabel = transform.Find("SpeedLabel")?.GetComponent<TextMeshPro>();
            if (roleLabel != null) {
                roleLabel.fontSize = roleLabelFontSize;
                roleLabel.rectTransform.sizeDelta = labelBoxSize;
            }
            originalScale = transform.localScale;
        }

        public void SetState(int baseId, Vector3 position, int troops, bool isOwned, bool isNeutral, Color color,
            DI_BaseRole baseRole) {
            BaseId = baseId;
            transform.position = position;
            owned = isOwned;
            teamColor = color;
            role = baseRole;
            SetRendererColor(bodyRenderer, color, color * (isNeutral ? neutralBodyGlow : bodyGlow));

            if (troopLabel != null) {
                troopLabel.text = troops.ToString();
                troopLabel.color = Color.white;
            }

            RoleStyle style = Style(baseRole);
            if (roleLabel != null) {
                roleLabel.gameObject.SetActive(style != null);
                if (style != null) {
                    roleLabel.text = style.label;
                    roleLabel.color = style.labelColor;
                }
            }

            RefreshGlow();
        }

        public void SetSelected(bool value) {
            selected = value;
            RefreshGlow();
        }

        /// <summary>A short scale punch of the base's body and count, for arrivals and captures.</summary>
        public void PlayPunch(float amount, float seconds) {
            if (bodyRenderer != null)
                Punch.Scale(bodyRenderer.transform, amount, seconds);
            if (troopLabel != null)
                Punch.Scale(troopLabel.transform, amount * 1.5f, seconds);
        }

        public void SetProduction(bool isSuperProducer, float charge, float rate, bool active, bool full) {
            isSuperBase = isSuperProducer;

            if (isSuperProducer && productionLabel == null && troopLabel != null) {
                productionLabel = Instantiate(troopLabel, transform);
                productionLabel.name = "SuperProductionIndicator";
                productionLabel.fontSize = superLabelFontSize;
                productionLabel.enableAutoSizing = false;
                productionLabel.textWrappingMode = TextWrappingModes.NoWrap;
                productionLabel.overflowMode = TextOverflowModes.Overflow;
                productionLabel.rectTransform.sizeDelta = superLabelBoxSize;
                productionLabel.alignment = TextAlignmentOptions.Center;
            }
            if (productionLabel == null) return;
            productionLabel.gameObject.SetActive(isSuperProducer);
            if (!isSuperProducer) return;
            int filled = active ? Mathf.RoundToInt(Mathf.Clamp01(charge) * superMeterSegments) : 0;
            // The rate in tenths of a troop per second, or a state: the text only rebuilds when it would read differently.
            int status = !active ? MeterIdle : full ? MeterFull : rate > 0f ? Mathf.RoundToInt(rate * 10f) : MeterPaused;
            if (filled != shownMeter || status != shownStatus) {
                shownMeter = filled;
                shownStatus = status;
                BuildSuperText(filled, status);
                productionLabel.SetText(meterText);
            }
            productionLabel.color = Color.Lerp(superChargingColor, Color.white, charge);
        }

        void BuildSuperText(int filled, int status) {
            // Digits and '/' only: the board's label font has no bar or dash glyphs.
            meterText.Clear().Append("SUPER ").Append(filled).Append('/').Append(superMeterSegments);
            if (status == MeterFull)
                meterText.Append("\nFULL");
            else if (status == MeterPaused)
                meterText.Append("\nPAUSED");
            else if (status >= 0)
                meterText.Append('\n').Append(status / 10).Append('.').Append(status % 10).Append("/s");
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
            SetRendererColor(turretRenderer, teamColor * ringGlow);
            if (turretIconRenderers != null)
                foreach (Renderer icon in turretIconRenderers)
                    SetRendererColor(icon, Color.Lerp(teamColor, Color.white, 0.35f) * ringGlow);
            // A newly joined client must not replay a historical shot.
            if (isTurret && lastShotSequence >= 0 && shotSequence > lastShotSequence)
                PlayShot(shotPosition);
            lastShotSequence = shotSequence;
        }

        void PlayShot(Vector3 shotPosition) {
            Vector3 start = turret != null ? turret.position : transform.position + Vector3.up * 2f;
            Vector3 direction = shotPosition - start;
            direction.y = 0f;
            if (turretAim != null && direction.sqrMagnitude > 0.001f)
                turretAim.rotation = Quaternion.LookRotation(direction);
            Color shotColor = Color.Lerp(teamColor, Color.white, 0.65f);
            if (shotLine != null) {
                shotLine.gameObject.SetActive(true);
                shotLine.SetPosition(0, start);
                shotLine.SetPosition(1, shotPosition);
                shotLine.startColor = shotLine.endColor = shotColor;
                SetRendererColor(shotLine, shotColor * ringGlow);
                shotUntil = Time.unscaledTime + shotSeconds;
            }
            GameFx.Play(FxKind.Sparks, shotPosition, Vector3.up, shotColor, shotSparkScale);
        }

        RoleStyle Style(DI_BaseRole baseRole) => baseRole switch {
            DI_BaseRole.Speed => speedStyle,
            DI_BaseRole.Factory => factoryStyle,
            DI_BaseRole.Fortress => fortressStyle,
            _ => null,
        };

        void RefreshGlow() {
            if (glowRenderer == null)
                return;

            glowRenderer.gameObject.SetActive(owned || selected || neighbor || hovered);

            RoleStyle style = Style(role);
            Color idle = style != null && !owned ? style.ringTint : teamColor;
            Color ring = selected || hovered ? selectedRingColor : neighbor ? neighborRingColor : idle;
            float intensity = 1f;
            if (isSuperBase)
                intensity = 1f - superGlowSwing + Mathf.PingPong(Time.unscaledTime * superGlowSpeed, superGlowSwing);
            else if (style != null && style.pulseAmount > 0f)
                intensity = 1f - style.pulseAmount + Mathf.PingPong(Time.unscaledTime * style.pulseSpeed, style.pulseAmount);
            SetRendererColor(glowRenderer, ring * (intensity * ringGlow));
        }

        void LateUpdate() {
            Camera camera = Camera.main;
            if (troopLabel != null && camera != null)
                troopLabel.transform.rotation = camera.transform.rotation;
            if (productionLabel != null && troopLabel != null && camera != null) {
                productionLabel.transform.rotation = camera.transform.rotation;
                productionLabel.transform.position = troopLabel.transform.position -
                    camera.transform.up * (labelSpacing * transform.lossyScale.x);
            }
            if (roleLabel != null && troopLabel != null && camera != null) {
                roleLabel.transform.rotation = camera.transform.rotation;
                roleLabel.transform.position = troopLabel.transform.position +
                    camera.transform.up * (labelSpacing * transform.lossyScale.x);
            }
            if (shotLine != null && Time.unscaledTime >= shotUntil)
                shotLine.gameObject.SetActive(false);

            // Super bases breathe between 1.0x and 1.0x + 2 * superPulseScale.
            if (isSuperBase) {
                superBasePulseTimer += Time.unscaledDeltaTime;
                float pulseFactor = Mathf.Sin(superBasePulseTimer * superPulseSpeed) * superPulseScale + 1f + superPulseScale;
                transform.localScale = originalScale * pulseFactor;
            } else {
                transform.localScale = originalScale;
            }
        }

        void SetRendererColor(Renderer target, Color color) => SetRendererColor(target, color, null);

        void SetRendererColor(Renderer target, Color color, Color? emission) {
            if (target == null)
                return;

            colorBlock ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(colorBlock);
            colorBlock.SetColor(BaseColorId, color);
            colorBlock.SetColor(ColorId, color);
            if (emission.HasValue)
                colorBlock.SetColor(EmissionColorId, emission.Value);
            target.SetPropertyBlock(colorBlock);
        }
#endif
    }
}
