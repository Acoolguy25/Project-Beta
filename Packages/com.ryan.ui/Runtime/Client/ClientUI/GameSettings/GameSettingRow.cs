using TMPro;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.GameSettings {
    /// <summary>One row of the settings list: a label on the left and the setting's control on the right.</summary>
    public class GameSettingRow : MonoBehaviour {
        [SerializeField] TMP_Text label;

        public void Bind(GameSettingsInstance setting) {
            name = setting.name;
            label.text = setting.title;
            setting.Init(gameObject);
            setting.Load();
            setting.InitDone();
        }
    }
}
