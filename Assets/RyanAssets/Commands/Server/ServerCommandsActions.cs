using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FishNet.Connection;
using RyanAssets.Commands.Shared;

namespace RyanAssets.Commands.Server {
    public static class ServerCommandsActions {
        static readonly Dictionary<string, ServerCommandService.CommandHandler> Actions = BuildActions();

        public static ServerCommandService.CommandHandler Resolve(string commandName) {
            string key = Normalize(commandName);
            if (Actions.TryGetValue(key, out ServerCommandService.CommandHandler action))
                return action;

            return ServerCommandService.UnknownGlobalCommand;
        }

        static Dictionary<string, ServerCommandService.CommandHandler> BuildActions() {
            Dictionary<string, ServerCommandService.CommandHandler> actions = new(StringComparer.OrdinalIgnoreCase);
            MethodInfo[] methods = typeof(ServerCommandsActions).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (MethodInfo method in methods) {
                if (!IsCommandActionMethod(method))
                    continue;

                ServerCommandService.CommandHandler handler =
                    (ServerCommandService.CommandHandler)Delegate.CreateDelegate(typeof(ServerCommandService.CommandHandler), method);
                actions[Normalize(method.Name)] = handler;
            }

            return actions;
        }

        static bool IsCommandActionMethod(MethodInfo method) {
            if (method.ReturnType != typeof(void))
                return false;

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 3
                && parameters[0].ParameterType == typeof(NetworkConnection)
                && parameters[1].ParameterType == typeof(string)
                && parameters[2].ParameterType == typeof(string[]);
        }

        static string Normalize(string value) {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        static void Help(NetworkConnection caller, string commandName, string[] args) {
            string commandList = string.Join(", ", ServerCommandService.GetRegisteredCommandConfigs()
                .Select(config => "/" + config.commandName)
                .OrderBy(command => command, StringComparer.OrdinalIgnoreCase));

            ServerCommandService.SendSystemMessage(caller, $"Commands: {commandList}");
        }

        static void SayHi(NetworkConnection caller, string commandName, string[] args) {
            string username = ServerCommandService.GetPlayerUsername(caller);
            ServerCommandService.SendSystemMessage(caller, $"{username} says hi.");
        }
    }
}
