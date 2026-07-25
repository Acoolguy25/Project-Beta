namespace RyanAssets.Commands.Shared {
    public static class SharedCommands {
        public static readonly CommandConfig[] AllGameCommands = {
            new() {
                commandType = "environment",
                commandName = "help",
                description = "Lists available commands.",
                arguments = new CommandArgumentConfig[0]
            },
            new() {
                commandType = "player",
                commandName = "walkspeed",
                description = "Sets player walkspeed.",
                arguments = new CommandArgumentConfig[] {
                    new() {
                        name = "players",
                        type = CommandArgumentType.Players
                    },
                    new()
                    {
                        name = "speed",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 500f
                    }
                }
            },
            new() {
                commandType = "player",
                commandName = "sprintspeed",
                description = "Sets player sprintspeed.",
                arguments = new CommandArgumentConfig[] {
                    new() {
                        name = "players",
                        type = CommandArgumentType.Players
                    },
                    new()
                    {
                        name = "speed",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 500f
                    }
                }
            },
            new() {
                commandType = "player",
                commandName = "maxstamina",
                description = "Sets player max stamina.",
                arguments = new CommandArgumentConfig[] {
                    new() {
                        name = "players",
                        type = CommandArgumentType.Players
                    },
                    new()
                    {
                        name = "speed",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 1000f
                    }
                }
            },
            new() {
                commandType = "player",
                commandName = "staminaregen",
                description = "Sets player stamina regeneration.",
                arguments = new CommandArgumentConfig[] {
                    new() {
                        name = "players",
                        type = CommandArgumentType.Players
                    },
                    new()
                    {
                        name = "speed",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 1000f
                    }
                }
            },
            new() {
                commandType = "player",
                commandName = "staminacooldown",
                description = "Sets player stamina regeneration cooldown.",
                arguments = new CommandArgumentConfig[] {
                    new() {
                        name = "players",
                        type = CommandArgumentType.Players
                    },
                    new()
                    {
                        name = "speed",
                        type = CommandArgumentType.Float,
                        min = 0f,
                        max = 10f
                    }
                }
            },
            new() {
                commandType = "player",
                commandName = "kill",
                description = "Kill player character.",
                arguments = new CommandArgumentConfig[] {
                    new() {
                        name = "players",
                        type = CommandArgumentType.Players
                    }
                }
            },
            new() {
                commandType = "player",
                commandName = "respawn",
                description = "Respawn player character.",
                arguments = new CommandArgumentConfig[] {
                    new() {
                        name = "players",
                        type = CommandArgumentType.Players
                    }
                }
            },
            new() {
                commandType = "player",
                commandName = "kick",
                description = "Kick player.",
                arguments = new CommandArgumentConfig[] {
                    new() {
                        name = "players",
                        type = CommandArgumentType.Players
                    }
                }
            },
            new() {
                commandType = "server",
                commandName = "shutdown",
                description = "Closes current server.",
                arguments = new CommandArgumentConfig[0]
            },
            new() {
                commandType = "server",
                commandName = "restart",
                description = "Restarts current server.",
                arguments = new CommandArgumentConfig[0]
            }
        };
    }
}
