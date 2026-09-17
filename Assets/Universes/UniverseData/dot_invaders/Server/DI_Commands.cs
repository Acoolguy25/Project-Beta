#if UNITY_SERVER
using System;
using System.Globalization;
using FishNet.Connection;
using RyanAssets.Commands.Server;
using RyanAssets.Commands.Shared;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;

namespace Universes.UniverseData.dot_invaders {
    static class DI_Commands {
        static readonly CommandConfig[] Commands = {
            FloatCommand("NPCIntelligence", "Sets NPC intelligence as a percentage.", "Percentage", 0f, 100f),
            FloatCommand("TurretRange", "Sets turret range radius in board units.", "Radius", 0f, 500f),
            FloatCommand("TurretFireRate", "Sets the delay in seconds between turret shots.", "Seconds", 0.01f, 60f),
            IntCommand("MaxCapacity", "Sets the maximum units a base produces automatically.", "Units", 1, 10000),
            FloatCommand("MoveSpeed", "Sets unit movement speed in board units per second.", "Speed", 0.01f, 500f)
        };

        static DI_ServerRunner runner;

        internal static void Register(DI_ServerRunner serverRunner) {
            runner = serverRunner;
            ServerCommandService.RegisterCommand(Commands[0], SetNPCIntelligence);
            ServerCommandService.RegisterCommand(Commands[1], SetTurretRange);
            ServerCommandService.RegisterCommand(Commands[2], SetTurretFireRate);
            ServerCommandService.RegisterCommand(Commands[3], SetMaxCapacity);
            ServerCommandService.RegisterCommand(Commands[4], SetMoveSpeed);
        }

        internal static void Unregister() {
            foreach (CommandConfig command in Commands)
                ServerCommandService.UnregisterCommand(command.commandName);
            runner = null;
        }

        static void SetNPCIntelligence(NetworkConnection caller, string commandName, string[] args) {
            float value = ParseFloat(args[0]);
            runner.SetNPCIntelligence(value);
            Announce(commandName, value);
        }

        static void SetTurretRange(NetworkConnection caller, string commandName, string[] args) {
            float value = ParseFloat(args[0]);
            runner.SetTurretRange(value);
            Announce(commandName, value);
        }

        static void SetTurretFireRate(NetworkConnection caller, string commandName, string[] args) {
            float value = ParseFloat(args[0]);
            runner.SetTurretFireRate(value);
            Announce(commandName, value);
        }

        static void SetMaxCapacity(NetworkConnection caller, string commandName, string[] args) {
            int value = int.Parse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
            runner.SetMaxCapacity(value);
            Announce(commandName, value);
        }

        static void SetMoveSpeed(NetworkConnection caller, string commandName, string[] args) {
            float value = ParseFloat(args[0]);
            runner.SetMoveSpeed(value);
            Announce(commandName, value);
        }

        static float ParseFloat(string value) {
            return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static void Announce(string commandName, IFormattable value) {
            ServerChat.SendSystemMessage(new SystemMessageBroadcast(
                $"{commandName} updated to {value.ToString(null, CultureInfo.InvariantCulture)} for everyone.",
                SystemMessageSource.CustomMessage));
        }

        static CommandConfig FloatCommand(string name, string description, string argumentName, float min, float max) {
            return NumberCommand(name, description, argumentName, CommandArgumentType.Float, min, max);
        }

        static CommandConfig IntCommand(string name, string description, string argumentName, int min, int max) {
            return NumberCommand(name, description, argumentName, CommandArgumentType.Int, min, max);
        }

        static CommandConfig NumberCommand(string name, string description, string argumentName,
            CommandArgumentType type, float min, float max) {
            return new CommandConfig {
                commandType = "Dot Invaders",
                commandName = name,
                description = description,
                arguments = new[] {
                    new CommandArgumentConfig {
                        name = argumentName,
                        type = type,
                        min = min,
                        max = max
                    }
                }
            };
        }
    }
}
#endif
