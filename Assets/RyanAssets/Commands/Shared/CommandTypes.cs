using System;

namespace RyanAssets.Commands.Shared {
    public enum CommandArgumentType {
        Int,
        Float,
        Player,
        Players,
        String
    }

    [Serializable]
    public struct CommandArgumentConfig {
        public string name;
        public CommandArgumentType type;
        public float min;
        public float max;
        public string[] suggestions;
        public bool optional;
    }

    [Serializable]
    public struct CommandConfig {
        public string commandName;
        public string description;
        public CommandArgumentConfig[] arguments;
    }
}
