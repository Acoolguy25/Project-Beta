using System;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.GameSettings {
    public class IntGameSetting: GameSettingsInstance<int> {
        GameSettingsNumberSlider numberSlider;
        public IntGameSetting(): base(GameSettingType.IntGameSetting){
        }
        public override bool TrySet(int new_value){
            value = Math.Clamp(new_value, min, max);
            numberSlider.SetValue(value);
            return true;
        }
        public override void Init(GameObject obj){
            numberSlider = obj.GetComponent<GameSettingsNumberSlider>();
            numberSlider.SetRange(min, max, true);
            numberSlider.GetComponent<Slider>().onValueChanged.AddListener((_) => on_update.Invoke(value));
        }
        public int min, max;
    }
}