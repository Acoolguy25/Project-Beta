using RyanAssets.Client.ClientUI.GameSettings;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static RyanAssets.UI.Editor.UIKit;

namespace RyanAssets.UI.Editor {
    /// <summary>
    /// Generates the game menu (pause + settings screen) and its row prefabs from the active
    /// <see cref="UITheme"/>. Menu: Ryan/UI/Rebuild Game Menu. Layout numbers live here; colours,
    /// fonts and shapes live in the theme.
    /// </summary>
    public static class GameMenuAuthoring {
        const string Folder = "Packages/com.ryan.ui/Runtime/Client/ClientUI/GameSettings";
        public const string CanvasPath = Folder + "/GameSettingsCanvas.prefab";
        const string SliderRowPath = Folder + "/Prefabs/GameSettingsSliderPrefab.prefab";
        const string ToggleRowPath = Folder + "/Prefabs/GameSettingsTogglePrefab.prefab";
        const string HeaderPath = Folder + "/Prefabs/GameSettingsCategoryHeaderPrefab.prefab";

        public const int SortingOrder = 60;
        static readonly Vector2 WindowSize = new(640f, 660f);
        const float WindowRadius = 16f;
        const float RowHeight = 52f;
        const float ActionHeight = 44f;

        [MenuItem("Ryan/UI/Rebuild Game Menu")]
        public static void Build() {
            UIThemeAssets.Ensure();
            GameObject header = BuildCategoryHeader();
            GameObject sliderRow = BuildSliderRow();
            GameObject toggleRow = BuildToggleRow();
            BuildCanvas(header.GetComponent<TMP_Text>(), sliderRow.GetComponent<GameSettingRow>(), toggleRow.GetComponent<GameSettingRow>());
            AssetDatabase.SaveAssets();
        }

        // --- Rows -----------------------------------------------------------------

        static GameObject BuildCategoryHeader() {
            TextMeshProUGUI text = Text(null, "GameSettingsCategoryHeader", "CATEGORY", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.BottomLeft);
            text.margin = new Vector4(4f, 0f, 0f, 6f);
            Size(text, -1f, 40f);
            return SavePrefab(text.gameObject, HeaderPath);
        }

        static RectTransform RowBase(string name, out GameSettingRow row) {
            RectTransform rect = Node(null, name);
            rect.sizeDelta = new Vector2(560f, RowHeight);
            Size(rect, -1f, RowHeight);
            Rounded(rect, UIColorRole.SurfaceInset, 10f);
            Row(rect.gameObject, 16f, Pad(16, 0), TextAnchor.MiddleLeft);
            TextMeshProUGUI label = Text(rect, "Label", "Setting", UIText.Body);
            Size(label, -1f, -1f, 1f);
            row = rect.gameObject.AddComponent<GameSettingRow>();
            SerializedFields.Set(row, "label", label);
            return rect;
        }

        static GameObject BuildSliderRow() {
            RectTransform rect = RowBase("GameSettingsSliderRow", out _);

            RectTransform control = Node(rect, "Control");
            Size(control, 280f, 32f);
            Row(control.gameObject, 14f, null, TextAnchor.MiddleRight);

            Slider slider = BuildSlider(control);
            Size(slider, -1f, 20f, 1f);
            TMP_InputField number = BuildNumberBox(control);

            var numberSlider = rect.gameObject.AddComponent<GameSettingsNumberSlider>();
            numberSlider.slider = slider;
            SerializedFields.Set(numberSlider, "numberInput", number);
            SerializedFields.Set(numberSlider, "numberFormat", "0");
            SerializedFields.Set(numberSlider, "wholeNumbers", true);
            return SavePrefab(rect.gameObject, SliderRowPath);
        }

        static GameObject BuildToggleRow() {
            RectTransform rect = RowBase("GameSettingsToggleRow", out _);

            RectTransform track = Node(rect, "Toggle");
            track.sizeDelta = new Vector2(46f, 26f);
            Size(track, 46f, 26f);
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.sprite = Theme.roundedSprite;
            trackImage.type = Image.Type.Sliced;
            trackImage.pixelsPerUnitMultiplier = UIThemeAssets.RoundedSpriteRadius / 13f;
            trackImage.color = Theme.surfaceHover;
            var button = track.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            NoNavigation(button);

            RectTransform knob = Pinned(track, "Knob", new Vector2(0f, 0.5f), new Vector2(13f, 0f), new Vector2(20f, 20f));
            knob.pivot = new Vector2(0.5f, 0.5f);
            Image knobImage = Rounded(knob, UIColorRole.TextPrimary, 10f, raycast: false);
            knobImage.raycastTarget = false;

            var toggle = track.gameObject.AddComponent<GameSettingsToggle>();
            SerializedFields.Set(toggle, "button", button);
            SerializedFields.Set(toggle, "track", trackImage);
            SerializedFields.Set(toggle, "knob", knob);
            SerializedFields.Set(toggle, "knobInset", 13f);
            return SavePrefab(rect.gameObject, ToggleRowPath);
        }

