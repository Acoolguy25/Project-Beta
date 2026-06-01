using System;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.GameSettings {
    public class BoolGameSetting: GameSettingsInstance<bool> {
        public BoolGameSetting(): base(GameSettingType.BoolGameSetting){

        }
        private int EncodeValue(bool val){
            return (val == true)? 1: 0;
        }
        private bool DecodeValue(int val){
            return (val == 1)? true: false;
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