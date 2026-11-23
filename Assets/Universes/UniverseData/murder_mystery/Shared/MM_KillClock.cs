using System;
using System.Collections.Generic;

namespace Universes.UniverseData.murder_mystery {
    /// <summary>
    /// When each murderer may kill again: a grace period at the start of the round, then a cooldown
    /// after each kill whose length the caller picks (players and NPCs differ). Pure rules with no
    /// Unity or network state, keyed by any stable per-character id and driven by the caller's clock
    /// (seconds), so the server owns the timing and EditMode tests can drive it directly.
    /// </summary>
    public sealed class MM_KillClock {
        readonly Dictionary<int, float> personalReadyAt = new();
        float sharedReadyAt = float.PositiveInfinity;

        public float GraceSeconds { get; }

        /// <summary>True once <see cref="BeginHunt"/> ran and until <see cref="Clear"/>.</summary>
        public bool Running => !float.IsPositiveInfinity(sharedReadyAt);

        public MM_KillClock(float graceSeconds) {
            if (graceSeconds < 0f || float.IsNaN(graceSeconds))
                throw new ArgumentOutOfRangeException(nameof(graceSeconds));
            GraceSeconds = graceSeconds;
        }

        /// <summary>The round started at <paramref name="now"/>: nobody may kill until the grace period is over.</summary>
        public void BeginHunt(float now) {
            personalReadyAt.Clear();
            sharedReadyAt = now + GraceSeconds;
        }

        /// <summary>Lifts every wait from <paramref name="now"/> on (the endgame frenzy when no player is left).</summary>
        public void ReleaseAll(float now) {
            if (!Running)
                return;
            personalReadyAt.Clear();
            sharedReadyAt = Math.Min(sharedReadyAt, now);
        }

        /// <summary><paramref name="murdererId"/> killed someone at <paramref name="now"/> and waits <paramref name="cooldownSeconds"/>.</summary>
        public void RecordKill(int murdererId, float now, float cooldownSeconds) {
            if (cooldownSeconds < 0f || float.IsNaN(cooldownSeconds))
                throw new ArgumentOutOfRangeException(nameof(cooldownSeconds));
            float readyAt = now + cooldownSeconds;
            personalReadyAt[murdererId] = personalReadyAt.TryGetValue(murdererId, out float existing)
                ? Math.Max(existing, readyAt)
                : readyAt;
        }

        /// <summary>Seconds until <paramref name="murdererId"/> may kill; 0 when they may now. Infinite before the round.</summary>
        public float SecondsUntilReady(int murdererId, float now) {
            float readyAt = sharedReadyAt;
            if (personalReadyAt.TryGetValue(murdererId, out float personal))
                readyAt = Math.Max(readyAt, personal);
            return Math.Max(0f, readyAt - now);
        }

        public bool CanKill(int murdererId, float now) => SecondsUntilReady(murdererId, now) <= 0f;

        /// <summary>The round is over: nobody may kill until the next <see cref="BeginHunt"/>.</summary>
        public void Clear() {
            personalReadyAt.Clear();
            sharedReadyAt = float.PositiveInfinity;
        }
    }
}
