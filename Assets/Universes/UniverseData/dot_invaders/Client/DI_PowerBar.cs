#if !UNITY_SERVER
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>Snapshot-driven power shares; hover details use the same screen rectangles as the bar.</summary>
    public sealed class DI_PowerBar : MonoBehaviour {
        sealed class Segment {
            public RectTransform rect;
            public TextMeshProUGUI label;
            public string details;
        }

        readonly Dictionary<int, Segment> segments = new();
        readonly List<int> removed = new();
        RectTransform bar;
        RectTransform tooltip;
        TextMeshProUGUI tooltipText;

        void Awake() {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;
            var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            bar = CreateRect("PowerBar", transform);
            bar.anchorMin = new Vector2(0.22f, 1f);
            bar.anchorMax = new Vector2(0.78f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = new Vector2(0f, -64f);
            bar.sizeDelta = new Vector2(0f, 28f);
            var background = bar.gameObject.AddComponent<UnityEngine.UI.Image>();
            background.color = new Color(0.03f, 0.045f, 0.07f, 0.95f);
            background.raycastTarget = false;

            tooltip = CreateRect("PowerDetails", transform);
            tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 1f);
            tooltip.pivot = new Vector2(0.5f, 1f);
            tooltip.anchoredPosition = new Vector2(0f, -100f);
            tooltip.sizeDelta = new Vector2(460f, 120f);
            var panel = tooltip.gameObject.AddComponent<UnityEngine.UI.Image>();
            panel.color = new Color(0.025f, 0.035f, 0.055f, 0.97f);
            panel.raycastTarget = false;
            tooltipText = CreateLabel("Details", tooltip, 19f);
            tooltipText.margin = new Vector4(12f, 8f, 12f, 8f);
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
                    var rect = CreateRect($"Team {team}", bar);
                    var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
                    Color color = teamColor(team);
                    image.color = color;
                    // Hover is hit-tested directly, matching the board's Input System pointer.
                    image.raycastTarget = false;
                    segment = new Segment { rect = rect, label = CreateLabel("Percentage", rect, 18f) };
                    segment.label.color = color.grayscale > 0.55f ? new Color(0.02f, 0.03f, 0.05f) : Color.white;
                    segments.Add(team, segment);
                }
                float share = total > 0 ? (float)((double)power.Power / total) : 0f;
                segment.rect.anchorMin = new Vector2(start, 0f);
                segment.rect.anchorMax = new Vector2(start + share, 1f);
                segment.rect.offsetMin = new Vector2(1f, 1f);
                segment.rect.offsetMax = new Vector2(-1f, -1f);
                segment.label.text = $"{share * 100f:0}%";
                // Clipping prevents small shares from overwriting neighboring labels.
                segment.label.overflowMode = TextOverflowModes.Masking;
                int speedBases = 0;
                if (state.baseTeams != null && state.baseSpeedBases != null) {
                    int count = Mathf.Min(state.baseTeams.Length, state.baseSpeedBases.Length);
                    for (int i = 0; i < count; i++)
                        if (state.baseTeams[i] == team && state.baseSpeedBases[i]) speedBases++;
                }
                float teamSpeed = DI_Rules.TeamMoveSpeed(state.moveSpeed, speedBases, state.speedBaseBonus);
                segment.details = $"{teamName(team)}{(team == state.yourTeamId ? " (YOU)" : "")}\n" +
                    $"{power.troops:N0} troops  |  {power.bases} bases  |  {power.Power:N0} power\n" +
                    $"Move speed: {teamSpeed / Mathf.Max(0.01f, state.moveSpeed):0.##}x ({speedBases} speed bases)\n" +
                    $"Power = troops + {DI_Rules.BasePower} per base";
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
                    tooltipText.text = segment.details;
                    tooltip.gameObject.SetActive(true);
                    return;
                }
            }
            tooltip.gameObject.SetActive(false);
        }

        static RectTransform CreateRect(string name, Transform parent) {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static TextMeshProUGUI CreateLabel(string name, Transform parent, float size) {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }
    }
}
#endif
