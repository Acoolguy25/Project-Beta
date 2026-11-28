using UnityEngine;
using UnityEngine.UI;
#if !UNITY_SERVER
using System;
#endif

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// A stacked area chart of every team's power share over a match (<see cref="DI_PowerHistory"/>): one band
    /// per team in its colour, the viewer's own team at the bottom and brighter, with thin dark seams between
    /// bands. A UI graphic in the timeline card (DI_Authoring); it only redraws when given new data.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DI_PowerGraph : MaskableGraphic {
#if !UNITY_SERVER
        [Tooltip("Brightness of other teams' bands; your own band is drawn at full colour.")]
        [SerializeField, Range(0f, 1f)] float otherTeamAlpha = 0.55f;
        [Tooltip("Dark line between bands, in pixels.")]
        [SerializeField, Min(0f)] float seamWidth = 1.5f;
        [SerializeField] Color seamColor = new(0.04f, 0.05f, 0.08f, 0.9f);
        [Tooltip("Height in pixels of the bright edge drawn on top of your own band.")]
        [SerializeField, Min(0f)] float ownEdgeWidth = 2.5f;

        DI_PowerHistory history;
        Func<int, Color> teamColor;
        int ownTeam = -1;
        // Band order: your team first (bottom), then the others in the order they were seen.
        int[] order = Array.Empty<int>();

        public void SetData(DI_PowerHistory data, int yourTeam, Func<int, Color> colorOf) {
            history = data;
            ownTeam = yourTeam;
            teamColor = colorOf;
            int count = data != null ? data.Teams.Count : 0;
            order = new int[count];
            int next = 0, own = -1;
            for (int i = 0; i < count; i++)
                if (data.Teams[i] == yourTeam)
                    own = i;
            if (own >= 0)
                order[next++] = own;
            for (int i = 0; i < count; i++)
                if (i != own)
                    order[next++] = i;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh) {
            mesh.Clear();
            if (history == null || history.Count < 2 || teamColor == null || history.Duration <= 0f)
                return;
            Rect rect = GetPixelAdjustedRect();
            int samples = history.Count;
            var lower = new float[samples];
            foreach (int teamIndex in order) {
                int team = history.Teams[teamIndex];
                bool own = team == ownTeam;
                Color fill = teamColor(team);
                fill.a = own ? 1f : otherTeamAlpha;
                for (int s = 0; s + 1 < samples; s++) {
                    float x0 = X(rect, s), x1 = X(rect, s + 1);
                    float b0 = lower[s], b1 = lower[s + 1];
                    float t0 = b0 + history.ShareAt(s, teamIndex), t1 = b1 + history.ShareAt(s + 1, teamIndex);
                    Quad(mesh, x0, Y(rect, b0), x1, Y(rect, b1), Y(rect, t1), Y(rect, t0), fill);
                    float edge = own ? ownEdgeWidth : seamWidth;
                    Color edgeColor = own ? Color.Lerp(fill, Color.white, 0.6f) : seamColor;
                    if (edge > 0f && t0 < 0.999f)
                        Quad(mesh, x0, Y(rect, t0) - edge * 0.5f, x1, Y(rect, t1) - edge * 0.5f,
                            Y(rect, t1) + edge * 0.5f, Y(rect, t0) + edge * 0.5f, edgeColor);
                }
                for (int s = 0; s < samples; s++)
                    lower[s] += history.ShareAt(s, teamIndex);
            }
        }

        float X(Rect rect, int sample) => rect.xMin + rect.width * Mathf.Clamp01(history.TimeAt(sample) / history.Duration);

        static float Y(Rect rect, float share) => rect.yMin + rect.height * Mathf.Clamp01(share);

        /// <summary>A four-sided strip between two x positions: bottom edge (y0, y1), top edge (y2 at x1, y3 at x0).</summary>
        static void Quad(VertexHelper mesh, float x0, float bottom0, float x1, float bottom1, float top1, float top0, Color color) {
            int start = mesh.currentVertCount;
            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = new Vector3(x0, bottom0);
            mesh.AddVert(vertex);
            vertex.position = new Vector3(x1, bottom1);
            mesh.AddVert(vertex);
            vertex.position = new Vector3(x1, top1);
            mesh.AddVert(vertex);
            vertex.position = new Vector3(x0, top0);
            mesh.AddVert(vertex);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
#endif
    }
}
