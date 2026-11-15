using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Component;

namespace RyanAssets.Rounds {
    /// <summary>
    /// Finds a player's character by FishNet client id on any peer. The server keeps
    /// <see cref="LocalCharacter.Characters"/>, but clients only know other players' characters as
    /// spawned entities, so game broadcasts name players by client id and clients resolve them here.
    /// </summary>
    public static class CharacterLookup {
        public static LocalCharacter ByClientId(int clientId) {
            if (clientId < 0) return null;
            foreach (EntityBase entity in EntityBase.All)
                if (entity is LocalCharacter character && character != null && character.OwnerId == clientId) return character;
            return null;
        }
    }
}
