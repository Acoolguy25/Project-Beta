using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using RyanAssets.Input;
using RyanAssets.UI.Textbox;
using UnityEngine.UI;

namespace RyanAssets.UI.Textbox {
    public class TextboxHelper : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private TMP_InputField tmpInputField;
        private CustomInputField customTmpInputField;
        private InputField inputField;
        private void Awake(){
            customTmpInputField = GetComponent<CustomInputField>();
            tmpInputField = GetComponent<TMP_InputField>();
            inputField = GetComponent<InputField>();
        }
        void OnSelected(string text) {
            InputService.SetInputScreenActive(InputScreen.Textbox, true);
            customTmpInputField?.placeholder.gameObject.SetActive(false);
            tmpInputField?.placeholder.gameObject.SetActive(false);
            inputField?.placeholder.gameObject.SetActive(false);
        }
        void OnDeselected(string text) {
            InputService.SetInputScreenActive(InputScreen.Textbox, false);
            customTmpInputField?.placeholder.gameObject.SetActive(true);
            tmpInputField?.placeholder.gameObject.SetActive(true);
            inputField?.placeholder.gameObject.SetActive(true);
        }
        public void OnSelect(BaseEventData eventData){
            if (inputField)
                OnSelected(inputField.text);
            else if (customTmpInputField)
                OnSelected(customTmpInputField.text);
            else
                OnSelected(tmpInputField.text);
        }
        public void OnDeselect(BaseEventData eventData){
            if (inputField)
                OnDeselected(inputField.text);
            else if (customTmpInputField)
                OnDeselected(customTmpInputField.text);
            else
                OnDeselected(tmpInputField.text);
        }
        void OnEnable(){
            // tmpInputField?.onSubmit.AddListener(OnSubmitted);
            customTmpInputField?.onSubmit.AddListener(OnSubmitted);

            customTmpInputField?.onEndEdit.AddListener(OnSubmitted);
            tmpInputField?.onEndEdit.AddListener(OnSubmitted);
            inputField?.onEndEdit.AddListener(OnSubmitted);
        }
        void OnDisable() {
            // tmpInputField?.onSubmit.RemoveListener(OnSubmitted);
            customTmpInputField?.onSubmit.RemoveListener(OnSubmitted);
            customTmpInputField?.onEndEdit.RemoveListener(OnSubmitted);
            tmpInputField?.onEndEdit.RemoveListener(OnSubmitted);
            inputField?.onEndEdit.RemoveListener(OnSubmitted);

            // Fake submitted to deselect it fully.
            OnSubmitted(string.Empty);
        }
        void OnSubmitted(string text){
            customTmpInputField?.DeactivateInputField();
            tmpInputField?.DeactivateInputField();
            inputField?.DeactivateInputField();
            
            if ((tmpInputField || customTmpInputField) && EventSystem.current && !EventSystem.current.alreadySelecting)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
