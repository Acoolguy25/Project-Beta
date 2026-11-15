using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using RyanAssets.Commands.Shared;
using RyanAssets.DataService;
using RyanAssets.Shared.Requests;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using UnityEngine;
using RyanAssets.Server.ServerCore;

namespace RyanAssets.Commands.Server {
    public static class ServerCommandService {
        public delegate void CommandHandler(NetworkConnection caller, string commandName, string[] args);
        public delegate object CommandGetter(NetworkConnection caller, string[] args);

        static readonly Dictionary<string, CommandRegistration> Commands = new(StringComparer.OrdinalIgnoreCase);
        static bool registeredBroadcast;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            Commands.Clear();
            registeredBroadcast = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            if (!registeredBroadcast) {
                InstanceFinder.ServerManager.RegisterBroadcast<CommandBroadcast>(OnClientCommand, true);
                registeredBroadcast = true;
            }

            SharedGlobalEvents.OnInstanceReady -= OnSharedGlobalEventsReady;
            SharedGlobalEvents.OnInstanceReady += OnSharedGlobalEventsReady;
            RegisterAllGameCommands();
            SyncRegisteredCommandConfigs();
        }

        public static void RegisterCommand(CommandConfig config, CommandHandler handler, CommandGetter getter = null) {
            if (string.IsNullOrWhiteSpace(config.commandName))
                throw new ArgumentException("Command name cannot be empty.", nameof(config));

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (getter != null && (config.arguments == null || config.arguments.Length == 0))
                throw new ArgumentException("A getter requires a final value argument.", nameof(config));

            config.supportsGetter = getter != null;
            Commands[config.commandName] = new CommandRegistration(config, handler, getter);
            SyncCommandConfig(config);
        }

        public static void RegisterCommand(CommandConfig config) {
            RegisterCommand(config, ServerCommandsActions.Resolve(config.commandName), ServerCommandsActions.ResolveGetter(config.commandName));
        }

        public static bool UnregisterCommand(string commandName) {
            bool removed = Commands.Remove(commandName);
            if (removed)
                RemoveSyncedCommandConfig(commandName);

            return removed;
        }

        static void RegisterAllGameCommands() {
            foreach (CommandConfig config in SharedCommands.AllGameCommands) {
                if (ServerBootStrap.universeCfg.disableDefaultCommands
                    && string.Equals(config.commandType, "character", StringComparison.OrdinalIgnoreCase))
                    continue;

                RegisterCommand(config);
            }
        }

        static void OnSharedGlobalEventsReady() {
            SyncRegisteredCommandConfigs();
        }

        static void OnClientCommand(NetworkConnection conn, CommandBroadcast msg, Channel channel) {
            string[] args = msg.args ?? Array.Empty<string>();
            if (!Commands.TryGetValue(msg.command, out CommandRegistration registration)) {
                SendCommandError(conn, $"Command '{msg.command}' does not exist.");
                return;
            }

            if (!CommandVerification.VerifyCommand(registration.Config, args, GetPlayerNames(), out string error)) {
                SendCommandError(conn, error);
                return;
            }

            if (CommandVerification.IsGetterRequest(registration.Config, args)) {
                object value = registration.Getter(conn, args);
                SendSystemMessage(conn, $"{registration.Config.commandName}: {Convert.ToString(value, CultureInfo.InvariantCulture)}");
            } else {
                registration.Handler(conn, registration.Config.commandName, args);
            }
        }

        static void SyncCommandConfig(CommandConfig config) {
            if (SharedGlobalEvents.Instance == null)
                return;

            RemoveSyncedCommandConfig(config.commandName);
            SharedGlobalEvents.Instance.Commands.Add(config);
        }

        static void SyncRegisteredCommandConfigs() {
            if (SharedGlobalEvents.Instance == null)
                return;

            foreach (CommandRegistration registration in Commands.Values)
                SyncCommandConfig(registration.Config);
        }

        static void RemoveSyncedCommandConfig(string commandName) {
            if (SharedGlobalEvents.Instance == null)
                return;

            for (int i = SharedGlobalEvents.Instance.Commands.Count - 1; i >= 0; i--) {
                if (string.Equals(SharedGlobalEvents.Instance.Commands[i].commandName, commandName, StringComparison.OrdinalIgnoreCase))
                    SharedGlobalEvents.Instance.Commands.RemoveAt(i);
            }
        }

        static IEnumerable<string> GetPlayerNames() {
            if (SharedGlobalEvents.Instance == null)
                return Enumerable.Empty<string>();

            return PlayerData.Players.Values
                .Select(player => player.username.Value)
                .Where(username => !string.IsNullOrWhiteSpace(username));
        }

        internal static IEnumerable<CommandConfig> GetRegisteredCommandConfigs() {
            return Commands.Values.Select(registration => registration.Config);
        }        

        internal static void UnknownGlobalCommand(NetworkConnection caller, string commandName, string[] args) {
            SendCommandError(caller, $"Command '{commandName}' has no server handler.");
        }

        internal static void SendCommandError(NetworkConnection conn, string message) {
            SendSystemMessage(conn, $"Command Error: {message}");
        }

        internal static void SendSystemMessage(NetworkConnection conn, string message) {
            InstanceFinder.ServerManager.Broadcast(conn, new SystemMessageBroadcast(message, SystemMessageSource.CustomMessage));
        }

        readonly struct CommandRegistration {
            public readonly CommandConfig Config;
            public readonly CommandHandler Handler;
            public readonly CommandGetter Getter;

            public CommandRegistration(CommandConfig config, CommandHandler handler, CommandGetter getter) {
                Config = config;
                Handler = handler;
                Getter = getter;
            }
        }
    }
}
