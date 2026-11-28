using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.classic_horror.Client;

namespace Universes.UniverseData.classic_horror.Editor {
    public static partial class CH_Authoring {
        // Where each part of the minigame card sits, as fractions of its height (bottom, top).
        static readonly Vector2 PuzzleTitleBand = new(0.87f, 0.99f), PuzzleHintBand = new(0.7f, 0.87f), PuzzleStageBand = new(0.38f, 0.69f),
            PuzzleHeatBand = new(0.325f, 0.365f), PuzzleReadoutBand = new(0.25f, 0.325f), PuzzlePipBand = new(0.16f, 0.24f), PuzzleStatusBand = new(0.03f, 0.15f);
        // The card sits in the lower third (the world above it stays clear: you can look up and see him coming); the dial is round.
        static readonly Vector2 PuzzleCardAnchor = new(0.5f, 0.2f), PuzzleCardSize = new(780f, 380f);
        const float PuzzleDialSize = 132f, PuzzleDialTickRadius = 56f, PuzzleDialNeedleLength = 54f;
        const int PuzzleDialTicks = 12;
        const int PuzzleTiles = 4, PuzzlePips = 5;
        static readonly Color PuzzleTrack = new(0.07f, 0.065f, 0.06f, 0.96f), PuzzleZone = new(0.5f, 0.85f, 0.5f, 0.75f), PuzzleNeedle = new(0.98f, 0.8f, 0.45f);

        /// <summary>
        /// The minigame card: in the lower third of the screen so the world stays visible above it.
        /// One layout serves all three games (a gauge, four keys, or a dial with a heat bar); <see cref="CH_PuzzlePanel"/>
        /// shows what each needs.
        /// </summary>
        static void BuildPuzzle(RectTransform gameplay, CH_ClientController client) {
            RectTransform card = HudKit.Card(gameplay, "Minigame", PuzzleCardAnchor, Vector2.zero, PuzzleCardSize, popup: false);
            HudKit.AccentStripe(card, Amber, 4f);
            var panel = card.gameObject.AddComponent<CH_PuzzlePanel>();
            panel.group = card.gameObject.AddComponent<CanvasGroup>();
            panel.group.blocksRaycasts = false;
            panel.group.alpha = 0f; // shown by fading, never by deactivating, so the panel's timeouts keep running
            client.puzzle = panel;

            panel.title = PuzzleText(card, "Title", PuzzleTitleBand, UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            panel.hint = PuzzleText(card, "Hint", PuzzleHintBand, UIText.Body, UIColorRole.TextMuted, TextAlignmentOptions.Center);
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

            // The lock's dial: a round face, a ring of ticks and a needle that turns on it.
            RectTransform dial = Rect("Dial", card, new Vector2(0.06f, PuzzleStageBand.x), new Vector2(0.94f, PuzzleStageBand.y), Vector2.zero, Vector2.zero);
            panel.dialRoot = dial.gameObject;
            Image face = Panel("Face", dial, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), PuzzleTrack);
            face.sprite = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            face.rectTransform.sizeDelta = new Vector2(PuzzleDialSize, PuzzleDialSize);
            for (int tick = 0; tick < PuzzleDialTicks; tick++) {
                RectTransform spoke = Rect($"Tick {tick}", face.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                spoke.localEulerAngles = new Vector3(0f, 0f, -tick * 360f / PuzzleDialTicks);
                Image mark = Panel("Mark", spoke, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), tick == 0 ? PuzzleNeedle : new Color(1f, 1f, 1f, 0.3f));
                mark.rectTransform.sizeDelta = new Vector2(tick == 0 ? 5f : 3f, tick == 0 ? 11f : 8f);
                mark.rectTransform.anchoredPosition = new Vector2(0f, PuzzleDialTickRadius);
            }
            panel.dialNeedleImage = Panel("Needle", face.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), PuzzleNeedle);
            panel.dialNeedle = panel.dialNeedleImage.rectTransform;
            panel.dialNeedle.pivot = new Vector2(0.5f, 0f);
            panel.dialNeedle.sizeDelta = new Vector2(6f, PuzzleDialNeedleLength);
            panel.dialNeedle.anchoredPosition = Vector2.zero;
            Panel("Hub", face.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), PuzzleNeedle).rectTransform.sizeDelta = new Vector2(12f, 12f);

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
