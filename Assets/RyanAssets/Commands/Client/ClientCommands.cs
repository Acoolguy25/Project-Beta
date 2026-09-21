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
                description = "Lists available commands and their descriptions. Pass a command name for its details.",
                arguments = new[] {
                    new CommandArgumentConfig {
                        name = "command",
                        type = CommandArgumentType.String,
                        optional = true
                    }
                }
            },
            new() {
                commandType = "environment",
                commandName = "clear",
                description = "Clears the chat.",
                arguments = Array.Empty<CommandArgumentConfig>()
            }
        };

        static readonly Dictionary<string, CommandHandler> Actions = BuildActions();

        /// <summary>
        /// Suggestions that depend on live state and so cannot live in the serialized
        /// <see cref="CommandArgumentConfig.suggestions"/> array.
        /// </summary>
        public static bool TryGetDynamicSuggestions(string commandName, int argumentIndex,
            IEnumerable<CommandConfig> commands, out List<string> suggestions) {
            if (argumentIndex == 0 && string.Equals(commandName, "help", StringComparison.OrdinalIgnoreCase)) {
                suggestions = CommandVerification.GetCommandPredictions(commands, string.Empty);
                return true;
            }

            suggestions = null;
            return false;
        }

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
            List<CommandConfig> configs = controller.GetCommandConfigs()
                .OrderBy(config => config.commandName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string requested = args is { Length: > 0 } ? args[0].TrimStart('/') : string.Empty;
            if (!string.IsNullOrWhiteSpace(requested)) {
                if (!CommandVerification.TryGetCommandConfig(configs, requested, out CommandConfig config)) {
                    controller.ShowCommandError($"Command '{requested}' does not exist.");
                    return;
                }

                controller.ShowSystemMessage(DescribeCommand(config));
                return;
            }

            controller.ShowSystemMessage("Commands:\n" + string.Join("\n", configs.Select(DescribeCommand)));
        }

        static string DescribeCommand(CommandConfig config) {
            string usage = "/" + config.commandName;
            foreach (CommandArgumentConfig argument in config.arguments ?? Array.Empty<CommandArgumentConfig>()) {
                string argName = string.IsNullOrWhiteSpace(argument.name) ? argument.type.ToString().ToLowerInvariant() : argument.name;
                usage += argument.optional ? $" [{argName}]" : $" <{argName}>";
            }

            return string.IsNullOrWhiteSpace(config.description) ? usage : $"{usage} - {config.description}";
        }

        static void Clear(ClientCommandController controller, string commandName, string[] args) {
            controller.ClearChat();
        }
    }
}
