using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.classic_horror.Client;

namespace Universes.UniverseData.classic_horror.Editor {
    public static partial class CH_Authoring {
        // Where each part of the minigame card sits, as fractions of its height (bottom, top).
        static readonly Vector2 PuzzleTitleBand = new(0.86f, 0.98f), PuzzleHintBand = new(0.74f, 0.86f), PuzzleStageBand = new(0.42f, 0.72f),
            PuzzleHeatBand = new(0.355f, 0.395f), PuzzleReadoutBand = new(0.27f, 0.35f), PuzzlePipBand = new(0.17f, 0.25f), PuzzleStatusBand = new(0.03f, 0.16f);
        const int PuzzleTiles = 4, PuzzlePips = 5;
        static readonly Color PuzzleTrack = new(0.07f, 0.065f, 0.06f, 0.96f), PuzzleZone = new(0.5f, 0.85f, 0.5f, 0.75f), PuzzleNeedle = new(0.98f, 0.8f, 0.45f);

        /// <summary>
        /// The minigame card: low on the screen so the world stays visible above it (you can look up and see him coming).
        /// One layout serves all three games (a gauge, four keys, or a dial with a heat bar); <see cref="CH_PuzzlePanel"/>
        /// shows what each needs.
        /// </summary>
        static void BuildPuzzle(RectTransform gameplay, CH_ClientController client) {
            RectTransform card = HudKit.Card(gameplay, "Minigame", new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(820f, 360f), popup: false);
            HudKit.AccentStripe(card, Amber, 4f);
            var panel = card.gameObject.AddComponent<CH_PuzzlePanel>();
            panel.group = card.gameObject.AddComponent<CanvasGroup>();
            panel.group.blocksRaycasts = false;
            client.puzzle = panel;

            panel.title = PuzzleText(card, "Title", PuzzleTitleBand, UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            panel.hint = PuzzleText(card, "Hint", PuzzleHintBand, UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            panel.readout = PuzzleText(card, "Readout", PuzzleReadoutBand, UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            panel.readout.color = Amber;
            panel.status = PuzzleText(card, "Status", PuzzleStatusBand, UIText.Strong, UIColorRole.TextSecondary, TextAlignmentOptions.Center);

            // The gauge: the engine's needle over a green zone, and the lock's dial marker.
            RectTransform gauge = Rect("Gauge", card, new Vector2(0.06f, PuzzleStageBand.x), new Vector2(0.94f, PuzzleStageBand.y), Vector2.zero, Vector2.zero);
            panel.gaugeRoot = gauge.gameObject;
            panel.gauge = gauge;
            Panel("Track", gauge, Vector2.zero, Vector2.one, PuzzleTrack);
            panel.zoneImage = Panel("Zone", gauge, new Vector2(0.4f, 0f), new Vector2(0.6f, 1f), PuzzleZone);
            panel.zone = panel.zoneImage.rectTransform;
            for (int tick = 1; tick < 10; tick++)
                Panel($"Tick {tick}", gauge, new Vector2(tick / 10f, 0f), new Vector2(tick / 10f, 0.22f), new Color(1f, 1f, 1f, 0.14f)).rectTransform.sizeDelta = new Vector2(2f, 0f);
            panel.needleImage = Panel("Needle", gauge, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), PuzzleNeedle);
            panel.needle = panel.needleImage.rectTransform;
            panel.needle.sizeDelta = new Vector2(10f, 96f);

            // The lock's heat bar.
            RectTransform heat = Rect("Heat", card, new Vector2(0.06f, PuzzleHeatBand.x), new Vector2(0.94f, PuzzleHeatBand.y), Vector2.zero, Vector2.zero);
            panel.heatRoot = heat.gameObject;
            Panel("Track", heat, Vector2.zero, Vector2.one, PuzzleTrack);
            panel.heatImage = Panel("Fill", heat, Vector2.zero, new Vector2(0f, 1f), Amber);
            panel.heatFill = panel.heatImage.rectTransform;

            // The hymn's four keys.
            RectTransform tiles = Rect("Keys", card, new Vector2(0.1f, PuzzleStageBand.x), new Vector2(0.9f, PuzzleStageBand.y), Vector2.zero, Vector2.zero);
            panel.tilesRoot = tiles.gameObject;
            panel.tiles = new Image[PuzzleTiles];
            panel.tileKeys = new TMP_Text[PuzzleTiles];
            for (int i = 0; i < PuzzleTiles; i++) {
                float left = i / (float)PuzzleTiles, right = (i + 1) / (float)PuzzleTiles;
                Image tile = Panel($"Key {i + 1}", tiles, new Vector2(left + 0.01f, 0f), new Vector2(right - 0.01f, 1f), PuzzleTrack);
                panel.tiles[i] = tile;
                TMP_Text label = UIKit.Text(tile.rectTransform, $"Label {i + 1}", (i + 1).ToString(), UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                label.overflowMode = TextOverflowModes.Overflow;
                Object.DestroyImmediate(label.GetComponent<LayoutElement>());
                panel.tileKeys[i] = label;
            }

            // One pip per step.
            RectTransform pips = Rect("Pips", card, new Vector2(0.3f, PuzzlePipBand.x), new Vector2(0.7f, PuzzlePipBand.y), Vector2.zero, Vector2.zero);
            panel.pips = new Image[PuzzlePips];
            for (int i = 0; i < PuzzlePips; i++) {
                float centre = (i + 0.5f) / PuzzlePips;
                Image pip = Panel($"Pip {i + 1}", pips, new Vector2(centre, 0.5f), new Vector2(centre, 0.5f), Amber);
                pip.rectTransform.sizeDelta = new Vector2(34f, 14f);
                panel.pips[i] = pip;
            }
            card.gameObject.SetActive(false);
        }

        static TMP_Text PuzzleText(RectTransform card, string name, Vector2 band, UIText style, UIColorRole role, TextAlignmentOptions align) {
            TMP_Text text = UIKit.Text(card, name, string.Empty, style, role, align);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.04f, band.x);
            rect.anchorMax = new Vector2(0.96f, band.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            text.overflowMode = TextOverflowModes.Overflow; // the theme's font has no ellipsis glyph
            text.textWrappingMode = TextWrappingModes.Normal;
            Object.DestroyImmediate(text.GetComponent<LayoutElement>());
            return text;
        }
    }
}
