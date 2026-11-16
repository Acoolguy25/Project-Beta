using System;
using System.Collections.Generic;

namespace Universes.UniverseData.murder_mystery {
    /// <summary>
    /// When each murderer may kill again: a grace period at the start of the hunt, a cooldown after
    /// each kill, and a short shared cooldown after every meeting. Pure rules with no Unity or network
    /// state, keyed by any stable per-character id and driven by the caller's clock (seconds), so the
    /// server owns the timing and EditMode tests can drive it directly.
    /// </summary>
    public sealed class MM_KillClock {
        readonly Dictionary<int, float> personalReadyAt = new();
        float sharedReadyAt = float.PositiveInfinity;

        public float GraceSeconds { get; }
        public float KillCooldownSeconds { get; }
        public float PostMeetingCooldownSeconds { get; }

        /// <summary>True once <see cref="BeginHunt"/> ran and until <see cref="Clear"/>.</summary>
        public bool Running => !float.IsPositiveInfinity(sharedReadyAt);

        public MM_KillClock(float graceSeconds, float killCooldownSeconds, float postMeetingCooldownSeconds) {
            if (graceSeconds < 0f || float.IsNaN(graceSeconds))
                throw new ArgumentOutOfRangeException(nameof(graceSeconds));
            if (killCooldownSeconds < 0f || float.IsNaN(killCooldownSeconds))
                throw new ArgumentOutOfRangeException(nameof(killCooldownSeconds));
            if (postMeetingCooldownSeconds < 0f || float.IsNaN(postMeetingCooldownSeconds))
                throw new ArgumentOutOfRangeException(nameof(postMeetingCooldownSeconds));
            GraceSeconds = graceSeconds;
            KillCooldownSeconds = killCooldownSeconds;
            PostMeetingCooldownSeconds = postMeetingCooldownSeconds;
        }

        public MM_KillClock(MM_ClassicTuning tuning)
            : this(tuning.murdererGraceSeconds, tuning.killCooldownSeconds, tuning.postMeetingKillCooldownSeconds) { }

        /// <summary>The hunt started at <paramref name="now"/>: nobody may kill until the grace period is over.</summary>
        public void BeginHunt(float now) {
            personalReadyAt.Clear();
            sharedReadyAt = now + GraceSeconds;
        }

        /// <summary>A meeting ended at <paramref name="now"/>: every murderer waits at least the post-meeting cooldown.</summary>
        public void EndMeeting(float now) {
            if (Running)
                sharedReadyAt = Math.Max(sharedReadyAt, now + PostMeetingCooldownSeconds);
        }

        /// <summary><paramref name="murdererId"/> killed someone at <paramref name="now"/>.</summary>
        public void RecordKill(int murdererId, float now) {
            float readyAt = now + KillCooldownSeconds;
            personalReadyAt[murdererId] = personalReadyAt.TryGetValue(murdererId, out float existing)
                ? Math.Max(existing, readyAt)
                : readyAt;
        }

        /// <summary>Seconds until <paramref name="murdererId"/> may kill; 0 when they may now. Infinite before the hunt.</summary>
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
