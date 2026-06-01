using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using RyanAssets.ControlLocking;

namespace RyanAssets.UI {
    public class TextboxHelper : MonoBehaviour {
        private TMP_InputField tmp_inputfield;
        void Awake() {
            tmp_inputfield = GetComponent<TMP_InputField>();
        }
        void OnEnable() {
            tmp_inputfield.onSelect.AddListener(OnTextInputSelected);
            tmp_inputfield.onDeselect.AddListener(OnTextInputUnselected);
        }
        void OnDisable() {
            tmp_inputfield.onSelect.RemoveListener(OnTextInputSelected);
            tmp_inputfield.onDeselect.RemoveListener(OnTextInputUnselected);
        }
        void OnTextInputSelected(string text) {
            ControlLockService.LockControls();
            tmp_inputfield.placeholder.gameObject.SetActive(false);
        }
        void OnTextInputUnselected(string text) {
            ControlLockService.UnlockControls();
            tmp_inputfield.placeholder.gameObject.SetActive(true);
        }
    }
}
