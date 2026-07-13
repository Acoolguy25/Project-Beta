using FishNet;
using RyanAssets.Client.ClientUI.Chat;
using RyanAssets.Commands.Shared;
using RyanAssets.DataService;
using RyanAssets.Declarations;
using RyanAssets.Shared.Player;
using RyanAssets.UI.Autocomplete;
using RyanAssets.UI.Textbox;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RyanAssets.Commands.Client {
    public class ClientCommandController : AutocompleteUI {
        int old_spaces = -1;
        protected override void Start() {
            base.Start();
            ClientChat.cancelSendMessageFuncs.Add(TrySubmit);
            SharedGlobalEvents.OnCommandsUpdated += UpdateCommands;
            inputField.onValueChanged.AddListener((_) => UpdateCommands());
            if (SharedGlobalEvents.Instance)
                UpdateCommands();
        }
        void UpdateCommands() {
            int spaces = inputField.text.Count((char c) => c == ' ');
            if (spaces == old_spaces)
                return;
            old_spaces = spaces;
            ClearPrefabs();
            string fullString = inputField.text.Length > AutocompletePrefix.Length? inputField.text.Substring(AutocompletePrefix.Length): string.Empty;
            List<string> options = new();
            var commandConfigs = GetCommands();
            if (spaces == 0) {
                options = CommandVerification.GetCommandPredictions(commandConfigs.Values, fullString);
            } else {
                bool found = CommandVerification.TryGetCommandConfig(commandConfigs.Values, fullString.Split(" ")[0], out CommandConfig commandConfig);
                if (found && commandConfig.arguments.Length > spaces-1)
                    options = CommandVerification.GetArgumentPredictions(commandConfig.arguments[spaces-1], fullString.Split(" ")[spaces-1], PlayerData.GetPlayerNames());
            }
            foreach (var newText in options) {
                AddPrefab(new() { display = newText });
            }
            Refresh();
        }
        Dictionary<string, CommandConfig> GetCommands() {
            Dictionary<string, CommandConfig> commandConfigs = new();
            foreach (var cmd in SharedGlobalEvents.Instance.Commands) {
                commandConfigs.Add(cmd.commandName, cmd);
            }
            return commandConfigs;
        }
        public bool TrySubmit(string text) {
            if (text.StartsWith(AutocompletePrefix)) {
                if (!CommandVerification.TryParseCommandLine(text, out string commandName, out string[] args, out string errorMessage)) {
                    ShowCommandError(errorMessage);
                    return true;
                }
                var str2Cmd = GetCommands();
                if (str2Cmd.TryGetValue(commandName, out CommandConfig commandCfg) && commandCfg.arguments.Length > 0 && args.Length + 1 == commandCfg.arguments.Length && 
                    (commandCfg.arguments[commandCfg.arguments.Length - 1].type == CommandArgumentType.Player || commandCfg.arguments[commandCfg.arguments.Length - 1].type == CommandArgumentType.Players))
                    args = args.Append("me").ToArray();
                if (!CommandVerification.VerifyCommand(str2Cmd.Values, commandName, args, PlayerData.GetPlayerNames(), out errorMessage)) {
                    ShowCommandError(errorMessage);
                    return true;
                }
                InstanceFinder.ClientManager.Broadcast(new CommandBroadcast() { command = commandName, args = args });
                return true;
            }
            return false;
        }
        void ShowCommandError(string errorMessage) {
            if (errorMessage.Length == 0)
                return;
            ClientChat.Instance.CreateSystemMessage(new() { message = errorMessage, type = RyanAssets.Shared.Declarations.SystemMessageSource.ClientCommand });
        }
    }
}