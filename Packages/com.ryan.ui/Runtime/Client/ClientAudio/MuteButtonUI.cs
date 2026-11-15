using RyanAssets.Client.ClientUI.GameSettings;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientAudio {
    public class MuteButtonUI : MonoBehaviour {
        [SerializeField]
        Sprite mutedSprite, unmutedSprite;
        [SerializeField]
        string targetSettingName;

        IntGameSetting targetSetting;
        Image buttonImage;
        void Start() {
            buttonImage = GetComponent<Image>();
            buttonImage.GetComponent<Button>().onClick.AddListener(OnMuteButtonPressed);
            targetSetting = GameSettingsClient.GetSetting<IntGameSetting>(targetSettingName);
            targetSetting.on_update += RefreshDisplay;
            RefreshDisplay(targetSetting.value);
        }
        void OnDestroy() {
            if (targetSetting != null)
                targetSetting.on_update -= RefreshDisplay;
        }
        void OnMuteButtonPressed() {
            targetSetting.ToggleValue();
        }
        void RefreshDisplay(int val) {
            buttonImage.sprite = (val == 0) ? mutedSprite : unmutedSprite;
        }
    }
}
