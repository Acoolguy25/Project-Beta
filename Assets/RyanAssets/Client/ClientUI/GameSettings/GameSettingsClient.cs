using UnityEngine;
using RyanAssets.UI.ListGrid;
using RyanAssets.PromptService;
using FishNet;
using FishNet.Managing;
using System.Collections.Generic;
using System;
using UnityEngine.UI;
using System.Linq;

namespace RyanAssets.Client.ClientUI.GameSettings {
    public class GameSettingsClient: ListGridUI<GameSettingsInstance> {
        public static readonly Dictionary<string, GameSettingsInstance> gameSettingsConfigUI = new(){
            ["ZoomSensitivity"] = new IntGameSetting(){
                // name = "BidirectionalZoomSensitivity",
                title = "Zoom Sensitivity",
                min = 1, max = 250, start = 100
                // on_update = (val) => {
                    
                // }
            },
            ["TurnSensitivity"] = new IntGameSetting(){
                title = "Turn Sensitivity",
                min = 1, max = 250, start = 100
            },
            ["VerticalTurnSensitivity"] = new IntGameSetting(){
                title = "Vertical Turn Sensitivity",
                min = 1, max = 250, start = 100
            },
            ["HorizontalTurnSensitivity"] = new IntGameSetting(){
                title = "Horizontal Turn Sensitivity",
                min = 1, max = 250, start = 100
            },
            ["InvertedMovementControls"] = new BoolGameSetting(){
                title = "Inverted Movement Controls",
                start = false
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
        protected void Awake(){
            base.Start();
            OnCreatePrefab += OnPrefabAdded;
            foreach (var keyVal in gameSettingsConfigUI) {
                keyVal.Value.name = keyVal.Key; // Assign the names!
            }
            AddPrefabs(gameSettingsConfigUI.Values.ToArray());
        }
        protected override void Start(){
            // disabled
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

        public async void OnLeaveGame_ButtonPressed(){
            PromptButton res = await PromptManager.Instance.PromptLocalUser("Leave Game?", "Are you sure you want to leave this game?", PromptId.LeaveGameConfirm, PromptManager.ButtonPreset_YesNo);
            if (res != PromptButton.Yes)
                return;
            PromptManager.PromptWait("Disconnecting", "Disconnecting\nThis should only take a second", PromptId.LeaveGameAwait);
            InstanceFinder.ClientManager.StopConnection();
        }
    }
}
