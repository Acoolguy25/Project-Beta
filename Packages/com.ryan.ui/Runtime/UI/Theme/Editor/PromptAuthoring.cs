using RyanAssets.PromptService;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static RyanAssets.UI.Editor.UIKit;

namespace RyanAssets.UI.Editor {
    /// <summary>
    /// Generates the prompt dialog (PromptCanvas) from the active <see cref="UITheme"/>.
    /// Menu: Ryan/UI/Rebuild Prompt Dialog.
    /// </summary>
    public static class PromptAuthoring {
        public const string CanvasPath = "Packages/com.ryan.ui/Runtime/PromptService/PromptCanvas.prefab";

        /// <summary>Above every HUD panel and menu, so a prompt is never hidden.</summary>
        public const int SortingOrder = 1300;
        const float WindowWidth = 460f;
        const float WindowRadius = 16f;
        const float ButtonHeight = 42f;

        /// <summary>Left to right on screen: negative answers first, the positive one last.</summary>
        static readonly (PromptButton id, string label, bool primary)[] Buttons = {
            (PromptButton.Cancel, "Cancel", false),
            (PromptButton.No, "No", false),
            (PromptButton.Retry, "Try again", true),
            (PromptButton.Yes, "Yes", true),
            (PromptButton.Ok, "OK", true),
        };

        [MenuItem("Ryan/UI/Rebuild Prompt Dialog")]
        public static void Build() {
            UIThemeAssets.Ensure();
            RectTransform root = Canvas("PromptCanvas", SortingOrder);

            RectTransform content = Fill(root, "Content");
            CanvasGroupController fader = Fader(content, visible: false);
            RectTransform scrim = Fill(content, "Scrim");
            Paint(scrim.gameObject.AddComponent<Image>(), UIColorRole.Scrim); // blocks clicks behind

            RectTransform window = Pinned(content, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WindowWidth, 0f));
            Shadow(window, 40f, 12f, 0.5f);
            Rounded(window, UIColorRole.SurfaceRaised, WindowRadius);
            Column(window.gameObject, 10f, new RectOffset(28, 28, 26, 24));
            FitContent(window.gameObject, false, true);

            TextMeshProUGUI title = Text(window, "Title", "Title", UIText.Title);
            title.textWrappingMode = TextWrappingModes.Normal;
            TextMeshProUGUI description = Text(window, "Description", "Description", UIText.Body, UIColorRole.TextSecondary);
            description.textWrappingMode = TextWrappingModes.Normal;
            description.lineSpacing = 6f;

            RectTransform footer = Node(window, "Footer");
            Size(footer, -1f, ButtonHeight);
            Row(footer.gameObject, 10f, null, TextAnchor.MiddleRight);

            Image spinner = Icon(footer, "refresh", 24f, UIColorRole.Accent);
            spinner.gameObject.name = "Waiting";
            spinner.gameObject.AddComponent<UISpinner>();
            RectTransform spacer = Node(footer, "Spacer");
            Size(spacer, -1f, 1f, 1f);

            var slots = new (PromptButton, Button)[Buttons.Length];
            for (int i = 0; i < Buttons.Length; i++) {
                (PromptButton id, string label, bool primary) = Buttons[i];
                Button button = TextButton(footer, id + "Button", label, null,
                    primary ? UIColorRole.Accent : UIColorRole.SurfaceHover,
                    primary ? UIColorRole.OnAccent : UIColorRole.TextPrimary, ButtonHeight);
                LayoutElement size = button.GetComponent<LayoutElement>();
                size.flexibleWidth = 0f;
                size.minWidth = 104f;
                slots[i] = (id, button);
            }

            var manager = root.gameObject.AddComponent<PromptManager>();
            SerializedFields.Set(manager, "content", fader);
            SerializedFields.Set(manager, "titleText", title);
            SerializedFields.Set(manager, "descText", description);
            SerializedFields.Set(manager, "waitingIndicator", spinner.gameObject);
            var so = new SerializedObject(manager);
            SerializedProperty array = so.FindProperty("slots");
            array.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++) {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("id").intValue = (int)slots[i].Item1;
                element.FindPropertyRelative("button").objectReferenceValue = slots[i].Item2;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root.gameObject, CanvasPath);
            AssetDatabase.SaveAssets();
        }
    }
}
