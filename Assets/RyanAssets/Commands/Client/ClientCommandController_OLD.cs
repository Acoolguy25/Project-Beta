//using System;
//using System.Collections.Generic;
//using System.Linq;
//using FishNet;
//using RyanAssets.Commands.Shared;
//using RyanAssets.Shared.Requests;
//using RyanAssets.Shared.Declarations;
//using RyanAssets.Shared.Global;
//using TMPro;
//using UnityEngine;
//using UnityEngine.EventSystems;
//using UnityEngine.InputSystem;
//using UnityEngine.UI;

//namespace RyanAssets.Commands.Client {
//    public class ClientCommandController {
//        readonly TMP_InputField chatBox;
//        readonly Action<SystemMessageBroadcast> createSystemMessage;
//        readonly List<string> commandHistory = new();
//        readonly List<string> autocompleteMatches = new();
//        readonly List<TextMeshProUGUI> suggestionRows = new();
//        string autocompleteKey = string.Empty;
//        Func<string, string> autocompleteApplyMatch;
//        RectTransform suggestionsRoot;
//        Image suggestionsBackground;
//        int autocompleteIndex = -1;
//        int historyIndex = -1;
//        bool settingText;
//        bool suppressNextSubmit;

//        public ClientCommandController(TMP_InputField chatBox, Action<SystemMessageBroadcast> createSystemMessage) {
//            this.chatBox = chatBox;
//            this.createSystemMessage = createSystemMessage;
//            historyIndex = commandHistory.Count;

//            if (this.chatBox != null)
//                this.chatBox.onValueChanged.AddListener(OnChatValueChanged);

//            CreateSuggestionsPopup();
//            RefreshSuggestions();
//        }

//        public void Tick() {
//            if (chatBox == null || !chatBox.isFocused)
//                return;

//            Keyboard keyboard = Keyboard.current;
//            if (keyboard == null)
//                return;

//            if (keyboard.upArrowKey.wasPressedThisFrame) {
//                if (HasVisibleSuggestions())
//                    MoveSuggestionSelection(-1);
//                else
//                    MoveHistory(-1);
//            } else if (keyboard.downArrowKey.wasPressedThisFrame) {
//                if (HasVisibleSuggestions())
//                    MoveSuggestionSelection(1);
//                else
//                    MoveHistory(1);
//            } else if (keyboard.tabKey.wasPressedThisFrame) {
//                ApplySelectedSuggestion();
//            } else if (keyboard.enterKey.wasPressedThisFrame && HasVisibleSuggestions()) {
//                suppressNextSubmit = true;
//                ApplySelectedSuggestion();
//            }
//        }

//        public bool TrySubmit(string text) {
//            return TrySubmit(text, out _);
//        }

//        public bool TrySubmit(string text, out bool clearInput) {
//            clearInput = true;
//            if (suppressNextSubmit) {
//                suppressNextSubmit = false;
//                clearInput = false;
//                FocusChatBox();
//                return true;
//            }

//            if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("/", StringComparison.Ordinal))
//                return false;

//            if (!CommandVerification.TryParseCommandLine(text, out string commandName, out string[] args, out string errorMessage)) {
//                ShowCommandError(errorMessage);
//                return true;
//            }

//            AddHistory(text);

//            if (!CommandVerification.VerifyCommand(GetCommands(), commandName, args, GetPlayerNames(), out errorMessage)) {
//                ShowCommandError(errorMessage);
//                return true;
//            }

//            InstanceFinder.ClientManager.Broadcast(new CommandBroadcast {
//                command = commandName,
//                args = args
//            });

//            HideSuggestions();
//            FocusChatBox();
//            return true;
//        }

//        void MoveHistory(int direction) {
//            if (commandHistory.Count == 0)
//                return;

//            historyIndex = Mathf.Clamp(historyIndex + direction, 0, commandHistory.Count);
//            SetChatText(historyIndex >= commandHistory.Count ? string.Empty : commandHistory[historyIndex]);
//            ResetAutocomplete();
//        }

//        void HandleAutocomplete() {
//            string text = chatBox.text;
//            if (string.IsNullOrEmpty(text) || !text.StartsWith("/", StringComparison.Ordinal))
//                return;

//            if (!TryBuildAutocomplete(text, out string key, out List<string> matches, out Func<string, string> applyMatch))
//                return;

//            if (matches.Count == 0)
//                return;

//            if (!string.Equals(key, autocompleteKey, StringComparison.Ordinal)) {
//                autocompleteKey = key;
//                autocompleteMatches.Clear();
//                autocompleteMatches.AddRange(matches);
//                autocompleteApplyMatch = applyMatch;
//                autocompleteIndex = -1;
//            }

