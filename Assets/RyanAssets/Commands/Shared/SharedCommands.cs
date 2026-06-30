namespace RyanAssets.Commands.Shared {
    public static class SharedCommands {
        public static readonly CommandConfig[] AllGameCommands = {
            new() {
                commandName = "help",
                description = "Lists available commands.",
                arguments = new CommandArgumentConfig[0]
            },
            new() {
                commandName = "sayhi",
                description = "Sends a server-validated hello.",
                arguments = new CommandArgumentConfig[0]
            }
        };
    }
}
