using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>
    /// What a bot may know about the match (implemented by <see cref="CTF_ServerRunner"/>): the phase,
    /// the flags, the stands, who is on the field and which power-ups are up. The same facts a player
    /// sees on the HUD; bots get no hidden information.
    /// </summary>
    public interface CTF_IBotWorld {
        /// <summary>Bots may move (countdown over).</summary>
        bool Moving { get; }
        /// <summary>Bots may shoot (match running).</summary>
        bool Fighting { get; }
        CTF_Map Map { get; }
        CTF_FlagState FlagState(CTF_Team flag);
        Vector3 FlagPosition(CTF_Team flag);
        /// <summary>Who carries <paramref name="flag"/>, or null.</summary>
        CTF_Actor CarrierOf(CTF_Team flag);
        /// <summary>Everyone in the match, players and bots, alive or not.</summary>
        IReadOnlyList<CTF_Actor> Actors { get; }
        bool PowerUpReady(int index);
        /// <summary>Players' sprint speed (for judging how fast a target crosses the line of fire).</summary>
        float SprintSpeed { get; }
        /// <summary>How fast <paramref name="actor"/> moves right now (carrying slows, a speed power-up hurries).</summary>
        float SpeedMultiplier(CTF_Actor actor);
    }
}