//            autocompleteIndex = (autocompleteIndex + 1) % autocompleteMatches.Count;
//            SetChatText(applyMatch(autocompleteMatches[autocompleteIndex]));
//        }

//        bool TryBuildAutocomplete(string text, out string key, out List<string> matches, out Func<string, string> applyMatch) {
//            key = string.Empty;
//            matches = new List<string>();
//            applyMatch = value => text;

//            if (text == string.Empty || text[0] != '/')
//                return false;

//            string withoutSlash = text.Substring(1);
//            string[] parts = withoutSlash.Split(' ');

//            if (parts.Length == 1) {
//                string typedCommand = parts[0];
//                key = "command:" + typedCommand;
//                matches = CommandVerification.GetCommandPredictions(GetCommands(), typedCommand);
//                applyMatch = command => "/" + command + " ";
//                return true;
//            }

//            string commandName = parts[0];
//            if (!CommandVerification.TryGetCommandConfig(GetCommands(), commandName, out CommandConfig config))
//                return false;

//            CommandArgumentConfig[] commandArgs = config.arguments ?? Array.Empty<CommandArgumentConfig>();
//            int argIndex = parts.Length - 2;
//            if (argIndex < 0 || argIndex >= commandArgs.Length)
//                return false;

//            string typedArg = parts[parts.Length - 1];
//            key = $"arg:{commandName}:{argIndex}:{typedArg}";
//            matches = CommandVerification.GetArgumentPredictions(commandArgs[argIndex], typedArg, GetPlayerNames());
//            applyMatch = value => {
//                string[] output = parts.ToArray();
//                output[output.Length - 1] = value;
//                return "/" + string.Join(" ", output) + " ";
//            };
//            return true;
//        }

//        IEnumerable<CommandConfig> GetCommands() {
//            if (SharedGlobalEvents.Instance == null)
//                return Enumerable.Empty<CommandConfig>();

//            return SharedGlobalEvents.Instance.Commands;
//        }

//        IEnumerable<string> GetPlayerNames() {
//            if (SharedGlobalEvents.Instance == null)
//                return Enumerable.Empty<string>();

//            return SharedGlobalEvents.Instance.Players.Values
//                .Select(player => player.data.username)
//                .Where(username => !string.IsNullOrWhiteSpace(username));
//        }

//        void AddHistory(string fullCommand) {
//            commandHistory.RemoveAll(command => string.Equals(command, fullCommand, StringComparison.Ordinal));
//            commandHistory.Add(fullCommand);
//            historyIndex = commandHistory.Count;
//            ResetAutocomplete();
//        }

//        void OnChatValueChanged(string value) {
//            if (settingText)
//                return;

//            if (historyIndex < commandHistory.Count && value != commandHistory[historyIndex])
//                historyIndex = commandHistory.Count;

//            ResetAutocomplete();
//            RefreshSuggestions();
//        }

//        void SetChatText(string value) {
//            settingText = true;
//            chatBox.text = value;
//            chatBox.caretPosition = chatBox.text.Length;
//            settingText = false;
//            RefreshSuggestions();
//        }

//        void CreateSuggestionsPopup() {
//            if (chatBox == null || suggestionsRoot != null)
//                return;

//            GameObject popup = new("CommandSuggestions", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
//            popup.transform.SetParent(chatBox.transform.parent, false);

//            suggestionsRoot = popup.GetComponent<RectTransform>();
//            suggestionsRoot.anchorMin = new Vector2(0f, 1f);
//            suggestionsRoot.anchorMax = new Vector2(1f, 1f);
//            suggestionsRoot.pivot = new Vector2(0.5f, 0f);
//            suggestionsRoot.anchoredPosition = new Vector2(0f, 4f);
//            suggestionsRoot.sizeDelta = new Vector2(0f, 128f);

//            suggestionsBackground = popup.GetComponent<Image>();
//            suggestionsBackground.color = new Color(0.04f, 0.05f, 0.06f, 0.94f);
//            suggestionsBackground.raycastTarget = false;

//            VerticalLayoutGroup layout = popup.GetComponent<VerticalLayoutGroup>();
//            layout.padding = new RectOffset(6, 6, 6, 6);
//            layout.spacing = 2f;
//            layout.childControlWidth = true;
//            layout.childControlHeight = true;
//            layout.childForceExpandWidth = true;
//            layout.childForceExpandHeight = false;

//            HideSuggestions();
//        }

