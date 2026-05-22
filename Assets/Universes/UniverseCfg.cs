namespace Universes {
    public enum UniverseAccess: sbyte{
        Private = 0,
        Protected = 1,
        Public = 2
    }
    public struct UniverseStruct {
        public string id;
        public string title;
        public string description;
        public string creator_playerid;
        public UniverseAccess access;
    };
    public static class UniverseCfg {
        public readonly static UniverseStruct[] ActiveUniverses = {
            new(){
                id = "empty_baseplate",
                title = "Empty Baseplate",
                description = "This is your very first creation. Check it out, then make it your own with Ryan's help!",
                creator_playerid = "Uvr2xiFAyUZJDybNdBEKcPOsMvjR",
                access = UniverseAccess.Public
            }
        };
    }
}