using FishNet.Connection;
using RyanAssets.Characters.Server;
using RyanAssets.Commands.Server;
using RyanAssets.Commands.Shared;
using RyanAssets.Shared.Global;
using System.Globalization;
using UnityEngine;
using Universes.UniverseData.murder_mystery;

namespace Universes.murder_mystery.Server
{
    public class MM_Commands : MonoBehaviour {
        static readonly CommandConfig[] commands =
        {
            new()
            {
               commandType = "NPC",
               commandName = "NPCWalkSpeedMultiplier",
               description = "Sets the NPC Walk Speed",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Speed",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 100f
                    }
                }
            },
            new()
            {
               commandType = "NPC",
               commandName = "NPCFleeSpeedMultiplier",
               description = "Sets the NPC Flee Speed",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Speed",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 100f
                    }
                }
            },
            new()
            {
               commandType = "NPC",
               commandName = "NPCAttackSpeedMultiplier",
               description = "Sets the NPC Attack Speed",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Speed",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 100f
                    }
                }
            },
            new()
            {
               commandType = "NPC",
               commandName = "NPCSpawnMultiplier",
               description = "Sets the NPC Spawn Multiplier",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Multiplier",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 100f
                    }
                }
            },
            new()
            {
               commandType = "Murderer",
               commandName = "MurdererEvery",
               description = "Classic: one murderer per this many characters (players + NPCs)",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Characters",
                        type = CommandArgumentType.Int,
                        min = 2f,
                        max = 100f
                    }
                }
            },
            new()
            {
               commandType = "Sheriff",
               commandName = "SheriffEvery",
               description = "Classic: one sheriff per this many characters (players + NPCs)",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Characters",
                        type = CommandArgumentType.Int,
                        min = 2f,
                        max = 100f
                    }
                }
            },
            new()
            {
               commandType = "Roles",
               commandName = "RolePlayerWeight",
               description = "Classic: a player's weight in the murderer and sheriff draws (an NPC weighs 1)",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Weight",
                        type = CommandArgumentType.Float,
                        min = 0.1f,
                        max = 20f
                    }
                }
            },
            new()
            {
               commandType = "Roles",
               commandName = "RoleLuckPerRound",
               description = "Classic: weight a player gains for a role each round they miss it",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Luck",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 20f
                    }
                }
            }
        };
        static readonly ServerCommandService.CommandHandler[] commandActions =
        {
            (NetworkConnection caller, string commandName, string[] args) => {
                LocalNPC.WalkSpeedMultiplier = float.Parse(args[0], CultureInfo.InvariantCulture);
                MM_ServerRunner.RefreshNPCSpeeds();
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                LocalNPC.FleeSpeedMultiplier = float.Parse(args[0], CultureInfo.InvariantCulture);
                MM_ServerRunner.RefreshNPCSpeeds();
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                LocalNPC.AttackSpeedMultiplier = float.Parse(args[0], CultureInfo.InvariantCulture);
                MM_ServerRunner.RefreshNPCSpeeds();
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                MM_ServerRunner.SpawnMultiplier = float.Parse(args[0], CultureInfo.InvariantCulture);
            },
            (NetworkConnection caller, string commandName, string[] args) =>
                MM_ServerRunner.TuneClassic(t => t.charactersPerMurderer = Mathf.Max(2, int.Parse(args[0], CultureInfo.InvariantCulture))),
            (NetworkConnection caller, string commandName, string[] args) =>
                MM_ServerRunner.TuneClassic(t => t.charactersPerSheriff = Mathf.Max(2, int.Parse(args[0], CultureInfo.InvariantCulture))),
            (NetworkConnection caller, string commandName, string[] args) =>
                MM_ServerRunner.TuneClassic(t => t.playerRoleWeight = Mathf.Max(0.1f, float.Parse(args[0], CultureInfo.InvariantCulture))),
            (NetworkConnection caller, string commandName, string[] args) =>
                MM_ServerRunner.TuneClassic(t => t.luckPerRound = Mathf.Max(0f, float.Parse(args[0], CultureInfo.InvariantCulture))),
        };
        static readonly ServerCommandService.CommandGetter[] commandGetters = {
            (_, _) => LocalNPC.WalkSpeedMultiplier,
            (_, _) => LocalNPC.FleeSpeedMultiplier,
            (_, _) => LocalNPC.AttackSpeedMultiplier,
            (_, _) => MM_ServerRunner.SpawnMultiplier,
            (_, _) => MM_ServerRunner.ClassicTuning?.charactersPerMurderer,
            (_, _) => MM_ServerRunner.ClassicTuning?.charactersPerSheriff,
            (_, _) => MM_ServerRunner.ClassicTuning?.playerRoleWeight,
            (_, _) => MM_ServerRunner.ClassicTuning?.luckPerRound
        };

        void Start() {
            for (int i = 0; i < commands.Length; i++)
            {
                ServerCommandService.RegisterCommand(commands[i], commandActions[i], commandGetters[i]);
            }
        }
    }
}
