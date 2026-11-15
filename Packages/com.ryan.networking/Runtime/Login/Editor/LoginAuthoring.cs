using System.Linq;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using RyanAssets.UI.Textbox;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static RyanAssets.UI.Editor.UIKit;

namespace RyanAssets.Login.Editor {
    /// <summary>
    /// Rebuilds the login / account screen (LoginCanvas) from the active <see cref="UITheme"/>.
    /// Menu: Ryan/UI/Rebuild Login Screen.
    ///
    /// The prefab is rebuilt in place: the root and its login components keep their ids, so the
    /// main menu scene's reference from <see cref="LoginScreen"/> to the game browser survives,
    /// and the nested prefabs (mute button, settings screen) are kept as they are.
    /// </summary>
    public static class LoginAuthoring {
        const string CanvasPath = "Packages/com.ryan.networking/Runtime/Login/LoginCanvas.prefab";
        const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";

        const float CardWidth = 460f;
        const float CardRadius = 18f;
        const float ButtonHeight = 46f;
        const float ScreenMargin = 24f;

        [MenuItem("Ryan/UI/Rebuild Login Screen")]
        public static void Build() {
            UIThemeAssets.Ensure();
            GameObject root = PrefabUtility.LoadPrefabContents(CanvasPath);
            try {
                ClearToRoot(root);
                BuildScreen((RectTransform)root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, CanvasPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            RemoveStaleSceneOverrides();
        }

        /// <summary>Keeps the root's canvas and login components and the nested prefabs; removes the rest.</summary>
        static void ClearToRoot(GameObject root) {
            for (int i = root.transform.childCount - 1; i >= 0; i--) {
                GameObject child = root.transform.GetChild(i).gameObject;
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(child))
                    Object.DestroyImmediate(child);
            }
            foreach (Component component in root.GetComponents<Component>()) {
                if (component is RectTransform or UnityEngine.Canvas or CanvasScaler or GraphicRaycaster or CanvasGroup
                    or CanvasGroupController or LoginScreen or LoginManager or LoginAuthenticator or LoginModifyUsername)
                    continue;
                Object.DestroyImmediate(component);
            }
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Theme.referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
        }

        static void BuildScreen(RectTransform root) {
            var screen = root.GetComponent<LoginScreen>();
            var manager = root.GetComponent<LoginManager>();
            var authenticator = root.GetComponent<LoginAuthenticator>();
            var modifyUsername = root.GetComponent<LoginModifyUsername>();

            // Backdrop: the theme's darkest colour, so the card stands out.
            RectTransform background = Fill(root, "Background");
            Paint(background.gameObject.AddComponent<Image>(), UIColorRole.Scrim, 1f / Theme.scrim.a); // opaque
            RectTransform card = Pinned(root, "Card", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardWidth, 0f));
            Shadow(card, 48f, 16f, 0.55f);
            Rounded(card, UIColorRole.SurfaceRaised, CardRadius);
            Column(card.gameObject, 0f, new RectOffset(32, 32, 32, 32));
            FitContent(card.gameObject, false, true);

            GameObject signIn = BuildSignIn(card, authenticator);
            GameObject account = BuildAccount(card, screen, authenticator, modifyUsername, out CustomInputField username);

            // Settings, top right.
            Button settings = IconButton(root, "SettingsButton", "settings", 44f, UIColorRole.SurfaceInset, 12f, out _, out _);
            var settingsRect = (RectTransform)settings.transform;
            settingsRect.anchorMin = settingsRect.anchorMax = settingsRect.pivot = Vector2.one;
            settingsRect.anchoredPosition = new Vector2(-ScreenMargin, -ScreenMargin);
            UnityEventTools.AddPersistentListener(settings.onClick, new UnityAction(screen.ToggleSettings_ButtonPressed));

            // Nested prefabs: mute button bottom left, the settings screen on top of everything.
            foreach (Transform child in root.Cast<Transform>().ToArray()) {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(child.gameObject))
                    continue;
                if (child.GetComponent<HudPanel>() != null) {
                    child.SetAsLastSibling();
                } else {
                    var rect = (RectTransform)child;
                    rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
                    rect.anchoredPosition = new Vector2(ScreenMargin, ScreenMargin);
                }
            }

            SerializedFields.Set(screen, "signInGroup", signIn);
            SerializedFields.Set(screen, "accountGroup", account);
            SerializedFields.Set(screen, "animationTime", 0.25f);
            SerializedFields.Set(manager, "loginScreen", screen);
            SerializedFields.Set(manager, "usernameInputField", username);
            SerializedFields.Set(modifyUsername, "usernameInputField", username);
        }

