using UnityEngine;
using RyanAssets.UI.ListGrid;
using RyanAssets.UI;
using RyanAssets.PromptService;
using RyanAssets.Input;
using FishNet;
using FishNet.Managing;
using System.Collections.Generic;
using System;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.GameSettings {
    public class GameSettingsClient: ListGridUI<GameSettingsInstance> {
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
                title = "Master Volume",
                category = GameSettingCategory.Audio,
                min = 1, max = 100, start = 50
            },
            ["MenuMusic"] = new IntGameSetting(){
                title = "Menu Music",
                category = GameSettingCategory.Audio,
                min = 1, max = 100, start = 50
            },
            ["GameMusic"] = new IntGameSetting(){
                title = "Game Music",
                category = GameSettingCategory.Audio,
                min = 1, max = 100, start = 50
            },
            ["UIVolume"] = new IntGameSetting(){
                title = "UI Volume",
                category = GameSettingCategory.Audio,
                min = 1, max = 100, start = 50
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

        [SerializeField]
        List<GameObject> settingToAdditionalPrefab;
        [SerializeField]
        GameObject categoryHeaderPrefab;
        [SerializeField]
        GameObject gameActionButtonsContainer;
        [SerializeField]
        bool hideGameActionButtons;

        readonly HashSet<GameSettingCategory> createdCategories = new();
        protected void Awake(){
            base.Start();
            foreach (var keyVal in gameSettingsConfigUI) {
                keyVal.Value.name = keyVal.Key; // Assign the names!
            }
            SetGameActionButtonsVisible(!hideGameActionButtons);
            AddSettingsPrefabs();
            GameSettingsControls.leaveToggledEvent += OnLeaveGame_ButtonPressed;
            GameSettingsControls.resetToggledEvent += OnReset_ButtonPressed;
        }
        protected override void Start(){
            // disabled
        }
        void AddSettingsPrefabs(){
            foreach (GameSettingsInstance setting in gameSettingsConfigUI.Values) {
                if (createdCategories.Add(setting.category))
                    CreateCategoryHeader(setting.category);

                GameObject placeholder = Instantiate(modelPrefab, contentTarget, false);
                OnPrefabAdded(placeholder, setting);
            }
            UpdateLayout();
        }
        void CreateCategoryHeader(GameSettingCategory category) {
            GameObject header = Instantiate(categoryHeaderPrefab, contentTarget, false);
            header.name = category.ToString();

            Text text = header.GetComponentInChildren<Text>(true);
            if (text != null)
                text.text = category.ToString();
        }
        void OnPrefabAdded(GameObject prefab, GameSettingsInstance setting){
            GameObject additionalObj = Instantiate(settingToAdditionalPrefab[(int) setting.type]);
            additionalObj.transform.SetParent(prefab.transform.parent, false);
            additionalObj.name = prefab.name;
            additionalObj.transform.GetChild(0).GetComponent<Text>().text = setting.title;
            additionalObj.name = setting.name;
            switch (setting) {
                case IntGameSetting intSetting:
                    // additionalObj.transform.GetChild(1)
                    var numberSlider = additionalObj.GetComponent<GameSettingsNumberSlider>();
                    intSetting.Init(numberSlider.gameObject);
                    break;

                case BoolGameSetting boolSetting:
                    var toggle = additionalObj.transform.GetChild(1).GetComponent<GameSettingsToggle>();
                    boolSetting.Init(toggle.gameObject);
                    break;
            }
            setting.Load();
            setting.InitDone();
            Destroy(prefab);
        }
        void SetGameActionButtonsVisible(bool visible) {
            if (gameActionButtonsContainer != null)
                gameActionButtonsContainer.SetActive(visible);
        }

        public async void OnLeaveGame_ButtonPressed(){
            PromptButton res = await PromptManager.Instance.PromptLocalUser("Leave Game?", "Are you sure you want to leave this game?", PromptId.LeaveGameConfirm, PromptManager.ButtonPreset_YesNo);
            if (res != PromptButton.Yes)
                return;
            PromptManager.PromptWait("Disconnecting", "Disconnecting\nThis should only take a second", PromptId.LeaveGameAwait);
            InstanceFinder.ClientManager.StopConnection();
        }
        public async void OnReset_ButtonPressed(){

        }
        public void CloseSettingsCanvas_ButtonPressed(){
            GetComponent<CanvasGroupController>().SetVisible(false, 1 / 3f);
        }
        private void OnEnable(){
            InputService.SetInputScreenActive(InputScreen.GameSettings, true);
        }
        private void OnDisable(){
            InputService.SetInputScreenActive(InputScreen.GameSettings, false);
        }
    }
}
