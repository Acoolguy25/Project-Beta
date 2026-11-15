using UnityEngine;
using RyanAssets.UI;
using RyanAssets.UI.Hover;
using RyanAssets.PromptService;
using RyanAssets.Input;
using FishNet;
using FishNet.Managing;
using System.Collections.Generic;
using System;
using UnityEngine.UI;
using TMPro;
using RyanAssets.Shared.Requests;
using RyanAssets.Shared.Declarations;

namespace RyanAssets.Client.ClientUI.GameSettings {
    /// <summary>
    /// The game menu: resume / reset / leave actions over a list of player settings, built from
    /// <see cref="gameSettingsConfigUI"/>. In the main menu (<see cref="hideGameActionButtons"/>)
    /// it is a plain settings screen. Shown and hidden through the <see cref="HudPanel"/> beside it.
    /// </summary>
    [RequireComponent(typeof(HudPanel))]
    public class GameSettingsClient : MonoBehaviour {
        public static readonly Dictionary<string, GameSettingsInstance> gameSettingsConfigUI = new(){
            ["InvertedMovementControls"] = new BoolGameSetting(){
                title = "Inverted Movement Controls",
                category = GameSettingCategory.Controls,
                start = false
            },
            ["ZoomSensitivity"] = new IntGameSetting(){
                // name = "BidirectionalZoomSensitivity",
                title = "Zoom Sensitivity",
                category = GameSettingCategory.Camera,
                min = 1, max = 250, start = 100
                // on_update = (val) => {
                    
                // }
            },
            ["TurnSensitivity"] = new IntGameSetting(){
                title = "Turn Sensitivity",
                category = GameSettingCategory.Camera,
                min = 1, max = 250, start = 100
            },
            ["VerticalTurnSensitivity"] = new IntGameSetting(){
                title = "Vertical Turn Sensitivity",
                category = GameSettingCategory.Camera,
                min = 1, max = 250, start = 100
            },
            ["HorizontalTurnSensitivity"] = new IntGameSetting(){
                title = "Horizontal Turn Sensitivity",
                category = GameSettingCategory.Camera,
                min = 1, max = 250, start = 100
            },
            ["MasterVolume"] = new IntGameSetting(){
                name = "MasterVolume",
                title = "Master Volume",
                category = GameSettingCategory.Audio,
                min = 0, max = 200, start = 50
            },
            ["MenuMusic"] = new IntGameSetting(){
                name = "MenuMusic",
                title = "Menu Music",
                category = GameSettingCategory.Audio,
                min = 0, max = 200, start = 50
            },
            ["GameMusic"] = new IntGameSetting(){
                name = "GameMusic",
                title = "Game Music",
                category = GameSettingCategory.Audio,
                min = 0, max = 200, start = 50
            },
            ["SFX"] = new IntGameSetting(){
                name = "SFX",
                title = "SFX Volume",
                category = GameSettingCategory.Audio,
                min = 0, max = 200, start = 50
            },
            [ShowGameHelpSetting] = new BoolGameSetting(){
                // Named here as well as in Awake: it is loaded at startup, before the settings
                // screen has ever been opened to assign the names.
                name = ShowGameHelpSetting,
                title = "Game Help (control tips and tooltips)",
                category = GameSettingCategory.Interface,
                start = true
            },
            // new IntGameSetting(){
            //     name = "VerticalZoomSensitivity",
            //     title = "Vertical Zoom Sensitivity",
            //     min = 1, max = 250, start = 100
            // },
            // new IntGameSetting(){
            //     name = "HorizontalZoomSensitivity",
            //     title = "Horizontal Zoom Sensitivity",
            //     min = 1, max = 250, start = 100
            // },
            // new BoolGameSetting(){
            //     name = "InvertedControls",
            //     title = "Inverted Controls",
            //     start = false
            // }
        };
        /// <summary>
        /// Whether game modes explain their controls: the tooltips and instruction lines published
        /// through <see cref="GameHelp"/>. Experienced players can turn it off.
        /// </summary>
        public const string ShowGameHelpSetting = "ShowGameHelp";

        /// <summary>
        /// Publishes the saved Game Help choice at startup and every change after it. Game modes read
        /// <see cref="GameHelp"/> rather than this screen, which may never be opened in a session.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BindGameHelp() {
            BoolGameSetting setting = GetSetting<BoolGameSetting>(ShowGameHelpSetting);
            if (setting == null)
                return;
            setting.Load();
            // Removed first so a play session entered without a domain reload does not bind twice.
            setting.on_update -= PublishGameHelp;
            setting.on_update += PublishGameHelp;
            PublishGameHelp(setting.value);
        }

        static void PublishGameHelp(bool enabled) => GameHelp.Enabled = enabled;

        public static T GetSetting<T>(string name) where T : GameSettingsInstance {
            if (gameSettingsConfigUI.TryGetValue(name, out GameSettingsInstance setting)){
                Debug.Assert(setting is T, $"Setting '{name}' is not of type {typeof(T).Name}");
                return (T) setting;
            }
            Debug.LogError($"Setting '{name}' not found.");
            return null;
        }
        public static T GetSettingValue<T>(string name){
            if (gameSettingsConfigUI.TryGetValue(name, out GameSettingsInstance setting)){
                return (T) setting.GetValue();
            }
            Debug.LogError($"Setting '{name}' not found.");
            return default;
        }

