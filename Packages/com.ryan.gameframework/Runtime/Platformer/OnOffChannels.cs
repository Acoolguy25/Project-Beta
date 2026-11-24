using System;
using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Client-side state of on/off switch channels (Mario's ON/OFF blocks). Every channel starts
    /// unflipped; an <see cref="OnOffSwitch"/> flips its channel and the <see cref="OnOffBlock"/>s on it
    /// swap which colour is solid. The state is the local player's own, like crumbling blocks, so one
    /// racer cannot pull the floor out from under another. Switches reset every channel when the local
    /// player is teleported (a respawn), so the puzzle always restarts from its first state.
    /// </summary>
    public static class OnOffChannels {
        static readonly HashSet<int> Flipped = new();

        /// <summary>Raised when a channel flips; the argument is the channel.</summary>
        public static event Action<int> Changed;
        /// <summary>Raised when every channel goes back to unflipped.</summary>
        public static event Action Reset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            Flipped.Clear();
            Changed = null;
            Reset = null;
        }

        public static bool IsFlipped(int channel) => Flipped.Contains(channel);

        public static void Flip(int channel) {
            if (!Flipped.Remove(channel)) Flipped.Add(channel);
            Changed?.Invoke(channel);
        }

        public static void ResetAll() {
            if (Flipped.Count == 0) return;
            Flipped.Clear();
            Reset?.Invoke();
        }
    }
}
