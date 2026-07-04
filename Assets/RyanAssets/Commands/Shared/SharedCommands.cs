namespace RyanAssets.Commands.Shared {
    public static class SharedCommands {
        public static readonly CommandConfig[] AllGameCommands = {
            new() {
                commandName = "help",
                description = "Lists available commands.",
                arguments = new CommandArgumentConfig[0]
            },
            new() {
                commandName = "player_setwalkspeed",
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
                commandName = "player_setsprintspeed",
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
                commandName = "player_kill",
                description = "Kill player character.",
                arguments = new CommandArgumentConfig[] {
                    new() {
                        name = "players",
                        type = CommandArgumentType.Players
                    }
                }
            },
            new() {
                commandName = "player_respawn",
                description = "Respawn player character.",
                arguments = new CommandArgumentConfig[] {
                    new() {
                        name = "players",
                        type = CommandArgumentType.Players
                    }
                }
            },
            new() {
                commandName = "player_kick",
                description = "Kick player.",
                arguments = new CommandArgumentConfig[] {
                    new() {
                        name = "players",
                        type = CommandArgumentType.Players
                    }
                }
            },
            new() {
                commandName = "server_shutdown",
                description = "Closes current server.",
                arguments = new CommandArgumentConfig[0]
            }
        };
    }
}
