using System;
using UnityEngine;
#if !UNITY_SERVER
using RyanAssets.GameFeel;
using TMPro;
#endif

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// One base on the board: team-coloured neon body and ring, troop count, role mark and label
    /// (speed, factory, fortress), super-base meter, turret, its range and its shots. Presentation
    /// only; fed by <see cref="DI_ClientController"/> from each server snapshot. The role marks, the
    /// range outline and the outlined label material are authored onto DI_Base.prefab by DI_Authoring.
    /// </summary>
    public sealed class DI_BaseView : MonoBehaviour {
#if !UNITY_SERVER
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>How a special base labels itself and colours its role mark.</summary>
        [Serializable]
        public sealed class RoleStyle {
            public string label = string.Empty;
            [Tooltip("Append the live rule multiplier to the label (FACTORY x2).")]
            public bool showMultiplier;
            public Color labelColor = Color.white;
            [Tooltip("Colour of the role mark around the base (gear teeth, fortress walls); it stays the same whoever holds the base.")]
            public Color markColor = Color.white;
            [Tooltip("Mark brightness swing, 0 = steady.")]
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
        [Tooltip("Ring brightness while selected or hovered: kept low so the white ring never blooms over the count.")]
        [SerializeField, Min(0f)] float selectedRingGlow = 1.2f;
        [SerializeField] Color selectedRingColor = Color.white;
        [Tooltip("Ring of the bases you can send to while dragging. Kept a cool white so it never reads as the yellow team.")]
        [SerializeField] Color neighborRingColor = new(0.8f, 0.88f, 1f);

        [Header("Roles")]
        [SerializeField] RoleStyle speedStyle = new() {
            label = ">> SPEED", labelColor = new Color(0.55f, 0.95f, 1f), markColor = new Color(0.6f, 0.8f, 1f),
            pulseAmount = 0f, pulseSpeed = 0f,
        };
        [SerializeField] RoleStyle factoryStyle = new() {
            label = "FACTORY", showMultiplier = true, labelColor = new Color(1f, 0.78f, 0.35f),
            markColor = new Color(1f, 0.62f, 0.18f), pulseAmount = 0.25f, pulseSpeed = 0.9f,
        };
        [SerializeField] RoleStyle fortressStyle = new() {
            label = "FORTRESS", showMultiplier = true, labelColor = new Color(0.8f, 0.88f, 1f),
            markColor = new Color(0.62f, 0.78f, 1f), pulseAmount = 0f, pulseSpeed = 0f,
        };

        [Header("Role marks")]
        [Tooltip("Gear teeth around a factory (children authored by DI_Authoring).")]
        [SerializeField] GameObject factoryMark;
        [Tooltip("Walls and towers around a fortress (children authored by DI_Authoring).")]
        [SerializeField] GameObject fortressMark;
        [Tooltip("Brightness multiplier of the role marks; above 1 blooms.")]
        [SerializeField, Min(0f)] float markGlow = 1.6f;
        [Tooltip("Degrees per second a held factory's gear turns; a neutral factory stands still.")]
        [SerializeField] float factoryGearSpin = 25f;

        [Header("Bounty")]
        [Tooltip("Gold diamonds circling a base with a bounty on it (authored by DI_Authoring).")]
        [SerializeField] GameObject bountyMark;
        [SerializeField] Color bountyColor = new(1f, 0.79f, 0.29f);
        [Tooltip("Brightness of the bounty mark; above 1 blooms.")]
        [SerializeField, Min(0f)] float bountyGlow = 2.4f;
        [Tooltip("Mark brightness swing, so the bounty catches the eye on a busy board.")]
        [SerializeField, Range(0f, 1f)] float bountyPulseAmount = 0.45f;
        [SerializeField, Min(0f)] float bountyPulseSpeed = 1.6f;
        [Tooltip("Degrees per second the diamonds circle the base.")]
        [SerializeField] float bountySpin = 40f;
        [Tooltip("Shown above the base in the role label's place (bounties only go on plain bases). {0} bonus troops, {1} time left.")]
        [SerializeField] string bountyLabelFormat = "BOUNTY +{0}  {1}";

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
        [Tooltip("Super meter colour while it is paused (the base is sending or under attack): a dim grey instead of a PAUSED word, which ran into neighbouring bases.")]
        [SerializeField] Color superPausedColor = new(0.55f, 0.58f, 0.64f);
        [Tooltip("Base units between the troop count and the role/meter labels above and below it; clears the role marks.")]
        [SerializeField, Min(0f)] float labelSpacing = 4.4f;

        [Header("Troop count")]
        [Tooltip("Font scale for a three-digit count, so 100+ fits inside the body with the margin two digits have.")]
        [SerializeField, Range(0.3f, 1f)] float threeDigitCountScale = 0.74f;
        [Tooltip("Font scale for four digits and more (troops can stack past the capacity).")]
        [SerializeField, Range(0.3f, 1f)] float fourDigitCountScale = 0.58f;

        [Header("Super base")]
        [Tooltip("Scale swing of a super base (0.1 = between 1.0x and 1.2x).")]
        [SerializeField, Range(0f, 0.5f)] float superPulseScale = 0.1f;
        [SerializeField, Min(0f)] float superPulseSpeed = 2f;
        [SerializeField, Range(0f, 1f)] float superGlowSwing = 0.3f;
        [SerializeField, Min(0f)] float superGlowSpeed = 0.5f;

        [Header("Arrivals and captures")]
        [Tooltip("The count punches this much harder than the body, so a landing reads on the number.")]
        [SerializeField, Min(0f)] float labelPunchFactor = 1.5f;
        [Tooltip("Extra ring brightness at the top of a punch, as a share of the punch amount.")]
        [SerializeField, Min(0f)] float punchGlowBoost = 0.6f;

        [Header("Turret")]
        [SerializeField, Min(0f)] float shotSeconds = 0.16f;
        [Tooltip("Size of the sparks where a turret shot lands.")]
        [SerializeField, Min(0f)] float shotSparkScale = 0.8f;
        [Tooltip("Thin outline at the edge of the turret's reach (authored by DI_Authoring).")]
        [SerializeField] LineRenderer rangeEdge;
        [Tooltip("Opacity of the range disc's fill; the outline carries the shape.")]
        [SerializeField, Range(0f, 1f)] float rangeFillAlpha = 0.012f;
        [Tooltip("Opacity of the range outline.")]
        [SerializeField, Range(0f, 1f)] float rangeEdgeAlpha = 0.5f;
        [Tooltip("Width of the range outline in board units.")]
        [SerializeField, Min(0.01f)] float rangeEdgeWidth = 0.45f;
        [Tooltip("Height of the range outline above the board, under the roads.")]
        [SerializeField] float rangeEdgeHeight = 0.05f;
        [Tooltip("Shot colour: how far the team colour is lightened toward white.")]
        [SerializeField, Range(0f, 1f)] float shotWhiten = 0.65f;
        [Tooltip("Turret icon colour: how far the team colour is lightened toward white.")]
        [SerializeField, Range(0f, 1f)] float turretIconWhiten = 0.35f;

        Renderer bodyRenderer;
        // The dark disc under the count; it swells with the body so the count never sits on the bright body.
        Transform core;
        Renderer glowRenderer;
        TextMeshPro troopLabel;
        float countFontSize;
        int shownDigits;
        TextMeshPro productionLabel;
        TextMeshPro roleLabel;
        Color teamColor;
        bool neutral = true;
        int shownTroops = int.MinValue;
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
        Renderer[] factoryMarkRenderers;
        Renderer[] fortressMarkRenderers;
        Transform cameraTransform;
        Vector3[] edgePoints;
        float edgeRadius = -1f;
        Vector3 edgeCentre;
        float roleMultiplier = -1f;
        DI_BaseRole shownRole = (DI_BaseRole)(-1);
        readonly System.Text.StringBuilder roleText = new(24);
        DI_BaseRole role;
        bool isSuperBase;
        const int MeterCharging = 0, MeterFull = -1, MeterPaused = -2, MeterIdle = -3;
        readonly System.Text.StringBuilder meterText = new(32);
        int shownMeter = -1;
        int shownStatus = int.MinValue;
        // A punch is one swell and settle of the body, the ring and the count (not the whole base,
        // so the turret range and labels keep their size). Driven here so it cannot be lost to another animator.
        Vector3 bodyScale, coreScale, glowScale, labelScale;
        float punchAmount, punchSeconds, punchStart;
        bool punching;
        float punchSwell;
        // Share of a punch's time at which sin(pi * sqrt(t)) peaks.
        const float PunchPeakTime = 0.25f;
        float superBasePulseTimer;
        Vector3 originalScale;
        Renderer[] bountyMarkRenderers;
        bool bounty;
        int bountyShownSeconds = -1;

        public float PickRadius => bodyRenderer != null
            ? Mathf.Max(bodyRenderer.bounds.extents.x, bodyRenderer.bounds.extents.z) : 3.7f;

        public int BaseId { get; private set; } = -1;

        void Awake() {
            bodyRenderer = transform.Find("Body")?.GetComponent<Renderer>();
            core = transform.Find("Core");
            glowRenderer = transform.Find("Glow")?.GetComponent<Renderer>();
            troopLabel = transform.Find("TroopLabel")?.GetComponent<TextMeshPro>();
            if (troopLabel != null)
                countFontSize = troopLabel.fontSize;
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
            factoryMarkRenderers = factoryMark != null ? factoryMark.GetComponentsInChildren<Renderer>(true) : null;
            fortressMarkRenderers = fortressMark != null ? fortressMark.GetComponentsInChildren<Renderer>(true) : null;
            bountyMarkRenderers = bountyMark != null ? bountyMark.GetComponentsInChildren<Renderer>(true) : null;
            if (bountyMark != null)
                bountyMark.SetActive(false);
            if (rangeEdge != null)
                rangeEdge.widthMultiplier = rangeEdgeWidth;
            originalScale = transform.localScale;
            if (bodyRenderer != null)
                bodyScale = bodyRenderer.transform.localScale;
            if (core != null)
                coreScale = core.localScale;
            if (glowRenderer != null)
                glowScale = glowRenderer.transform.localScale;
            if (troopLabel != null)
                labelScale = troopLabel.transform.localScale;
        }

        /// <summary>The camera labels face; the controller hands it over so bases never look it up per frame.</summary>
        public void SetCamera(Camera boardCamera) => cameraTransform = boardCamera != null ? boardCamera.transform : null;

        /// <param name="multiplier">The live rule multiplier of the base's role (factory or fortress), shown on its label.</param>
        public void SetState(int baseId, Vector3 position, int troops, bool isNeutral, Color color,
            DI_BaseRole baseRole, float multiplier) {
            BaseId = baseId;
            transform.position = position;
            teamColor = color;
            role = baseRole;
            neutral = isNeutral;
            SetRendererColor(bodyRenderer, color, color * (isNeutral ? neutralBodyGlow : bodyGlow));

            if (troopLabel != null && troops != shownTroops) {
                shownTroops = troops;
                troopLabel.SetText("{0}", troops);
                FitCount(troops);
            }

            RoleStyle style = Style(baseRole);
            // While a bounty is up the role label carries the bounty (see SetBounty).
            if (roleLabel != null && !bounty && (baseRole != shownRole || multiplier != roleMultiplier)) {
                shownRole = baseRole;
                roleMultiplier = multiplier;
                roleLabel.gameObject.SetActive(style != null);
                if (style != null) {
                    roleText.Clear().Append(style.label);
                    if (style.showMultiplier)
                        AppendMultiplier(roleText.Append(" x"), multiplier);
                    roleLabel.SetText(roleText);
                    roleLabel.color = style.labelColor;
                }
            }
            if (factoryMark != null)
                factoryMark.SetActive(baseRole == DI_BaseRole.Factory);
            if (fortressMark != null)
                fortressMark.SetActive(baseRole == DI_BaseRole.Fortress);

            RefreshGlow();
        }

        /// <summary>"2", "2.5": one decimal at most, without a string allocation.</summary>
        static void AppendMultiplier(System.Text.StringBuilder text, float value) {
            int tenths = Mathf.RoundToInt(value * 10f);
            text.Append(tenths / 10);
            if (tenths % 10 != 0)
                text.Append('.').Append(tenths % 10);
        }

        /// <summary>
        /// Marks the base while a bounty is up on it: circling gold diamonds and a countdown label above the count.
        /// Bounties only go on plain bases, so the label takes the role label's place.
        /// </summary>
        public void SetBounty(bool active, int bonusTroops, float secondsLeft) {
            if (!active && !bounty)
                return;
            if (bountyMark != null && bountyMark.activeSelf != active)
                bountyMark.SetActive(active);
            if (!active) {
                bounty = false;
                bountyShownSeconds = -1;
                // Let the next SetState put the role label back (or hide it on a plain base).
                shownRole = (DI_BaseRole)(-1);
                if (roleLabel != null && Style(role) == null)
                    roleLabel.gameObject.SetActive(false);
                return;
            }
            bounty = true;
            if (roleLabel == null)
                return;
            roleLabel.gameObject.SetActive(true);
            roleLabel.color = bountyColor;
            int whole = Mathf.CeilToInt(Mathf.Max(0f, secondsLeft));
            if (whole != bountyShownSeconds) {
                bountyShownSeconds = whole;
                roleLabel.SetText(string.Format(bountyLabelFormat, bonusTroops, DI_Rules.FormatClock(whole)));
            }
        }

        public void SetSelected(bool value) {
            selected = value;
            RefreshGlow();
        }

        /// <summary>Steps the count's font down for three and four digits; only touches TMP when the digit count changes.</summary>
        void FitCount(int troops) {
            int digits = troops >= 1000 ? 4 : troops >= 100 ? 3 : 2;
            if (digits == shownDigits)
                return;
            shownDigits = digits;
            troopLabel.fontSize = countFontSize * (digits >= 4 ? fourDigitCountScale : digits == 3 ? threeDigitCountScale : 1f);
        }

        /// <summary>
        /// A short swell of the body, core, ring and count (the count by <see cref="labelPunchFactor"/> more) for
        /// arrivals and captures. A new punch restarts from rest, so a stream never compounds.
        /// </summary>
        public void PlayPunch(float amount, float seconds) {
            punchAmount = amount;
            punchSeconds = Mathf.Max(0.01f, seconds);
            // Start at the peak, so the frame that shows the landing shows the full swell. Starting from rest
            // let a short punch begin and end between two frames whenever a frame took longer than the punch
            // (the Editor here renders at about 3 fps), and it was never seen.
            punchStart = Time.unscaledTime - punchSeconds * PunchPeakTime;
            punching = true;
        }

        void UpdatePunch() {
            if (!punching)
                return;
            float t = (Time.unscaledTime - punchStart) / punchSeconds;
            // One swell, peaking at PunchPeakTime and settling over the rest of the time.
            punchSwell = t >= 1f ? 0f : punchAmount * Mathf.Sin(Mathf.PI * Mathf.Sqrt(Mathf.Clamp01(t)));
            punching = t < 1f;
            // The disc parts widen on the board plane only: growing in height lifted the body's top past the core
            // and through the count.
            var widen = new Vector3(1f + punchSwell, 1f, 1f + punchSwell);
            if (bodyRenderer != null)
                bodyRenderer.transform.localScale = Vector3.Scale(bodyScale, widen);
            if (core != null)
                core.localScale = Vector3.Scale(coreScale, widen);
            if (glowRenderer != null)
                glowRenderer.transform.localScale = Vector3.Scale(glowScale, widen);
            if (troopLabel != null)
                troopLabel.transform.localScale = labelScale * (1f + punchSwell * labelPunchFactor);
            RefreshGlow();
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
            // The text only rebuilds when it would read differently.
            int status = !active ? MeterIdle : full ? MeterFull : rate > 0f ? MeterCharging : MeterPaused;
            if (filled != shownMeter || status != shownStatus) {
                shownMeter = filled;
                shownStatus = status;
                BuildSuperText(filled, status);
                productionLabel.SetText(meterText);
            }
            productionLabel.color = status == MeterPaused ? superPausedColor : Color.Lerp(superChargingColor, Color.white, charge);
        }

        void BuildSuperText(int filled, int status) {
            // Digits and '/' only: the board's label font has no bar or dash glyphs.
            meterText.Clear().Append("SUPER ").Append(filled).Append('/').Append(superMeterSegments);
            // One line only: a second line ran into neighbouring bases and the drag arrow.
            // A paused meter is shown by colour alone (see superPausedColor), keeping the label short.
            if (status == MeterFull)
                meterText.Append(" FULL");
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
                Color fill = teamColor;
                fill.a = rangeFillAlpha;
                SetRendererColor(rangeRenderer, fill);
            }
            if (rangeEdge != null) {
                rangeEdge.gameObject.SetActive(isTurret);
                if (isTurret) {
                    PlaceRangeEdge(turretRange);
                    Color edge = teamColor;
                    edge.a = rangeEdgeAlpha;
                    SetRendererColor(rangeEdge, edge);
                }
            }
            SetRendererColor(turretRenderer, teamColor * ringGlow);
            if (turretIconRenderers != null)
                foreach (Renderer icon in turretIconRenderers)
                    SetRendererColor(icon, Color.Lerp(teamColor, Color.white, turretIconWhiten) * ringGlow);
            // A newly joined client must not replay a historical shot.
            if (isTurret && lastShotSequence >= 0 && shotSequence > lastShotSequence)
                PlayShot(shotPosition);
            lastShotSequence = shotSequence;
        }

        /// <summary>
        /// The outline is drawn in world space, so its width is exact board units whatever the base's
        /// scale; its points move only when the base or the range changes (bases never move mid-match).
        /// </summary>
        void PlaceRangeEdge(float radius) {
            Vector3 centre = transform.position;
            if (radius == edgeRadius && centre == edgeCentre)
                return;
            edgeRadius = radius;
            edgeCentre = centre;
            int count = rangeEdge.positionCount;
            edgePoints ??= new Vector3[count];
            if (edgePoints.Length != count)
                edgePoints = new Vector3[count];
            for (int i = 0; i < count; i++) {
                float angle = Mathf.PI * 2f * i / count;
                edgePoints[i] = centre + new Vector3(Mathf.Cos(angle) * radius, rangeEdgeHeight, Mathf.Sin(angle) * radius);
            }
            rangeEdge.SetPositions(edgePoints);
        }

        void PlayShot(Vector3 shotPosition) {
            Vector3 start = turret != null ? turret.position : transform.position + Vector3.up * 2f;
            Vector3 direction = shotPosition - start;
            direction.y = 0f;
            if (turretAim != null && direction.sqrMagnitude > 0.001f)
                turretAim.rotation = Quaternion.LookRotation(direction);
            Color shotColor = Color.Lerp(teamColor, Color.white, shotWhiten);
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

        /// <summary>
        /// The ring shows the base's team (any team, not only yours), white while selected or hovered and
        /// gold as a neighbour of the order you drag; the role mark keeps the role's own colour.
        /// </summary>
        void RefreshGlow() {
            RoleStyle style = Style(role);
            float markPulse = style != null && style.pulseAmount > 0f
                ? 1f - style.pulseAmount + Mathf.PingPong(Time.unscaledTime * style.pulseSpeed, style.pulseAmount) : 1f;
            Renderer[] marks = role == DI_BaseRole.Factory ? factoryMarkRenderers
                : role == DI_BaseRole.Fortress ? fortressMarkRenderers : null;
            if (marks != null && style != null)
                foreach (Renderer mark in marks)
                    SetRendererColor(mark, style.markColor, style.markColor * (markPulse * markGlow));
            if (bounty && bountyMarkRenderers != null) {
                float pulse = 1f - bountyPulseAmount + Mathf.PingPong(Time.unscaledTime * bountyPulseSpeed, bountyPulseAmount);
                foreach (Renderer mark in bountyMarkRenderers)
                    SetRendererColor(mark, bountyColor, bountyColor * (pulse * bountyGlow));
            }

            if (glowRenderer == null)
                return;
            glowRenderer.gameObject.SetActive(!neutral || selected || neighbor || hovered || bounty);
            bool highlighted = selected || hovered;
            // A neutral bounty base wears a gold ring; a held one keeps its team's ring and the gold diamonds say the rest.
            Color ring = highlighted ? selectedRingColor : neighbor ? neighborRingColor
                : neutral && bounty ? bountyColor : teamColor;
            float intensity = isSuperBase
                ? 1f - superGlowSwing + Mathf.PingPong(Time.unscaledTime * superGlowSpeed, superGlowSwing) : 1f;
            intensity *= 1f + punchSwell * punchGlowBoost;
            SetRendererColor(glowRenderer, ring * (intensity * (highlighted ? selectedRingGlow : ringGlow)));
        }

        bool Pulsing => isSuperBase || bounty || Style(role) is { pulseAmount: > 0f };

        void LateUpdate() {
            if (cameraTransform != null) {
                Quaternion facing = cameraTransform.rotation;
                Vector3 up = cameraTransform.up * (labelSpacing * transform.lossyScale.x);
                if (troopLabel != null)
                    troopLabel.transform.rotation = facing;
                if (productionLabel != null && troopLabel != null) {
                    productionLabel.transform.rotation = facing;
                    productionLabel.transform.position = troopLabel.transform.position - up;
                }
                if (roleLabel != null && troopLabel != null) {
                    roleLabel.transform.rotation = facing;
                    roleLabel.transform.position = troopLabel.transform.position + up;
                }
            }
            // Glow pulses run per frame (snapshots arrive only about ten times a second).
            if (punching)
                UpdatePunch();
            else if (Pulsing)
                RefreshGlow();
            if (bounty && bountyMark != null)
                bountyMark.transform.Rotate(0f, bountySpin * Time.unscaledDeltaTime, 0f, Space.Self);
            if (factoryMark != null && role == DI_BaseRole.Factory && !neutral)
                factoryMark.transform.Rotate(0f, factoryGearSpin * Time.unscaledDeltaTime, 0f, Space.Self);
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
