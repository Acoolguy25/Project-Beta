using System.Collections.Generic;
using RyanAssets.Characters.Shared;

namespace RyanAssets.Rounds {
    /// <summary>
    /// Finds a player's character by FishNet client id on any peer. Game broadcasts name players by
    /// client id and every peer resolves them here.
    /// </summary>
    /// <remarks>
    /// Characters are not in <c>EntityBase.All</c> (they never chain to its OnStartNetwork), so this
    /// looks in <see cref="LocalCharacter.Characters"/>, which both peers fill as ownership is set, and
    /// then in the team registry (<see cref="GameCharacter.TeamToCharacter"/>), which holds every
    /// living character a client has spawned.
    /// </remarks>
    public static class CharacterLookup {
        public static LocalCharacter ByClientId(int clientId) {
            if (clientId < 0) return null;
            foreach (KeyValuePair<FishNet.Connection.NetworkConnection, LocalCharacter> pair in LocalCharacter.Characters)
                if (pair.Key != null && pair.Key.ClientId == clientId && pair.Value != null && pair.Value.OwnerId == clientId)
                    return pair.Value;
            foreach (List<GameCharacter> team in GameCharacter.TeamToCharacter.Values)
                foreach (GameCharacter character in team)
                    if (character is LocalCharacter local && local != null && local.OwnerId == clientId)
                        return local;
            return null;
        }
    }
}