        static GameObject BuildSignIn(RectTransform card, LoginAuthenticator authenticator) {
            RectTransform group = Node(card, "SignIn");
            Column(group.gameObject, 12f);

            RectTransform badge = Node(group, "Badge");
            Size(badge, 56f, 56f);
            Rounded(badge, UIColorRole.Accent, 28f, 0.16f, raycast: false);
            Row(badge.gameObject, 0f, null, TextAnchor.MiddleCenter);
            Icon(badge, "person", 30f, UIColorRole.Accent);
            group.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;

            Wrapped(Text(group, "Title", "Welcome", UIText.Display));
            Wrapped(Text(group, "Message", "Sign in with your Unity account to save your progress and play online.", UIText.Body, UIColorRole.TextSecondary));
            Spacer(group, 8f);

            Button signIn = TextButton(group, "SignInButton", "Sign in with Unity", "person", UIColorRole.Accent, UIColorRole.OnAccent, ButtonHeight);
            StretchInColumn(signIn);
            UnityEventTools.AddPersistentListener(signIn.onClick, new UnityAction(authenticator.UnityLogin));
            return group.gameObject;
        }

        static GameObject BuildAccount(RectTransform card, LoginScreen screen, LoginAuthenticator authenticator, LoginModifyUsername modifyUsername, out CustomInputField username) {
            RectTransform group = Node(card, "Account");
            Column(group.gameObject, 12f);

            Wrapped(Text(group, "Title", "Your account", UIText.Display));
            Wrapped(Text(group, "Message", "You're signed in. Pick the name other players see, then jump into a game.", UIText.Body, UIColorRole.TextSecondary));
            Spacer(group, 6f);

            Text(group, "UsernameLabel", "USERNAME", UIText.Caption, UIColorRole.TextMuted);
            RectTransform usernameRow = Node(group, "UsernameRow");
            Size(usernameRow, -1f, 44f);
            Row(usernameRow.gameObject, 8f, null, TextAnchor.MiddleLeft);
            username = InputBox(usernameRow, "UsernameInput", "Username", 44f);
            username.characterLimit = 20;
            UnityEventTools.AddVoidPersistentListener(username.onEndEdit, new UnityAction(modifyUsername.UsernameSubmit));
            Button change = TextButton(usernameRow, "ChangeButton", "Change", null, UIColorRole.SurfaceHover, UIColorRole.TextPrimary, 44f);
            change.GetComponent<LayoutElement>().flexibleWidth = 0f;
            UnityEventTools.AddPersistentListener(change.onClick, new UnityAction(modifyUsername.EditButtonClicked));

            Spacer(group, 10f);
            RectTransform buttons = Node(group, "Buttons");
            Size(buttons, -1f, ButtonHeight);
            Row(buttons.gameObject, 10f, null, TextAnchor.MiddleCenter, expandWidth: true);
            Button signOut = TextButton(buttons, "SignOutButton", "Sign out", "logout", UIColorRole.SurfaceHover, UIColorRole.TextPrimary, ButtonHeight);
            UnityEventTools.AddPersistentListener(signOut.onClick, new UnityAction(authenticator.UnityLogout));
            Button play = TextButton(buttons, "ContinueButton", "Continue", "play_arrow", UIColorRole.Accent, UIColorRole.OnAccent, ButtonHeight);
            play.GetComponent<LayoutElement>().flexibleWidth = 2f;
            UnityEventTools.AddPersistentListener(play.onClick, new UnityAction(screen.Continue_ButtonPressed));
            return group.gameObject;
        }

        static void Wrapped(TMP_Text text) => text.textWrappingMode = TextWrappingModes.Normal;

        static void Spacer(Transform parent, float height) => Size(Node(parent, "Spacer"), -1f, height);

        /// <summary>A button in a column whose children don't stretch: make this one full width.</summary>
        static void StretchInColumn(Button button) => button.GetComponent<LayoutElement>().flexibleWidth = 1f;

        /// <summary>
        /// The main menu scene overrode parts of the old screen (now deleted); drop those overrides
        /// so the scene stays clean. The reference from LoginScreen to the game browser stays.
        /// </summary>
        static void RemoveStaleSceneOverrides() {
            var scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPath);
            GameObject[] instances = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<Transform>(true))
                .Select(t => t.gameObject)
                .Where(go => PrefabUtility.IsOutermostPrefabInstanceRoot(go) && PrefabUtility.GetCorrespondingObjectFromSource(go) == asset)
                .ToArray();
            if (instances.Length == 0)
                return;
            PrefabUtility.RemoveUnusedOverrides(instances, InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
