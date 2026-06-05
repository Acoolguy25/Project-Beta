using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using RyanAssets.Input;
using UnityEngine.UI;

namespace RyanAssets.UI {
    public class TextboxHelper : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private TMP_InputField tmpInputField;
        private InputField inputField;
        private void Awake(){
            tmpInputField = GetComponent<TMP_InputField>();
            inputField = GetComponent<InputField>();
        }
        void OnSelected(string text) {
            InputService.SetInputScreenActive(InputScreen.Textbox, true);
            tmpInputField?.placeholder.gameObject.SetActive(false);
            inputField?.placeholder.gameObject.SetActive(false);
        }
        void OnDeselected(string text) {
            InputService.SetInputScreenActive(InputScreen.Textbox, false);
            tmpInputField?.placeholder.gameObject.SetActive(true);
            inputField?.placeholder.gameObject.SetActive(true);
        }
        public void OnSelect(BaseEventData eventData){
            OnSelected(inputField? inputField.text: tmpInputField.text);
        }
        public void OnDeselect(BaseEventData eventData){
            OnDeselected(inputField? inputField.text: tmpInputField.text);
        }
        void OnEnable(){
            // tmpInputField?.onSubmit.AddListener(OnSubmitted);
            tmpInputField?.onEndEdit.AddListener(OnSubmitted);

            inputField?.onEndEdit.AddListener(OnSubmitted);
        }
        void OnSubmitted(string text){
            tmpInputField?.DeactivateInputField();
            inputField?.DeactivateInputField();
            
            if (tmpInputField && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
