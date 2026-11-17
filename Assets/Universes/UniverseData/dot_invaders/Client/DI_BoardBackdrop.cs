using UnityEngine;

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// Feeds the board surface's "Dot Invaders/Backdrop" shader (DI_Backdrop.mat, assigned by
    /// DI_Authoring): every held base tints the space around it in its team colour, and the tint
    /// crossfades when a base changes hands. Also lights the board's border in neon.
    /// </summary>
    public sealed class DI_BoardBackdrop : MonoBehaviour {
#if !UNITY_SERVER
        /// <summary>Must match DI_MAX_TERRITORY in DI_Backdrop.shader.</summary>
        public const int MaxBases = 64;

        static readonly int BasesId = Shader.PropertyToID("_DI_TerritoryBases");
        static readonly int ColorsId = Shader.PropertyToID("_DI_TerritoryColors");
        static readonly int CountId = Shader.PropertyToID("_DI_TerritoryCount");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Territory")]
        [Tooltip("Board units around a held base that its team's tint reaches.")]
        [SerializeField, Min(1f)] float territoryRadius = 18f;
        [Tooltip("Seconds for a base's tint to fade over to its new owner.")]
        [SerializeField, Min(0.01f)] float tintFadeSeconds = 0.6f;

        [Header("Border")]
        [Tooltip("The board's edge renderers (DI_Base.mat, emission enabled by DI_Authoring).")]
        [SerializeField] Renderer[] borders = System.Array.Empty<Renderer>();
        [SerializeField] Color borderColor = new(0.1f, 0.2f, 0.35f);
        [SerializeField, ColorUsage(false, true)] Color borderGlow = new(0.25f, 0.65f, 1.4f);

        readonly Vector4[] sites = new Vector4[MaxBases];
        readonly Vector4[] colors = new Vector4[MaxBases];
        readonly Color[] targetColors = new Color[MaxBases];
        readonly float[] targetWeights = new float[MaxBases];
        int count;
        // True while some base's tint is still fading; once all have arrived the arrays stop being pushed.
        bool fading;
        const float SettledWeight = 0.002f;

        void OnEnable() {
            PaintBorders();
            Push();
        }

        void OnDisable() {
            // Another board (or a menu) must not inherit this match's territory.
            Shader.SetGlobalFloat(CountId, 0f);
        }

        public void SetState(in DI_StateBroadcast state, System.Func<int, Color> teamColor) {
            int bases = state.basePositions != null && state.baseTeams != null
                ? Mathf.Min(MaxBases, Mathf.Min(state.basePositions.Length, state.baseTeams.Length)) : 0;
            bool newBoard = bases != count;
            bool changed = false;
            for (int i = 0; i < bases; i++) {
                Vector2 position = state.basePositions[i];
                newBoard |= sites[i].x != position.x || sites[i].y != position.y;
                int team = state.baseTeams[i];
                float weight = team >= 0 ? 1f : 0f;
                Color color = team >= 0 ? teamColor(team) : targetColors[i];
                changed |= weight != targetWeights[i] || color != targetColors[i];
                targetWeights[i] = weight;
                targetColors[i] = color;
                sites[i] = new Vector4(position.x, position.y, territoryRadius, sites[i].w);
            }
            count = bases;
            // A fresh board starts at its real state; only changes on a running board fade.
            if (newBoard)
                for (int i = 0; i < count; i++) {
                    sites[i].w = targetWeights[i];
                    colors[i] = targetColors[i];
                }
            if (newBoard || changed) {
                fading = true;
                Push();
            }
        }

        void Update() {
            if (count == 0 || !fading)
                return;
            float step = 1f - Mathf.Exp(-Time.unscaledDeltaTime / tintFadeSeconds);
            bool settled = true;
            for (int i = 0; i < count; i++) {
                sites[i].w = Mathf.Lerp(sites[i].w, targetWeights[i], step);
                // Keep the old colour while a neutralised base fades out.
                if (targetWeights[i] > 0f)
                    colors[i] = Vector4.Lerp(colors[i], targetColors[i], step);
                if (Mathf.Abs(sites[i].w - targetWeights[i]) > SettledWeight ||
                    targetWeights[i] > 0f && ((Vector4)targetColors[i] - colors[i]).sqrMagnitude > SettledWeight * SettledWeight) {
                    settled = false;
                } else {
                    sites[i].w = targetWeights[i];
                    if (targetWeights[i] > 0f)
                        colors[i] = targetColors[i];
                }
            }
            fading = !settled;
            Push();
        }

        void Push() {
            Shader.SetGlobalVectorArray(BasesId, sites);
            Shader.SetGlobalVectorArray(ColorsId, colors);
            Shader.SetGlobalFloat(CountId, count);
        }

        void PaintBorders() {
            var block = new MaterialPropertyBlock();
            foreach (Renderer border in borders) {
                if (border == null)
                    continue;
                border.GetPropertyBlock(block);
                block.SetColor(BaseColorId, borderColor);
                block.SetColor(EmissionColorId, borderGlow);
                border.SetPropertyBlock(block);
            }
        }
#endif
    }
}
