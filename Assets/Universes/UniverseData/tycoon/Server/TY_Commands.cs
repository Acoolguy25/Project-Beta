using System;
using System.Globalization;
using FishNet.Connection;
using RyanAssets.Commands.Server;
using RyanAssets.Commands.Shared;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;

namespace Universes.UniverseData.tycoon.Server {
    /// <summary>
    /// Brick Tycoon's rush-hour testing and tuning commands, StartRushHour and RushMultiplier (com.ryan.commands).
    /// They are registered only when the runner's rushCommands flag is on (test servers), because the command service
    /// has no permissions and rush earnings are saved. The tycoon client has no chat command box
    /// (ClientCommandController), so a tester sends them as a CommandBroadcast from an Editor client.
    /// There is deliberately no cash or token command.
    /// </summary>
    static class TY_Commands {
        const string Type = "Brick Tycoon";
        const float MaxRushMultiplier = 10f;

        static readonly CommandConfig StartRush = new() {
            commandType = Type,
            commandName = "StartRushHour",
            description = "Starts a rush hour now for every tycoon.",
            arguments = Array.Empty<CommandArgumentConfig>(),
        };

        static readonly CommandConfig RushMultiplier = new() {
            commandType = Type,
            commandName = "RushMultiplier",
            description = "Sets the income multiplier of rush hours. Omit the value to read it.",
            arguments = new[] {
                new CommandArgumentConfig { name = "Multiplier", type = CommandArgumentType.Float, min = 1f, max = MaxRushMultiplier },
            },
        };

        static TY_ServerRunner runner;

        internal static void Register(TY_ServerRunner serverRunner) {
            runner = serverRunner;
            ServerCommandService.RegisterCommand(StartRush, OnStartRush);
            ServerCommandService.RegisterCommand(RushMultiplier, OnRushMultiplier, (_, _) => runner.RushMultiplier);
        }

        internal static void Unregister(TY_ServerRunner serverRunner) {
            // A newer runner may already have registered its own handlers.
            if (runner != serverRunner) return;
            ServerCommandService.UnregisterCommand(StartRush.commandName);
            ServerCommandService.UnregisterCommand(RushMultiplier.commandName);
            runner = null;
        }

        static void OnStartRush(NetworkConnection caller, string commandName, string[] args) {
            if (runner.RushHourActive) {
                Reply(caller, "A rush hour is already running.");
                return;
            }
            runner.StartRushHour();
        }

        static void OnRushMultiplier(NetworkConnection caller, string commandName, string[] args) {
            runner.SetRushMultiplier(float.Parse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture));
            Reply(caller, FormattableString.Invariant($"Rush hours now pay x{runner.RushMultiplier:0.##}."));
        }

        static void Reply(NetworkConnection caller, string text) =>
            ServerChat.SendSystemMessage(caller, new SystemMessageBroadcast(text, SystemMessageSource.CustomMessage));
    }
}
