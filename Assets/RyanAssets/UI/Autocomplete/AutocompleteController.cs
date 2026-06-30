using RyanAssets.Core;
using RyanAssets.Input;
using RyanAssets.UI.ButtonGrid;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using RyanAssets.UI.Textbox;
using UnityEngine.Windows;
using System.Linq;

namespace RyanAssets.UI.Autocomplete {
    public class AutocompleteUI : ButtonGridUI<AutocompleteElementData> {
        public string AutocompletePrefix = "/";
        [SerializeField]
        protected CustomInputField inputField;
        static Color32 selected_color = Color.greenYellow;
        static Color32 not_selected_color = Color.grey;
        Dictionary<GameObject, AutocompleteElementData> elementToPrefab = new();
        List<GameObject> activeElementToPrefabs = new();
        GameObject selectedElement;
        protected override void Start() {
            base.Start();
            OnCreatePrefab += OnPrefabAdded;
            OnDeletePrefab += OnPrefabRemoved;
            OnClickPrefab += OnClickedPrefab;
            inputField.onSelect.AddListener(OnInputFieldSelected);
            inputField.onDeselect.AddListener(OnInputFieldDeselected);
            inputField.onValueChanged.AddListener(OnInputFieldValueChanged);
            Refresh();
        }
        //public override void AddPrefab(AutocompleteElementData autocompleteElementData) {
        //    AddPrefab(autocompleteElementData, globalOrder--);
        //}
        void OnPrefabAdded(GameObject prefabClone, AutocompleteElementData element) {
            prefabClone.GetComponentInChildren<TextMeshProUGUI>().text = element.display;

            elementToPrefab.Add(prefabClone, element);
            OnInputFieldValueChanged(inputField.text);
        }
        void OnPrefabRemoved(GameObject prefabClone) {
            elementToPrefab.Remove(prefabClone);
            OnInputFieldValueChanged(inputField.text);
        }
        void OnClickedPrefab(GameObject prefabClone, AutocompleteElementData element) {
            SelectElement(prefabClone);
            OnTab();
        }
        void RefreshSelection() {
            activeElementToPrefabs.Clear();
            string parsedText = inputField.text.Substring(AutocompletePrefix.Length);
            parsedText = parsedText.Substring(parsedText.LastIndexOf(' ') + 1); // Get last space-separated word
            foreach (var kvp in elementToPrefab.Reverse()) {
                if (kvp.Value.display.ToLower().Contains(parsedText.ToLower())) {
                    kvp.Key.GetComponent<Image>().color = kvp.Key == selectedElement ? selected_color : not_selected_color;
                    kvp.Key.SetActive(true);
                    activeElementToPrefabs.Add(kvp.Key);
                } else {
                    kvp.Key.SetActive(false);
                }
            }
            activeElementToPrefabs.Sort((a, b) => prefabOrder[a.transform].CompareTo(prefabOrder[b.transform]));
            if (selectedElement == null && activeElementToPrefabs.Count > 0) {
                selectedElement = activeElementToPrefabs[0];
                selectedElement.GetComponent<Image>().color = selected_color;
            }
            UpdateLayout(); // handles scrollbars
        }
        void SelectElement(GameObject obj) {
            selectedElement = obj;
            RefreshSelection();
            ScrollIntoView(obj, 0f);
        }
        void SelectDeltaElement(int delta) {
            int count = activeElementToPrefabs.Count;
            if (count == 0)
                return;
            GameObject next = activeElementToPrefabs[MathHelper.Mod((activeElementToPrefabs.FindIndex((elem) => elem == selectedElement) + delta), activeElementToPrefabs.Count)];
            SelectElement(next);
        }
        protected void Refresh() {
            OnInputFieldValueChanged(inputField.text);
        }
        void OnInputFieldValueChanged(string _text) {
            if (_text == string.Empty || !_text.StartsWith(AutocompletePrefix)) {
                gameObject.SetActive(false);
                return;
            }
            gameObject.SetActive(true);
            selectedElement = null;
            RefreshSelection();
        }
        void OnInputFieldSelected(string _text) {
            OnInputFieldValueChanged(_text);
        }
        void OnInputFieldDeselected(string _text) {
            gameObject.SetActive(false);
        }
        void SetToEnd() {
            inputField.caretPosition = inputField.text.Length;
            inputField.selectionStringAnchorPosition = inputField.text.Length;
            inputField.selectionStringFocusPosition = inputField.text.Length;
        }
        void OnUp() {
            SelectDeltaElement(-1);
        }
        void OnDown() {
            SelectDeltaElement(1);
        }
        void OnTab() {
            if (!selectedElement) return;
            string display = elementToPrefab[selectedElement].display;
            string text = inputField.text;

            int lastSpace = text.LastIndexOf(' ');

            if (lastSpace == -1) {
                // First argument
                inputField.text = AutocompletePrefix + display;
            } else {
                // Replace only the current argument
                inputField.text = text[..(lastSpace + 1)] + display;
            }

            inputField.caretPosition = inputField.text.Length;
            SetToEnd();
        }
        void OnEnable() {
            TextboxControls.upEvent += OnUp;
            TextboxControls.downEvent += OnDown;
            TextboxControls.tabEvent += OnTab;
            inputField.AutocompleteActive = true;
        }
        void OnDisable() {
            TextboxControls.upEvent -= OnUp;
            TextboxControls.downEvent -= OnDown;
            TextboxControls.tabEvent -= OnTab;
            inputField.AutocompleteActive = false;
        }
    }
}