        static Slider BuildSlider(Transform parent) {
            RectTransform rect = Node(parent, "Slider");
            rect.sizeDelta = new Vector2(200f, 20f);

            RectTransform background = Node(rect, "Track");
            background.anchorMin = new Vector2(0f, 0.5f);
            background.anchorMax = new Vector2(1f, 0.5f);
            background.sizeDelta = new Vector2(0f, 6f);
            Rounded(background, UIColorRole.SurfaceHover, 3f, raycast: false);

            RectTransform fillArea = Node(rect, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 6f);
            RectTransform fill = Node(fillArea, "Fill");
            fill.sizeDelta = Vector2.zero;
            Rounded(fill, UIColorRole.Accent, 3f, raycast: false);

            // Zero-height slide area: the Slider stretches the handle vertically over it, so the
            // handle's own 18 px height is its final size (a circle, not a pill).
            RectTransform handleArea = Node(rect, "Handle Slide Area");
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.sizeDelta = new Vector2(-18f, 0f);
            RectTransform handle = Node(handleArea, "Handle");
            handle.sizeDelta = new Vector2(18f, 18f);
            Image handleImage = Rounded(handle, UIColorRole.TextPrimary, 9f);

            // Whole slider height catches clicks, not just the 6 px track.
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.ColorTint;
            slider.colors = new ColorBlock {
                normalColor = Color.white,
                highlightedColor = new Color(0.92f, 0.94f, 1f),
                pressedColor = new Color(0.8f, 0.86f, 1f),
                selectedColor = Color.white,
                disabledColor = new Color(1f, 1f, 1f, 0.4f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
            NoNavigation(slider);
            return slider;
        }

        static TMP_InputField BuildNumberBox(Transform parent) {
            RectTransform rect = Node(parent, "Number");
            Size(rect, 56f, 32f);
            Image background = Rounded(rect, UIColorRole.SurfaceHover, 8f);

            RectTransform area = Fill(rect, "Text Area", 6f, 0f, 6f, 0f);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI text = Text(area, "Text", "100", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.overflowMode = TextOverflowModes.Overflow;

            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.textViewport = area;
            input.textComponent = text;
            input.fontAsset = text.font;
            input.pointSize = text.fontSize;
            input.characterLimit = 6;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.customCaretColor = true;
            input.caretColor = Theme.accent;
            input.selectionColor = Theme.accent * new Color(1f, 1f, 1f, 0.35f);
            input.onFocusSelectAll = true;
            input.transition = Selectable.Transition.None;
            NoNavigation(input);
            rect.gameObject.AddComponent<Textbox.TextboxHelper>();
            return input;
        }

        // --- Canvas ---------------------------------------------------------------

        static void BuildCanvas(TMP_Text categoryHeader, GameSettingRow sliderRow, GameSettingRow toggleRow) {
            RectTransform root = Canvas("GameSettingsCanvas", SortingOrder);

            RectTransform content = Fill(root, "Content");
            CanvasGroupController fader = Fader(content, visible: false);

            RectTransform scrim = Fill(content, "Scrim");
            var scrimImage = scrim.gameObject.AddComponent<Image>();
            Paint(scrimImage, UIColorRole.Scrim);
            var dismiss = scrim.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            NoNavigation(dismiss);

            RectTransform window = Pinned(content, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, WindowSize);
            Shadow(window, 40f, 12f, 0.5f);
            Rounded(window, UIColorRole.SurfaceRaised, WindowRadius);
            Column(window.gameObject, 0f);

            // Header: title + close
            RectTransform header = Node(window, "Header");
            Size(header, -1f, 72f);
            Row(header.gameObject, 12f, new RectOffset(28, 20, 8, 0), TextAnchor.MiddleLeft);
            TextMeshProUGUI title = Text(header, "Title", "Menu", UIText.Title);
            Size(title, -1f, -1f, 1f);
            Button close = IconButton(header, "CloseButton", "close", 36f, UIColorRole.SurfaceInset, 10f, out _, out _);

            // Game actions: resume / reset / leave
            RectTransform actions = Node(window, "GameActions");
            Column(actions.gameObject, 16f, new RectOffset(28, 28, 4, 0));
            RectTransform buttons = Node(actions, "Buttons");
            Row(buttons.gameObject, 10f, null, TextAnchor.MiddleCenter, expandWidth: true);
            Size(buttons, -1f, ActionHeight);
            Button resume = TextButton(buttons, "ResumeButton", "Resume", "play_arrow", UIColorRole.Accent, UIColorRole.OnAccent, ActionHeight, "Esc");
            Button reset = TextButton(buttons, "ResetButton", "Reset", "replay", UIColorRole.SurfaceHover, UIColorRole.TextPrimary, ActionHeight, "R");
            Button leave = TextButton(buttons, "LeaveButton", "Leave", "logout", UIColorRole.Danger, UIColorRole.Danger, ActionHeight, "L");
            leave.GetComponent<UIThemeColor>().Opacity = 0.16f;
            Divider(actions);

            // Settings list
            RectTransform listArea = Node(window, "Settings");
            Size(listArea, -1f, -1f).flexibleHeight = 1f;
            ScrollRect scroll = ScrollList(listArea, 6f, new RectOffset(28, 28, 4, 28), fitContent: true);

            var panel = root.gameObject.AddComponent<HudPanel>();
            SerializedFields.SetEnum(panel, "id", (int)HudPanelId.GameMenu);
            SerializedFields.Set(panel, "content", fader);
            SerializedFields.Set(panel, "visibleOnStart", false);

            var client = root.gameObject.AddComponent<GameSettingsClient>();
            SerializedFields.Set(client, "listContent", scroll.content);
            SerializedFields.Set(client, "sliderRowPrefab", sliderRow);
            SerializedFields.Set(client, "toggleRowPrefab", toggleRow);
            SerializedFields.Set(client, "categoryHeaderPrefab", categoryHeader);
            SerializedFields.Set(client, "titleText", title);
            SerializedFields.Set(client, "closeButton", close);
            SerializedFields.Set(client, "dismissArea", dismiss);
            SerializedFields.Set(client, "gameActions", actions.gameObject);
            SerializedFields.Set(client, "resumeButton", resume);
            SerializedFields.Set(client, "resetButton", reset);
            SerializedFields.Set(client, "leaveButton", leave);

            SavePrefab(root.gameObject, CanvasPath);
        }
    }
}
