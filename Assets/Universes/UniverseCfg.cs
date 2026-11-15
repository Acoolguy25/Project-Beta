using UnityEngine;

namespace Universes {
    /// <summary>The game's universe list. Registered with UniverseRegistry (com.ryan.core) on load.</summary>
    public static class UniverseCfg {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        static void RegisterWithPackages() => UniverseRegistry.Register(ActiveUniverses);

        public readonly static UniverseStruct[] ActiveUniverses = {
            new(){
                id = "example",
                title = "Example Place",
                description = "This is an example!",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Private
            },
            new(){
                id = "empty_baseplate",
                title = "Empty Baseplate",
                description = "This is your very first creation. Check it out!",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Public
            },
            new(){
                id = "murder_mystery",
                title = "Murder In Plain Sight",
                description = "As the murderer, kill everyone else to win. As a sheriff, fend off the murderer. As an survivor, try to survive to win",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Public
            },
            new(){
                id = "dot_invaders",
                title = "Dot Invaders",
                description = "Grow your bases, send dot armies to neighboring bases, and conquer the board.",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Public,
                disableDefaultCommands = true
            },
            new(){
                id = "classic_horror",
                title = "Investigator",
                description = "A horror story where you investigate an unknown horror source",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Public
            },
            new(){
                id = "chatbot_service",
                title = "Chatbot Service",
                description = "A game where you manipulate the smartest artifical intelligence possible.",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Public
            },
            new(){
                id = "escape",
                title = "Escape",
                description = "Do you get virtigo? Escape the unvierse!",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Protected
            },
            new(){
                id = "war_valley",
                title = "War Valley",
                description = "A strategic battle for control of the territory.",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Protected
            },
        };
        public static UniverseStruct GetUniverseFromId(string id) => UniverseRegistry.GetUniverseFromId(id);
    }
}
