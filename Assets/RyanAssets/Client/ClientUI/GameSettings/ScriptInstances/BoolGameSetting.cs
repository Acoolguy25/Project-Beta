using System;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.GameSettings {
    public class BoolGameSetting: GameSettingsInstance<bool> {
        public BoolGameSetting(): base(GameSettingType.BoolGameSetting){

        }
    }
}