        [Header("Settings list")]
        [SerializeField] RectTransform listContent;
        [SerializeField] GameSettingRow sliderRowPrefab;
        [SerializeField] GameSettingRow toggleRowPrefab;
        [SerializeField] TMP_Text categoryHeaderPrefab;

        [Header("Header")]
        [SerializeField] TMP_Text titleText;
        [SerializeField] string inGameTitle = "Menu";
        [SerializeField] string settingsOnlyTitle = "Settings";
        [SerializeField] Button closeButton;
        [Tooltip("Clicking here (the dimmed backdrop) closes the menu.")]
        [SerializeField] Button dismissArea;

        [Header("Game actions")]
        [Tooltip("Resume / reset / leave row. Hidden outside a game (main menu).")]
        [SerializeField] GameObject gameActions;
        [SerializeField] bool hideGameActionButtons;
        [SerializeField] Button resumeButton;
        [SerializeField] Button resetButton;
        [SerializeField] Button leaveButton;

        HudPanel panel;

        void Awake() {
            foreach (var keyVal in gameSettingsConfigUI)
                keyVal.Value.name = keyVal.Key;

            panel = GetComponent<HudPanel>();
            panel.Toggled += OnMenuToggled;
            if (titleText != null)
                titleText.text = hideGameActionButtons ? settingsOnlyTitle : inGameTitle;
            if (gameActions != null)
                gameActions.SetActive(!hideGameActionButtons);

            Listen(closeButton, Close);
            Listen(dismissArea, Close);
            Listen(resumeButton, Close);
            Listen(resetButton, OnReset_ButtonPressed);
            Listen(leaveButton, OnLeaveGame_ButtonPressed);

            BuildSettingsList();
        }

        static void Listen(Button button, UnityEngine.Events.UnityAction action) {
            if (button != null)
                button.onClick.AddListener(action);
        }

        void BuildSettingsList() {
            var createdCategories = new HashSet<GameSettingCategory>();
            foreach (GameSettingsInstance setting in gameSettingsConfigUI.Values) {
                if (createdCategories.Add(setting.category)) {
                    TMP_Text header = Instantiate(categoryHeaderPrefab, listContent, false);
                    header.name = setting.category.ToString();
                    header.text = setting.category.ToString().ToUpperInvariant();
                }
                GameSettingRow prefab = setting.type switch {
                    GameSettingType.IntGameSetting => sliderRowPrefab,
                    GameSettingType.BoolGameSetting => toggleRowPrefab,
                    _ => null,
                };
                if (prefab == null) {
                    Debug.LogError($"No settings row prefab for {setting.type} ('{setting.name}').", this);
                    continue;
                }
                Instantiate(prefab, listContent, false).Bind(setting);
            }
        }

        /// <summary>Closes the menu (Resume, the close button, or Escape via the top bar).</summary>
        public void Close() {
            if (panel != null)
                panel.Hide();
        }

        public async void OnLeaveGame_ButtonPressed() {
            PromptButton res = await PromptManager.Instance.PromptLocalUser("Leave Game?", "Are you sure you want to leave this game?", PromptId.LeaveGameConfirm, PromptManager.ButtonPreset_YesNo);
            if (res != PromptButton.Yes)
                return;
            PromptManager.PromptWait("Disconnecting", "Disconnecting\nThis should only take a second", PromptId.LeaveGameAwait);
            InstanceFinder.ClientManager.StopConnection();
        }

        public async void OnReset_ButtonPressed() {
            PromptButton res = await PromptManager.Instance.PromptLocalUser("Reset Character?", "Are you sure you want to reset your character?", PromptId.ResetCharacterConfirm, PromptManager.ButtonPreset_YesNo);
            if (res != PromptButton.Yes)
                return;
            Close();
            InstanceFinder.ClientManager.Broadcast(new MenuActionRequest {
                type = MenuActionType.ResetCharacter
            });
        }

        void OnMenuToggled(bool open) {
            InputService.SetInputScreenActive(InputScreen.GameSettings, open);
            GameSettingsControls.leaveToggledEvent -= OnLeaveGame_ButtonPressed;
            GameSettingsControls.resetToggledEvent -= OnReset_ButtonPressed;
            if (open && !hideGameActionButtons) {
                GameSettingsControls.leaveToggledEvent += OnLeaveGame_ButtonPressed;
                GameSettingsControls.resetToggledEvent += OnReset_ButtonPressed;
            }
        }

        void OnDestroy() {
            if (panel != null)
                panel.Toggled -= OnMenuToggled;
            if (panel != null && panel.Visible)
                InputService.SetInputScreenActive(InputScreen.GameSettings, false);
            GameSettingsControls.leaveToggledEvent -= OnLeaveGame_ButtonPressed;
            GameSettingsControls.resetToggledEvent -= OnReset_ButtonPressed;
        }
    }
}
