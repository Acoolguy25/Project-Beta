using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FishNet;
using FishNet.Connection;
using RyanAssets.Commands.Shared;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Player;

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
        public static void Player_SetWalkspeed(NetworkConnection caller, string commandName, string[] args) {
            List<NetworkConnection> conns = CommandVerification.GetPlayersFromArgument(args[0], PlayerData.Players, caller);
            foreach (NetworkConnection conn in conns)
            {
                if (PlayerData.Players.TryGetValue(conn, out var player))
                {
                    player.walkSpeed.Value = float.Parse(args[1]);
                }
            }
        }
        public static void Player_SetSprintspeed(NetworkConnection caller, string commandName, string[] args)
        {
            List<NetworkConnection> conns = CommandVerification.GetPlayersFromArgument(args[0], PlayerData.Players, caller);
            foreach (NetworkConnection conn in conns)
            {
                if (PlayerData.Players.TryGetValue(conn, out var player))
                {
                    player.sprintSpeed.Value = float.Parse(args[1]);
                }
            }
        }
        public static void Player_Kill(NetworkConnection caller, string commandName, string[] args)
        {
            List<NetworkConnection> conns = CommandVerification.GetPlayersFromArgument(args[0], PlayerData.Players, caller);
            foreach (NetworkConnection conn in conns)
            {
                if (ServerPlayerCharacter.ClientToCharacter.TryGetValue(conn, out var character))
                {
                    character.Kill(Characters.Shared.DamageSource.Command);
                }
            }
        }
        public static void Player_Respawn(NetworkConnection caller, string commandName, string[] args)
        {
            List<NetworkConnection> conns = CommandVerification.GetPlayersFromArgument(args[0], PlayerData.Players, caller);
            foreach (NetworkConnection conn in conns)
            {
                ServerPlayerCharacter.DespawnPlayerCharacter(conn);
                ServerPlayerCharacter.Instance.SpawnPlayerCharacter(conn);
            }
        }
        public static void Player_Kick(NetworkConnection caller, string commandName, string[] args)
        {
            List<NetworkConnection> conns = CommandVerification.GetPlayersFromArgument(args[0], PlayerData.Players, caller);
            foreach (NetworkConnection conn in conns)
            {
                ServerPlayerEvents.KickPlayer(conn, $"Kicked by {PlayerData.GetPlayerName(caller) ?? "anonymous"}");
            }
        }
        public static void Server_Shutdown(NetworkConnection caller, string commandName, string[] args)
        {
            ServerBootStrap.StopServer($"Shutdown by {PlayerData.GetPlayerName(caller) ?? "anonymous"}");
        }

        public static void Server_Restart(NetworkConnection caller, string commandName, string[] args) {
            ServerChat.SendSystemMessage(new("Server is restarting...", SystemMessageSource.ServerRestart));
            ServerBootStrap.RestartServerEvent?.Invoke();
        }
    }
}
