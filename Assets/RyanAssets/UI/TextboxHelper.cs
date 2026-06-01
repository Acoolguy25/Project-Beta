using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

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
            SharedInputController.Instance?.LockControls();
            tmp_inputfield.placeholder.gameObject.SetActive(false);
        }
        void OnTextInputUnselected(string text) {
            SharedInputController.Instance?.UnlockControls();
            tmp_inputfield.placeholder.gameObject.SetActive(true);
        }
    }
}
