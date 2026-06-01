using UnityEngine;

namespace RyanAssets.Client.ClientUI.GameSettings {
    public class BoolGameSetting: GameSettingsInstance<bool> {
        GameSettingsToggle toggle;

        public BoolGameSetting(): base(GameSettingType.BoolGameSetting){

        }
        private int EncodeValue(bool val){
            return (val == true)? 1: 0;
        }
        private bool DecodeValue(int val){
            return (val == 1)? true: false;
        }
        public override bool TrySet(object new_value){
            value = (bool) new_value;

            if (toggle != null) {
                toggle.SetValue(value, false);
            }

            return true;
        }
        public override void Init(GameObject obj){
            toggle = obj.GetComponent<GameSettingsToggle>();

            if (toggle == null) {
                toggle = obj.GetComponentInChildren<GameSettingsToggle>(true);
            }

            if (toggle == null) {
                Debug.LogError($"Missing {nameof(GameSettingsToggle)} for setting '{name}'.");
                return;
            }

            toggle.onValueChanged += (val) => {
                value = val;
                on_update?.Invoke(value);
                Save();
            };
        }
        public override bool Load(){
            if (base.Load()){
                return TrySet(DecodeValue(PlayerPrefs.GetInt(GetSaveName())));
            }
            return false;
        }
        public override void Save(){
            PlayerPrefs.SetInt(GetSaveName(), EncodeValue(value));
            PlayerPrefs.Save();
        }
    }
}
