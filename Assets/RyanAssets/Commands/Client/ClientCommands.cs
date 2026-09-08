using RyanAssets.Commands.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RyanAssets.Commands.Client {
    public static class ClientCommands {
        public static readonly CommandConfig[] ClientGameCommands = {
            new() {
                commandType = "environment",
                commandName = "help",
                description = "Lists available commands.",
                arguments = Array.Empty<CommandArgumentConfig>()
            },
            new() {
                commandType = "environment",
                commandName = "clear",
                description = "Clears the chat.",
                arguments = Array.Empty<CommandArgumentConfig>()
            }
        };

        static readonly Dictionary<string, CommandHandler> Actions = BuildActions();

        public delegate void CommandHandler(ClientCommandController controller, string commandName, string[] args);

        public static CommandHandler Resolve(string commandName) {
            if (Actions.TryGetValue(Normalize(commandName), out CommandHandler action))
                return action;
            return UnknownClientCommand;
        }

        static Dictionary<string, CommandHandler> BuildActions() {
            Dictionary<string, CommandHandler> actions = new(StringComparer.OrdinalIgnoreCase);
            foreach (MethodInfo method in typeof(ClientCommands).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
                if (!IsCommandActionMethod(method))
                    continue;
                actions[Normalize(method.Name)] = (CommandHandler)Delegate.CreateDelegate(typeof(CommandHandler), method);
            }
            return actions;
        }

        static bool IsCommandActionMethod(MethodInfo method) {
            if (method.ReturnType != typeof(void))
                return false;
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 3
                && parameters[0].ParameterType == typeof(ClientCommandController)
                && parameters[1].ParameterType == typeof(string)
                && parameters[2].ParameterType == typeof(string[]);
        }

        static string Normalize(string value) {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        static void UnknownClientCommand(ClientCommandController controller, string commandName, string[] args) {
            controller.ShowCommandError($"Command '{commandName}' has no client handler.");
        }

        // Add actions with this signature and a matching config above.
        static void Help(ClientCommandController controller, string commandName, string[] args) {
            string commandList = string.Join(", ", controller.GetCommandConfigs()
                .Select(config => "/" + config.commandName)
                .OrderBy(command => command, StringComparer.OrdinalIgnoreCase));
            controller.ShowSystemMessage($"Commands: {commandList}");
        }

        static void Clear(ClientCommandController controller, string commandName, string[] args) {
            controller.ClearChat();
        }
    }
}
