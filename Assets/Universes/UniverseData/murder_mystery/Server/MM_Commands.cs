using FishNet.Connection;
using RyanAssets.Characters.Server;
using RyanAssets.Commands.Server;
using RyanAssets.Commands.Shared;
using RyanAssets.Shared.Player;
using System.Collections;
using UnityEngine;

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