//        void RefreshSuggestions() {
//            if (suggestionsRoot == null || chatBox == null)
//                return;

//            if (!TryBuildAutocomplete(chatBox.text, out string key, out List<string> matches, out Func<string, string> applyMatch) || matches.Count == 0) {
//                HideSuggestions();
//                return;
//            }

//            if (!string.Equals(key, autocompleteKey, StringComparison.Ordinal)) {
//                autocompleteKey = key;
//                autocompleteIndex = 0;
//            } else if (autocompleteIndex < 0) {
//                autocompleteIndex = 0;
//            }

//            autocompleteMatches.Clear();
//            autocompleteMatches.AddRange(matches.Take(8));
//            autocompleteApplyMatch = applyMatch;
//            autocompleteIndex = Mathf.Clamp(autocompleteIndex, 0, autocompleteMatches.Count - 1);

//            EnsureSuggestionRows(autocompleteMatches.Count);
//            for (int i = 0; i < suggestionRows.Count; i++) {
//                bool active = i < autocompleteMatches.Count;
//                suggestionRows[i].transform.parent.gameObject.SetActive(active);
//                if (!active)
//                    continue;

//                suggestionRows[i].text = autocompleteMatches[i];
//                suggestionRows[i].transform.parent.GetComponent<Image>().color = i == autocompleteIndex
//                    ? new Color(0.18f, 0.36f, 0.58f, 0.95f)
//                    : new Color(0f, 0f, 0f, 0f);
//            }

//            suggestionsRoot.gameObject.SetActive(true);
//        }

//        void EnsureSuggestionRows(int count) {
//            while (suggestionRows.Count < count) {
//                GameObject row = new("SuggestionRow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
//                row.transform.SetParent(suggestionsRoot, false);
//                row.GetComponent<Image>().raycastTarget = false;

//                LayoutElement layoutElement = row.GetComponent<LayoutElement>();
//                layoutElement.preferredHeight = 22f;
//                layoutElement.minHeight = 22f;

//                GameObject label = new("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
//                label.transform.SetParent(row.transform, false);
//                RectTransform labelRect = label.GetComponent<RectTransform>();
//                labelRect.anchorMin = Vector2.zero;
//                labelRect.anchorMax = Vector2.one;
//                labelRect.offsetMin = new Vector2(8f, 1f);
//                labelRect.offsetMax = new Vector2(-8f, -1f);

//                TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
//                text.fontSize = 14f;
//                text.color = Color.white;
//                text.alignment = TextAlignmentOptions.MidlineLeft;
//                text.raycastTarget = false;
//                text.textWrappingMode = TextWrappingModes.NoWrap;
//                text.overflowMode = TextOverflowModes.Ellipsis;
//                suggestionRows.Add(text);
//            }
//        }

//        bool HasVisibleSuggestions() {
//            return suggestionsRoot != null && suggestionsRoot.gameObject.activeSelf && autocompleteMatches.Count > 0;
//        }

//        void MoveSuggestionSelection(int direction) {
//            if (!HasVisibleSuggestions())
//                return;

//            autocompleteIndex = (autocompleteIndex + direction + autocompleteMatches.Count) % autocompleteMatches.Count;
//            RefreshSuggestions();
//        }

//        void ApplySelectedSuggestion() {
//            if (!HasVisibleSuggestions()) {
//                HandleAutocomplete();
//                return;
//            }

//            autocompleteIndex = Mathf.Clamp(autocompleteIndex, 0, autocompleteMatches.Count - 1);
//            SetChatText(autocompleteApplyMatch(autocompleteMatches[autocompleteIndex]));
//        }

//        void ShowCommandError(string message) {
//            createSystemMessage?.Invoke(new SystemMessageBroadcast($"Command Error: {message}", SystemMessageSource.LocalPlayerJoinMessage));
//            FocusChatBox();
//        }

//        void FocusChatBox() {
//            if (chatBox == null)
//                return;

//            if (EventSystem.current != null)
//                EventSystem.current.SetSelectedGameObject(chatBox.gameObject);
//            chatBox.ActivateInputField();
//        }

//        void ResetAutocomplete() {
//            autocompleteKey = string.Empty;
//            autocompleteMatches.Clear();
//            autocompleteApplyMatch = null;
//            autocompleteIndex = -1;
//        }

//        void HideSuggestions() {
//            suggestionsRoot?.gameObject.SetActive(false);
//            autocompleteMatches.Clear();
//            autocompleteApplyMatch = null;
//            autocompleteIndex = -1;
//        }
//    }
//}
