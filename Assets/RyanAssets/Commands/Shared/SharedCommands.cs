namespace RyanAssets.Commands.Shared {
    public static class SharedCommands {
        public static readonly CommandConfig[] AllGameCommands = {
            new() {
                commandType = "character",
                commandName = "walkspeed",
                supportsGetter = true,
                description = "Reads or sets player walkspeed. Omit the value to read; omit the player to read your own.",
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
                commandType = "character",
                commandName = "sprintspeed",
                supportsGetter = true,
                description = "Reads or sets player sprintspeed. Omit the value to read; omit the player to read your own.",
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
                commandType = "character",
                commandName = "maxstamina",
                supportsGetter = true,
                description = "Reads or sets player max stamina. Omit the value to read; omit the player to read your own.",
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
                commandType = "character",
                commandName = "staminaregen",
                supportsGetter = true,
                description = "Reads or sets player stamina regeneration. Omit the value to read; omit the player to read your own.",
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
                commandType = "character",
                commandName = "staminacooldown",
                supportsGetter = true,
                description = "Reads or sets player stamina regeneration cooldown. Omit the value to read; omit the player to read your own.",
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
                commandType = "character",
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
                commandType = "character",
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
