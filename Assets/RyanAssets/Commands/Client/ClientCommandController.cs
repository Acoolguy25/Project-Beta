using FishNet;
using RyanAssets.Client.ClientUI.Chat;
using RyanAssets.Commands.Shared;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Requests;
using RyanAssets.UI.Autocomplete;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RyanAssets.Commands.Client {
    public class ClientCommandController : AutocompleteUI {
        readonly Dictionary<string, ClientCommandRegistration> clientCommands = new(StringComparer.OrdinalIgnoreCase);
        int oldSpaces = -1;

        protected override void Start() {
            base.Start();
            RegisterAllClientCommands();
            ClientChat.cancelSendMessageFuncs.Add(TrySubmit);
            SharedGlobalEvents.OnCommandsUpdated += OnCommandsUpdated;
            inputField.onValueChanged.AddListener(OnInputChanged);
            UpdateCommands(true);
        }

        protected override void OnDestroy() {
            ClientChat.cancelSendMessageFuncs.Remove(TrySubmit);
            SharedGlobalEvents.OnCommandsUpdated -= OnCommandsUpdated;
            inputField?.onValueChanged.RemoveListener(OnInputChanged);
            base.OnDestroy();
        }

        public void RegisterCommand(CommandConfig config, ClientCommands.CommandHandler handler) {
            if (string.IsNullOrWhiteSpace(config.commandName))
                throw new ArgumentException("Command name cannot be empty.", nameof(config));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            clientCommands[config.commandName] = new ClientCommandRegistration(config, handler);
            UpdateCommands(true);
        }

        public void RegisterCommand(CommandConfig config) {
            RegisterCommand(config, ClientCommands.Resolve(config.commandName));
        }

        public bool UnregisterCommand(string commandName) {
            bool removed = clientCommands.Remove(commandName);
            if (removed)
                UpdateCommands(true);
            return removed;
        }

        void RegisterAllClientCommands() {
            foreach (CommandConfig config in ClientCommands.ClientGameCommands)
                RegisterCommand(config);
        }

        void OnCommandsUpdated() => UpdateCommands(true);
        void OnInputChanged(string value) => UpdateCommands();

        void UpdateCommands(bool force = false) {
            string text = inputField?.text ?? string.Empty;
            int spaces = text.Count(c => c == ' ');
            if (!force && spaces == oldSpaces)
                return;

            oldSpaces = spaces;
            ClearPrefabs();
            string fullString = text.Length > AutocompletePrefix.Length
                ? text.Substring(AutocompletePrefix.Length)
                : string.Empty;
            List<string> options = new();
            IReadOnlyDictionary<string, CommandConfig> commandConfigs = GetCommands();

            if (spaces == 0) {
                options = CommandVerification.GetCommandPredictions(commandConfigs.Values, fullString);
            } else {
                string[] parts = fullString.Split(' ');
                if (CommandVerification.TryGetCommandConfig(commandConfigs.Values, parts[0], out CommandConfig commandConfig)
                    && commandConfig.arguments != null
                    && commandConfig.arguments.Length > spaces - 1
                    && parts.Length > spaces - 1) {
                    options = CommandVerification.GetArgumentPredictions(
                        commandConfig.arguments[spaces - 1], parts[spaces - 1], PlayerData.GetPlayerNames());
                }
            }

            foreach (string newText in options)
                AddPrefab(new() { display = newText });
            Refresh();
        }

        IReadOnlyDictionary<string, CommandConfig> GetCommands() {
            Dictionary<string, CommandConfig> configs = new(StringComparer.OrdinalIgnoreCase);
            if (SharedGlobalEvents.Instance != null) {
                foreach (CommandConfig config in SharedGlobalEvents.Instance.Commands)
                    configs[config.commandName] = config;
            }

            // A local command owns its name and is never sent to the server.
            foreach (ClientCommandRegistration registration in clientCommands.Values)
                configs[registration.Config.commandName] = registration.Config;
            return configs;
        }

        internal IEnumerable<CommandConfig> GetCommandConfigs() => GetCommands().Values;

        public bool TrySubmit(string text) {
            if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(AutocompletePrefix, StringComparison.Ordinal))
                return false;

            if (!CommandVerification.TryParseCommandLine(text, out string commandName, out string[] args, out string errorMessage)) {
                ShowCommandError(errorMessage);
                return true;
            }

            IReadOnlyDictionary<string, CommandConfig> commands = GetCommands();
            if (commands.TryGetValue(commandName, out CommandConfig commandConfig)
                && commandConfig.arguments != null
                && commandConfig.arguments.Length > 0
                && args.Length + 1 == commandConfig.arguments.Length
                && (commandConfig.arguments[^1].type == CommandArgumentType.Player
                    || commandConfig.arguments[^1].type == CommandArgumentType.Players)) {
                args = args.Append("me").ToArray();
            }

            if (!CommandVerification.VerifyCommand(commands.Values, commandName, args, PlayerData.GetPlayerNames(), out errorMessage)) {
                ShowCommandError(errorMessage);
                return true;
            }

            if (clientCommands.TryGetValue(commandName, out ClientCommandRegistration registration))
                registration.Handler(this, registration.Config.commandName, args);
            else
                InstanceFinder.ClientManager.Broadcast(new CommandBroadcast { command = commandName, args = args });
            return true;
        }

        internal void ShowCommandError(string errorMessage) {
            if (!string.IsNullOrEmpty(errorMessage))
                ShowSystemMessage($"Command Error: {errorMessage}");
        }

        internal void ShowSystemMessage(string message) {
            ClientChat.Instance?.CreateSystemMessage(new SystemMessageBroadcast(message, SystemMessageSource.ClientCommand));
        }

        internal void ClearChat() {
            ClientChat.Instance?.ClearPrefabs();
        }

        readonly struct ClientCommandRegistration {
            public readonly CommandConfig Config;
            public readonly ClientCommands.CommandHandler Handler;

            public ClientCommandRegistration(CommandConfig config, ClientCommands.CommandHandler handler) {
                Config = config;
                Handler = handler;
            }
        }
    }
}
