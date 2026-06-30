using RyanAssets.Core;
using RyanAssets.Input;
using RyanAssets.UI.ButtonGrid;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Windows;

namespace RyanAssets.UI.Autocomplete {
    public class AutocompleteUI : ButtonGridUI<AutocompleteElementData> {
        [SerializeField]
        TMP_InputField inputField;
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
        public override void AddPrefab(AutocompleteElementData autocompleteElementData) {
            AddPrefab(autocompleteElementData, globalOrder--);
        }
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
            foreach (var kvp in elementToPrefab) {
                if (kvp.Value.display.ToLower().Contains(inputField.text.Substring(1).ToLower())) {
                    if (selectedElement == null) {
                        selectedElement = kvp.Key;
                    }
                    kvp.Key.GetComponent<Image>().color = kvp.Key == selectedElement ? Color.green : Color.grey;
                    kvp.Key.SetActive(true);
                    activeElementToPrefabs.Add(kvp.Key);
                } else {
                    kvp.Key.SetActive(false);
                }
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
            if (_text == string.Empty || _text[0] != '/') {
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
            SelectDeltaElement(1);
            //StartCoroutine(SetToEnd());
        }
        void OnDown() {
            SelectDeltaElement(-1);
            //StartCoroutine(SetToEnd());
        }
        void OnTab() {
            if (!selectedElement) return;
            inputField.text = "/" + elementToPrefab[selectedElement].display;
            SetToEnd();
        }
        void OnEnable() {
            TextboxControls.upEvent += OnUp;
            TextboxControls.downEvent += OnDown;
            TextboxControls.tabEvent += OnTab;
        }
        void OnDisable() {
            TextboxControls.upEvent -= OnUp;
            TextboxControls.downEvent -= OnDown;
            TextboxControls.tabEvent -= OnTab;
        }
    }
}
