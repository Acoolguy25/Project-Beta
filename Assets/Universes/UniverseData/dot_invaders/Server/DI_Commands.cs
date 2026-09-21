#if UNITY_SERVER
using System;
using System.Globalization;
using FishNet.Connection;
using RyanAssets.Commands.Server;
using RyanAssets.Commands.Shared;
using RyanAssets.DataService;
using System.Collections.Generic;
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
            FloatCommand("MoveSpeed", "Multiplies the default unit movement speed.", "Multiplier",
                DI_Rules.MinMoveSpeedMultiplier, DI_Rules.MaxMoveSpeedMultiplier),
            new CommandConfig {
                commandType = "Dot Invaders",
                commandName = "SuperProduction",
                description = "Sets super base peak troops/second and uninterrupted seconds to reach it.",
                arguments = new[] {
                    new CommandArgumentConfig { name = "TroopsPerSecond", type = CommandArgumentType.Float, min = 0.01f, max = 500f },
                    new CommandArgumentConfig { name = "SpeedupSeconds", type = CommandArgumentType.Float, min = 0.01f, max = 600f }
                }
            },
            IntCommand("SuperMaxCapacity", "Sets the maximum units a super base produces automatically.", "Units", 1, 10000),
            FloatCommand("ProductionSpeed", "Sets how many units a normal base trains per second.", "TroopsPerSecond", 0.01f, 100f),
            FloatCommand("SendInterval", "Multiplies the default delay between units leaving a player base.", "Multiplier",
                DI_Rules.MinSendIntervalMultiplier, DI_Rules.MaxSendIntervalMultiplier),
            FloatCommand("SpeedBaseBonus", "Sets the move speed multiplier each captured speed base adds.", "Multiplier", 0f, 5f),
            FloatCommand("NPCAggression", "Sets how little NPC teams hold back before committing an attack.", "Percentage", 0f, 100f),
            IntCommand("MatchTime", "Sets the seconds left in the running match.", "Seconds", 1, 7200),
            new CommandConfig {
                commandType = "Dot Invaders",
                commandName = "DotInvadersSettings",
                description = "Reports every Dot Invaders setting to you only.",
                arguments = Array.Empty<CommandArgumentConfig>()
            },
            new CommandConfig {
                commandType = "Dot Invaders",
                commandName = "RerollBoard",
                description = "Regenerates the board and restarts the running match.",
                arguments = Array.Empty<CommandArgumentConfig>()
            },
            PlayerMultiplierCommand("TroopDamage", "Privately sets a player's troop damage multiplier for this match."),
            PlayerMultiplierCommand("PlayerProduction", "Privately sets a player's production multiplier for this match.")
        };

        static DI_ServerRunner runner;

        internal static void Register(DI_ServerRunner serverRunner) {
            runner = serverRunner;
            ServerCommandService.RegisterCommand(Commands[0], SetNPCIntelligence, (_, _) => runner.NPCIntelligence);
            ServerCommandService.RegisterCommand(Commands[1], SetTurretRange, (_, _) => runner.TurretRange);
            ServerCommandService.RegisterCommand(Commands[2], SetTurretFireRate, (_, _) => runner.TurretFireRate);
            ServerCommandService.RegisterCommand(Commands[3], SetMaxCapacity, (_, _) => runner.MaxCapacity);
            ServerCommandService.RegisterCommand(Commands[4], SetMoveSpeed, (_, _) => runner.MoveSpeedMultiplier);
            ServerCommandService.RegisterCommand(Commands[5], SetSuperProduction);
            ServerCommandService.RegisterCommand(Commands[6], SetSuperMaxCapacity, (_, _) => runner.SuperMaxCapacity);
            ServerCommandService.RegisterCommand(Commands[7], SetProductionSpeed, (_, _) => runner.ProductionSpeed);
            ServerCommandService.RegisterCommand(Commands[8], SetSendInterval, (_, _) => runner.SendIntervalMultiplier);
            ServerCommandService.RegisterCommand(Commands[9], SetSpeedBaseBonus, (_, _) => runner.SpeedBaseBonus);
            ServerCommandService.RegisterCommand(Commands[10], SetNPCAggression, (_, _) => runner.NpcAggression);
            ServerCommandService.RegisterCommand(Commands[11], SetMatchTime, (_, _) => runner.SecondsRemaining);
            ServerCommandService.RegisterCommand(Commands[12], ReportSettings);
            ServerCommandService.RegisterCommand(Commands[13], RerollBoard);
            ServerCommandService.RegisterCommand(Commands[14], SetPlayerBuff, (caller, args) => ReadPlayerBuff(caller, args, true));
            ServerCommandService.RegisterCommand(Commands[15], SetPlayerBuff, (caller, args) => ReadPlayerBuff(caller, args, false));
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
            runner.SetMoveSpeedMultiplier(value);
            AnnounceMultiplier(commandName, runner.MoveSpeedMultiplier, runner.MoveSpeed, "board units/s");
        }

        static void SetSuperProduction(NetworkConnection caller, string commandName, string[] args) {
            runner.SetSuperProduction(ParseFloat(args[0]), ParseFloat(args[1]));
            ServerChat.SendSystemMessage(new SystemMessageBroadcast(
                FormattableString.Invariant($"SuperProduction updated to {runner.SuperProductionSpeed} troops/s with {runner.SuperSpeedupSeconds}s speedup for everyone."),
                SystemMessageSource.CustomMessage));
        }

        static void SetProductionSpeed(NetworkConnection caller, string commandName, string[] args) {
            float value = ParseFloat(args[0]);
            runner.SetProductionSpeed(value);
            Announce(commandName, value);
        }

        static void SetSendInterval(NetworkConnection caller, string commandName, string[] args) {
            float value = ParseFloat(args[0]);
            runner.SetSendIntervalMultiplier(value);
            AnnounceMultiplier(commandName, runner.SendIntervalMultiplier, runner.SendInterval, "seconds");
        }

        static void SetSpeedBaseBonus(NetworkConnection caller, string commandName, string[] args) {
            float value = ParseFloat(args[0]);
            runner.SetSpeedBaseBonus(value);
            Announce(commandName, value);
        }

        static void SetNPCAggression(NetworkConnection caller, string commandName, string[] args) {
            float value = ParseFloat(args[0]);
            runner.SetNpcAggression(value);
            Announce(commandName, value);
        }

        static void SetMatchTime(NetworkConnection caller, string commandName, string[] args) {
            int value = int.Parse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
            runner.SetSecondsRemaining(value);
            Announce(commandName, value);
        }

        static void ReportSettings(NetworkConnection caller, string commandName, string[] args) {
            // One interpolated string: concatenation would drop the FormattableString.
            string report = FormattableString.Invariant(
                $@"Dot Invaders settings
NPCIntelligence {runner.NPCIntelligence} | NPCAggression {runner.NpcAggression}
MoveSpeed {runner.MoveSpeedMultiplier}x ({runner.MoveSpeed} board units/s) | SpeedBaseBonus {runner.SpeedBaseBonus}
SendInterval {runner.SendIntervalMultiplier}x ({runner.SendInterval} seconds)
ProductionSpeed {runner.ProductionSpeed} | MaxCapacity {runner.MaxCapacity}
SuperProduction {runner.SuperProductionSpeed}/s over {runner.SuperSpeedupSeconds}s | SuperMaxCapacity {runner.SuperMaxCapacity}
TurretRange {runner.TurretRange} | TurretFireRate {runner.TurretFireRate}
MatchTime {runner.SecondsRemaining}s remaining");
            ServerChat.SendSystemMessage(caller, new SystemMessageBroadcast(report, SystemMessageSource.CustomMessage));
        }

        static void RerollBoard(NetworkConnection caller, string commandName, string[] args) {
            if (runner.RestartMatch()) {
                ServerChat.SendSystemMessage(new SystemMessageBroadcast(
                    "The board was rerolled and the match restarted.", SystemMessageSource.CustomMessage));
                return;
            }
            ServerChat.SendSystemMessage(caller, new SystemMessageBroadcast(
                "RerollBoard only works while a match is running.", SystemMessageSource.CustomMessage));
        }

        static void SetSuperMaxCapacity(NetworkConnection caller, string commandName, string[] args) {
            int value = int.Parse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
            runner.SetSuperMaxCapacity(value);
            Announce(commandName, value);
        }

        static float ParseFloat(string value) {
            return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static CommandConfig PlayerMultiplierCommand(string name, string description) {
            return new CommandConfig {
                commandType = "Dot Invaders",
                commandName = name,
                description = description + " 1 resets it; omit the multiplier to inspect privately.",
                arguments = new[] {
                    new CommandArgumentConfig { name = "Players", type = CommandArgumentType.Players },
                    new CommandArgumentConfig { name = "Multiplier", type = CommandArgumentType.Float, min = 1f, max = 10f }
                }
            };
        }

        static object ReadPlayerBuff(NetworkConnection caller, string[] args, bool damage) {
            var values = new List<string>();
            foreach (NetworkConnection target in CommandVerification.GetPlayersFromArgument(
                args.Length == 0 ? "me" : args[0], PlayerData.Players, caller)) {
                float value = damage ? runner.GetPlayerDamageMultiplier(target.ClientId)
                    : runner.GetPlayerProductionMultiplier(target.ClientId);
                values.Add(FormattableString.Invariant($"{PlayerData.GetPlayerName(target)}: {value}x"));
            }
            return string.Join(", ", values);
        }

        static void SetPlayerBuff(NetworkConnection caller, string commandName, string[] args) {
            bool damage = string.Equals(commandName, "TroopDamage", StringComparison.OrdinalIgnoreCase);
            float value = ParseFloat(args[1]);
            int applied = 0;
            foreach (NetworkConnection target in CommandVerification.GetPlayersFromArgument(args[0], PlayerData.Players, caller))
                if (runner.SetPlayerMultiplier(target.ClientId, value, damage))
                    applied++;
            ServerChat.SendSystemMessage(caller, new SystemMessageBroadcast(
                FormattableString.Invariant($"{commandName}: {value}x applied to {applied} active player(s), for this match only."),
                SystemMessageSource.CustomMessage));
        }

        static void AnnounceMultiplier(string commandName, float multiplier, float effective, string unit) {
            ServerChat.SendSystemMessage(new SystemMessageBroadcast(
                FormattableString.Invariant($"{commandName} updated to {multiplier}x ({effective} {unit}) for everyone."),
                SystemMessageSource.CustomMessage));
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
                description = description + " Omit the value to read the current setting.",
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
