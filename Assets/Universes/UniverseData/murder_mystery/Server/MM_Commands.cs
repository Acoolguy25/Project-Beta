using FishNet.Connection;
using RyanAssets.Characters.Server;
using RyanAssets.Commands.Server;
using RyanAssets.Commands.Shared;
using RyanAssets.Shared.Global;
using System.Collections;
using UnityEngine;
using Universes.UniverseData.murder_mystery.Server;

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
               commandName = "MurdererBaseChance",
               description = "Sets the base chance for an NPC or player to become a Murderer",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Chance",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 1f
                    }
                }
            },
            new()
            {
               commandType = "Murderer",
               commandName = "MurdererMin",
               description = "Sets the minimum number of Murderers",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Count",
                        type = CommandArgumentType.Int,
                        min = 0f,
                        max = 100f
                    }
                }
            },
            new()
            {
               commandType = "Murderer",
               commandName = "MurdererMaxRatio",
               description = "Sets the maximum fraction of the total pool that can be Murderers",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Ratio",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 1f
                    }
                }
            },
            new()
            {
               commandType = "Sheriff",
               commandName = "SheriffBaseChance",
               description = "Sets the base chance for an eligible player to become a Sheriff",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Chance",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 1f
                    }
                }
            },
            new()
            {
               commandType = "Sheriff",
               commandName = "SheriffMin",
               description = "Sets the minimum number of Sheriffs",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Count",
                        type = CommandArgumentType.Int,
                        min = 0f,
                        max = 100f
                    }
                }
            },
            new()
            {
               commandType = "Sheriff",
               commandName = "SheriffMaxRatio",
               description = "Sets the maximum fraction of the player pool that can be Sheriffs",
               arguments = new[]
                {
                    new CommandArgumentConfig
                    {
                        name = "Ratio",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 1f
                    }
                }
            }
        };
        static readonly ServerCommandService.CommandHandler[] commandActions =
        {
            (NetworkConnection caller, string commandName, string[] args) => {
                LocalNPC.WalkSpeedMultiplier = (float)System.Convert.ChangeType(args[0], typeof(float));
                MM_ServerRunner.RefreshNPCSpeeds();
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                LocalNPC.FleeSpeedMultiplier = (float)System.Convert.ChangeType(args[0], typeof(float));
                MM_ServerRunner.RefreshNPCSpeeds();
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                LocalNPC.AttackSpeedMultiplier = (float)System.Convert.ChangeType(args[0], typeof(float));
                MM_ServerRunner.RefreshNPCSpeeds();
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                MM_ServerRunner.SpawnMultiplier = (float)System.Convert.ChangeType(args[0], typeof(float));
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                MM_Roles.murdererBaseChance = (float)System.Convert.ChangeType(args[0], typeof(float));
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                MM_Roles.minMurderers = (int)System.Convert.ChangeType(args[0], typeof(int));
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                MM_Roles.murdererMaxRatio = (float)System.Convert.ChangeType(args[0], typeof(float));
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                MM_Roles.sheriffBaseChance = (float)System.Convert.ChangeType(args[0], typeof(float));
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                MM_Roles.minSheriffs = (int)System.Convert.ChangeType(args[0], typeof(int));
            },
            (NetworkConnection caller, string commandName, string[] args) => {
                MM_Roles.sheriffMaxRatio = (float)System.Convert.ChangeType(args[0], typeof(float));
            }
        };
        void Start() {
            for (int i = 0; i < commands.Length; i++)
            {
                ServerCommandService.RegisterCommand(commands[i], commandActions[i]); 
            }
        }
    }
}
