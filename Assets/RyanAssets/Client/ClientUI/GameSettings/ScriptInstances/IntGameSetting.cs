using System;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.GameSettings {
    public class IntGameSetting: GameSettingsInstance<int> {
        GameSettingsNumberSlider numberSlider;
        public IntGameSetting(): base(GameSettingType.IntGameSetting){
        }
        public override bool TrySet(object new_value){
            value = Math.Clamp((int) new_value, min, max);
            numberSlider.SetValue(value);
            return true;
        }
        public override void Init(GameObject obj){
            numberSlider = obj.GetComponent<GameSettingsNumberSlider>();
            numberSlider.SetRange(min, max, true);
            numberSlider.slider.onValueChanged.AddListener((val) => {
                value = (int) val;
                on_update?.Invoke(value);
                Save();
            });
        }
        public override bool Load(){
            if (base.Load()){
                return TrySet(PlayerPrefs.GetInt(GetSaveName()));
            }
            return false;
        }
        public override void Save(){
            PlayerPrefs.SetInt(GetSaveName(), value);
            PlayerPrefs.Save();
        }
        public int min = 1, max = 250;
    }
}