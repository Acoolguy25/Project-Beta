using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// One line on the board: a road between two bases, a route preview, the order arrow or the
    /// selection marquee. Colours are pushed above 1 (HDR) so the board's bloom makes them glow, and
    /// with the "Dot Invaders/Neon Line" material (DI_Road.mat, assigned by DI_Authoring) a line can
    /// carry light pulses that travel along it and chevrons that point the way.
    /// </summary>
    public sealed class DI_LinkView : MonoBehaviour {
#if !UNITY_SERVER
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int PulseColorId = Shader.PropertyToID("_PulseColor");
        static readonly int PulseStrengthId = Shader.PropertyToID("_PulseStrength");
        static readonly int PulseDirectionId = Shader.PropertyToID("_PulseDirection");
        static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        static readonly int PulseSpacingId = Shader.PropertyToID("_PulseSpacing");
        static readonly int ChevronsId = Shader.PropertyToID("_Chevrons");
        static readonly int LineLengthId = Shader.PropertyToID("_LineLength");
        static readonly int PulseLengthId = Shader.PropertyToID("_PulseLength");
        static readonly int PulseThinId = Shader.PropertyToID("_PulseThin");

        [Tooltip("Brightness multiplier on the line colour; above 1 makes bright lines bloom.")]
        [SerializeField, Min(0f)] float glow = 1.5f;
        [Header("Route streaks (defaults; the order arrow sets its own with SetPulseShape)")]
        [Tooltip("Brightness multiplier on travelling pulses. Kept low: a streak must stay dimmer than a troop.")]
        [SerializeField, Min(0f)] float pulseGlow = 1.1f;
        [Tooltip("Board units per second a pulse travels; roughly troop speed reads best.")]
        [SerializeField, Min(0f)] float pulseSpeed = 14f;
        [Tooltip("Board units between consecutive pulses: far wider than the gap between troops in a wave.")]
        [SerializeField, Min(0.5f)] float pulseSpacing = 26f;
        [Tooltip("Share of the spacing a pulse's fading tail covers: a long streak, not a bead.")]
        [SerializeField, Range(0.02f, 1f)] float pulseLength = 0.55f;
        [Tooltip("0 = pulse fills the line's width, 1 = a thin streak along its centre.")]
        [SerializeField, Range(0f, 1f)] float pulseThin = 0.4f;

        LineRenderer lineRenderer;
        MaterialPropertyBlock block;
        Color color = Color.white;
        Color pulseColor = Color.white;
        float pulseStrength;
        float pulseDirection = 1f;
        float chevrons;
        float length;

        void Awake() {
            lineRenderer = GetComponent<LineRenderer>();
        }

        public void SetLine(Vector3 start, Vector3 end, Color lineColor, float width) {
            if (!TryGetLine())
                return;

            lineRenderer.loop = false;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, start);
            lineRenderer.SetPosition(1, end);
            length = Vector3.Distance(start, end);
            Apply(lineColor, width);
        }

        /// <summary>Closed outline, used by the board's drag-selection marquee.</summary>
        public void SetLoop(Vector3[] corners, Color lineColor, float width) {
            if (!TryGetLine() || corners == null || corners.Length < 3)
                return;

            lineRenderer.loop = true;
            lineRenderer.positionCount = corners.Length;
            lineRenderer.SetPositions(corners);
            length = 0f;
            for (int i = 0; i < corners.Length; i++)
                length += Vector3.Distance(corners[i], corners[(i + 1) % corners.Length]);
            Apply(lineColor, width);
        }

        public void SetRoute(Vector2[] positions, int[] route, Color lineColor, float width, float height) {
            if (!TryGetLine() || route == null || route.Length < 2)
                return;
            lineRenderer.loop = false;
            lineRenderer.positionCount = route.Length;
            length = 0f;
            for (int i = 0; i < route.Length; i++) {
                Vector2 point = positions[route[i]];
                lineRenderer.SetPosition(i, new Vector3(point.x, height, point.y));
                if (i > 0)
                    length += Vector2.Distance(positions[route[i - 1]], point);
            }
            Apply(lineColor, width);
        }

        /// <summary>
        /// Light pulses travelling along the line (strength 0 turns them off). Direction +1 runs
        /// from the line's first point to its last, -1 the other way.
        /// </summary>
        public void SetPulse(Color colorOfPulse, float direction, float strength) {
            direction = direction < 0f ? -1f : 1f;
            strength = Mathf.Max(0f, strength);
            // Called for every road whenever the pointer moves; only touch the renderer on a change.
            if (pulseColor == colorOfPulse && pulseDirection == direction && pulseStrength == strength)
                return;
            pulseColor = colorOfPulse;
            pulseDirection = direction;
            pulseStrength = strength;
            if (TryGetLine())
                Push();
        }

        /// <summary>Overrides the pulse shape of this line (the order arrow uses short, full-width chevrons).</summary>
        public void SetPulseShape(float spacing, float lengthShare, float thin) {
            pulseSpacing = Mathf.Max(0.5f, spacing);
            pulseLength = Mathf.Clamp(lengthShare, 0.02f, 1f);
            pulseThin = Mathf.Clamp01(thin);
            if (TryGetLine())
                Push();
        }

        /// <summary>Bends pulses into chevrons that point along the pulse direction (0..1).</summary>
        public void SetChevrons(float amount) {
            amount = Mathf.Clamp01(amount);
            if (chevrons == amount)
                return;
            chevrons = amount;
            if (TryGetLine())
                Push();
        }

        bool TryGetLine() {
            if (lineRenderer == null)
                lineRenderer = GetComponent<LineRenderer>();
            return lineRenderer != null;
        }

        void Apply(Color lineColor, float width) {
            color = lineColor;
            lineRenderer.startColor = lineRenderer.endColor = lineColor;
            lineRenderer.startWidth = lineRenderer.endWidth = width;
            Push();
        }

        void Push() {
            block ??= new MaterialPropertyBlock();
            lineRenderer.GetPropertyBlock(block);
            Color hdr = color * glow;
            hdr.a = color.a;
            block.SetColor(BaseColorId, hdr);
            block.SetColor(ColorId, hdr);
            block.SetColor(PulseColorId, pulseColor * pulseGlow);
            block.SetFloat(PulseStrengthId, pulseStrength);
            block.SetFloat(PulseDirectionId, pulseDirection);
            block.SetFloat(PulseSpeedId, pulseSpeed);
            block.SetFloat(PulseSpacingId, pulseSpacing);
            block.SetFloat(PulseLengthId, pulseLength);
            block.SetFloat(PulseThinId, pulseThin);
            block.SetFloat(ChevronsId, chevrons);
            block.SetFloat(LineLengthId, Mathf.Max(0.01f, length));
            lineRenderer.SetPropertyBlock(block);
        }
#endif
    }
}
