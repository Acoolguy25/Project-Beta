using UnityEngine;
using RyanAssets.UI.ListGrid;
using System.Collections.Generic;
using System;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.GameSettings {
    public class GameSettingsClient: ListGridUI<GameSettingsInstance> {
        public static readonly GameSettingsInstance[] gameSettingsConfigUI = new GameSettingsInstance[]{
            new IntGameSetting(){
                name = "BidirectionalZoomSensitivity",
                title = "Birdirectional Zoom Sensitivity",
                min = 1, max = 250,
                on_update = (val) => {
                    
                }
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
            foreach (GameSettingsInstance instance in gameSettingsConfigUI) {
                if (instance.name == name) {
                    Debug.Assert(instance is T, $"Setting '{name}' is not of type {typeof(T).Name}");
                    return (T) instance;
                }
            }
            Debug.LogError($"Setting '{name}' not found.");
            return null;
        }

        [SerializeField]
        List<GameObject> settingToAdditionalPrefab;
        protected override void Start(){
            base.Start();
            OnCreatePrefab += OnPrefabAdded;
            AddPrefabs(gameSettingsConfigUI);
        }
        void OnPrefabAdded(GameObject prefab, GameSettingsInstance setting){
            GameObject additionalObj = Instantiate(settingToAdditionalPrefab[(int) setting.type]);
            additionalObj.transform.SetParent(prefab.transform.parent, false);
            additionalObj.name = prefab.name;
            additionalObj.transform.GetChild(0).GetComponent<Text>().text = setting.title;
            switch (setting) {
                case IntGameSetting intSetting:
                    // additionalObj.transform.GetChild(1)
                    var numberSlider = additionalObj.GetComponent<GameSettingsNumberSlider>();
                    intSetting.Init(numberSlider.gameObject);
                    break;

                case BoolGameSetting boolSetting:
                    var toggleButton = additionalObj.transform.GetChild(1).GetComponent<Button>();
                    boolSetting.Init(toggleButton.gameObject);
                    break;
            }
            setting.Load();
            Destroy(prefab);
        }
    }
}