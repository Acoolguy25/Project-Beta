#if !UNITY_SERVER
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// Snapshot-driven power shares: one rounded segment per team, sized by its share of all power,
    /// with a details card on hover. The bar, segment template and card are authored in the DI_HUD
    /// prefab (DI_Authoring); hover uses the same screen rectangles as the bar.
    /// </summary>
    public sealed class DI_PowerBar : MonoBehaviour {
        sealed class Segment {
            public RectTransform rect;
            public Image fill;
            public TextMeshProUGUI label;
            public Color color;
            public string title, details;
        }

        [Tooltip("Track the segments are laid out in.")]
        [SerializeField] RectTransform bar;
        [Tooltip("Inactive segment (Image with a TMP label child) copied once per team.")]
        [SerializeField] Image segmentTemplate;
        [Tooltip("Gap between neighbouring segments, in canvas units.")]
        [SerializeField, Min(0f)] float segmentGap = 3f;

        [Header("Details card")]
        [SerializeField] RectTransform tooltip;
        [SerializeField] Image tooltipSwatch;
        [SerializeField] TMP_Text tooltipTitle;
        [SerializeField] TMP_Text tooltipBody;

        readonly Dictionary<int, Segment> segments = new();
        readonly List<int> removed = new();

        void Awake() {
            segmentTemplate.gameObject.SetActive(false);
            tooltip.gameObject.SetActive(false);
            bar.gameObject.SetActive(false);
        }

        public void SetState(DI_StateBroadcast state, Func<int, Color> teamColor, Func<int, string> teamName) {
            var powers = DI_Rules.CalculatePower(state);
            long total = 0;
            foreach (DI_TeamPower power in powers.Values) total += power.Power;
            removed.Clear();
            foreach (int team in segments.Keys)
                if (!powers.ContainsKey(team)) removed.Add(team);
            foreach (int team in removed) {
                Destroy(segments[team].rect.gameObject);
                segments.Remove(team);
            }
            bar.gameObject.SetActive(total > 0);
            float start = 0f;
            foreach (var entry in powers) {
                int team = entry.Key;
                DI_TeamPower power = entry.Value;
                if (!segments.TryGetValue(team, out Segment segment)) {
                    Image fill = Instantiate(segmentTemplate, segmentTemplate.transform.parent);
                    fill.name = $"Team {team}";
                    fill.gameObject.SetActive(true);
                    Color color = teamColor(team);
                    fill.color = color;
                    segment = new Segment {
                        rect = fill.rectTransform, fill = fill, color = color,
                        label = fill.GetComponentInChildren<TextMeshProUGUI>(true),
                    };
                    segment.label.color = color.grayscale > 0.55f ? new Color(0.04f, 0.05f, 0.08f) : Color.white;
                    segments.Add(team, segment);
                }
                float share = total > 0 ? (float)((double)power.Power / total) : 0f;
                segment.rect.anchorMin = new Vector2(start, 0f);
                segment.rect.anchorMax = new Vector2(start + share, 1f);
                segment.rect.offsetMin = new Vector2(segmentGap * 0.5f, 0f);
                segment.rect.offsetMax = new Vector2(-segmentGap * 0.5f, 0f);
                // Only label shares wide enough to read; the hover card covers the rest.
                segment.label.text = share >= 0.06f ? $"{share * 100f:0}%" : string.Empty;
                // Other teams are dimmed slightly so your own segment stands out.
                segment.fill.color = team == state.yourTeamId ? segment.color : Color.Lerp(segment.color, new Color(0.1f, 0.12f, 0.16f), 0.18f);
                int speedBases = 0;
                if (state.baseTeams != null && state.baseSpeedBases != null) {
                    int count = Mathf.Min(state.baseTeams.Length, state.baseSpeedBases.Length);
                    for (int i = 0; i < count; i++)
                        if (state.baseTeams[i] == team && state.baseSpeedBases[i]) speedBases++;
                }
                float teamSpeed = DI_Rules.TeamMoveSpeed(state.moveSpeed, speedBases, state.speedBaseBonus);
                segment.title = $"{teamName(team)}{(team == state.yourTeamId ? "  <color=#9AA3AF>(you)</color>" : "")}";
                segment.details =
                    $"<color=#9AA3AF>Troops</color><pos=46%>{power.troops:N0}\n" +
                    $"<color=#9AA3AF>Bases</color><pos=46%>{power.bases}\n" +
                    $"<color=#9AA3AF>Power</color><pos=46%>{power.Power:N0}  <color=#7D8796>({share * 100f:0.#}%)</color>\n" +
                    $"<color=#9AA3AF>Move speed</color><pos=46%>{teamSpeed / Mathf.Max(0.01f, state.moveSpeed):0.##}x  <color=#7D8796>({speedBases} speed)</color>\n" +
                    $"<size=85%><color=#7D8796>Power = troops + {DI_Rules.BasePower} per base</color></size>";
                start += share;
            }
            UpdateHover();
        }

        public bool ContainsPointer(Vector2 position) => bar != null && bar.gameObject.activeInHierarchy &&
            (RectTransformUtility.RectangleContainsScreenPoint(bar, position) ||
             tooltip.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(tooltip, position));

        void Update() => UpdateHover();

        void UpdateHover() {
            if (tooltip == null) return;
            if (Mouse.current != null && Application.isFocused && bar.gameObject.activeInHierarchy) {
                Vector2 pointer = Mouse.current.position.ReadValue();
                foreach (Segment segment in segments.Values) {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(segment.rect, pointer)) continue;
                    tooltipSwatch.color = segment.color;
                    tooltipTitle.text = segment.title;
                    tooltipBody.text = segment.details;
                    tooltip.gameObject.SetActive(true);
                    return;
                }
            }
            tooltip.gameObject.SetActive(false);
        }
    }
}
#endif
