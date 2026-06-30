using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RyanAssets.Commands.Shared {
    public static class CommandVerification {
        static readonly string[] PlayerSpecialValues = { "me", "others", "all" };

        public static bool TryParseCommandLine(string text, out string commandName, out string[] args, out string errorMessage) {
            commandName = string.Empty;
            args = Array.Empty<string>();
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(text)) {
                errorMessage = "Command is empty.";
                return false;
            }

            if (!text.StartsWith("/", StringComparison.Ordinal)) {
                errorMessage = "Commands must start with '/'.";
                return false;
            }

            string withoutSlash = text.Substring(1).Trim();
            if (withoutSlash.Length == 0) {
                errorMessage = "Command name is empty.";
                return false;
            }

            if (!TryTokenize(withoutSlash, out string[] tokens, out errorMessage))
                return false;

            if (tokens.Length == 0) {
                errorMessage = "Command name is empty.";
                return false;
            }

            commandName = tokens[0];
            args = tokens.Skip(1).ToArray();
            return true;
        }

        public static bool TryGetCommandConfig(IEnumerable<CommandConfig> commands, string commandName, out CommandConfig config) {
            foreach (CommandConfig command in commands) {
                if (string.Equals(command.commandName, commandName, StringComparison.OrdinalIgnoreCase)) {
                    config = command;
                    return true;
                }
            }

            config = default;
            return false;
        }

        public static bool VerifyCommand(IEnumerable<CommandConfig> commands, string commandName, string[] args, IEnumerable<string> playerNames, out string errorMessage) {
            if (!TryGetCommandConfig(commands, commandName, out CommandConfig config)) {
                errorMessage = $"Command '{commandName}' does not exist.";
                return false;
            }

            return VerifyCommand(config, args, playerNames, out errorMessage);
        }

        public static bool VerifyCommand(CommandConfig config, string[] args, IEnumerable<string> playerNames, out string errorMessage) {
            errorMessage = string.Empty;
            CommandArgumentConfig[] expectedArgs = config.arguments ?? Array.Empty<CommandArgumentConfig>();
            string[] providedArgs = args ?? Array.Empty<string>();

            if (providedArgs.Length != expectedArgs.Length) {
                errorMessage = $"Command '{config.commandName}' expects {expectedArgs.Length} argument(s), got {providedArgs.Length}.";
                return false;
            }

            HashSet<string> names = new(playerNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < expectedArgs.Length; i++) {
                if (!VerifyArgument(expectedArgs[i], providedArgs[i], names, out errorMessage))
                    return false;
            }

            return true;
        }

        public static List<string> GetCommandPredictions(IEnumerable<CommandConfig> commands, string typedCommand) {
            string typed = typedCommand ?? string.Empty;
            return commands
                .Where(command => !string.IsNullOrWhiteSpace(command.commandName))
                .Select(command => command.commandName)
                .Where(commandName => commandName.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .OrderBy(commandName => commandName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<string> GetArgumentPredictions(CommandArgumentConfig config, string typedValue, IEnumerable<string> playerNames) {
            string typed = typedValue ?? string.Empty;
            List<string> suggestions = new();

            if (config.suggestions != null)
                suggestions.AddRange(config.suggestions.Where(value => !string.IsNullOrWhiteSpace(value)));

            switch (config.type) {
                case CommandArgumentType.Int:
                    AddNumericSuggestion(suggestions, MathfRoundToString(config.min));
                    AddNumericSuggestion(suggestions, MathfRoundToString(config.max));
                    break;
                case CommandArgumentType.Float:
                    AddNumericSuggestion(suggestions, config.min.ToString(CultureInfo.InvariantCulture));
                    AddNumericSuggestion(suggestions, config.max.ToString(CultureInfo.InvariantCulture));
                    break;
                case CommandArgumentType.Player:
                case CommandArgumentType.Players:
                    suggestions.AddRange(PlayerSpecialValues);
                    if (playerNames != null)
                        suggestions.AddRange(playerNames.Where(value => !string.IsNullOrWhiteSpace(value)));
                    break;
            }

            return suggestions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(value => value.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static bool VerifyArgument(CommandArgumentConfig config, string arg, HashSet<string> playerNames, out string errorMessage) {
            errorMessage = string.Empty;
            string argName = string.IsNullOrWhiteSpace(config.name) ? "argument" : config.name;

            switch (config.type) {
                case CommandArgumentType.Int:
                    if (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)) {
                        errorMessage = $"Argument '{argName}' must be an integer.";
                        return false;
                    }

                    if (intValue < config.min || intValue > config.max) {
                        errorMessage = $"Argument '{argName}' must be between {config.min} and {config.max}.";
                        return false;
                    }

                    return true;
                case CommandArgumentType.Float:
                    if (!float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue)) {
                        errorMessage = $"Argument '{argName}' must be a float.";
                        return false;
                    }

                    if (floatValue < config.min || floatValue > config.max) {
                        errorMessage = $"Argument '{argName}' must be between {config.min} and {config.max}.";
                        return false;
                    }

                    return true;
                case CommandArgumentType.Player:
                case CommandArgumentType.Players:
                    foreach (string value in SplitPlayersArgument(arg)) {
                        if (!IsPlayerReferenceValid(value, playerNames)) {
                            errorMessage = $"Player '{value}' was not found.";
                            return false;
                        }
                    }

                    return true;
                case CommandArgumentType.String:
                default:
                    if (config.suggestions == null || config.suggestions.Length == 0)
                        return true;

                    if (config.suggestions.Any(value => string.Equals(value, arg, StringComparison.OrdinalIgnoreCase)))
                        return true;

                    errorMessage = $"Argument '{argName}' must be one of: {string.Join(", ", config.suggestions)}.";
                    return false;
            }
        }

        static bool IsPlayerReferenceValid(string value, HashSet<string> playerNames) {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (PlayerSpecialValues.Any(special => string.Equals(special, value, StringComparison.OrdinalIgnoreCase)))
                return true;

            return playerNames.Contains(value);
        }

        static IEnumerable<string> SplitPlayersArgument(string arg) {
            return (arg ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim());
        }

        static void AddNumericSuggestion(List<string> suggestions, string value) {
            if (!string.IsNullOrWhiteSpace(value))
                suggestions.Add(value);
        }

        static string MathfRoundToString(float value) {
            return ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
        }

        static bool TryTokenize(string text, out string[] tokens, out string errorMessage) {
            List<string> result = new();
            errorMessage = string.Empty;
            bool inQuotes = false;
            char quote = '\0';
            System.Text.StringBuilder current = new();

            foreach (char c in text) {
                if ((c == '"' || c == '\'') && (!inQuotes || c == quote)) {
                    inQuotes = !inQuotes;
                    quote = inQuotes ? c : '\0';
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes) {
                    if (current.Length > 0) {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                current.Append(c);
            }

            if (inQuotes) {
                tokens = Array.Empty<string>();
                errorMessage = "Command contains an unterminated quoted argument.";
                return false;
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            tokens = result.ToArray();
            return true;
        }
    }
}
