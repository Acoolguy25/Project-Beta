using System;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>How strong each daily twist is (<see cref="CTF_Daily"/> picks which one plays today).</summary>
    [Serializable]
    public sealed class CTF_DailyTuning {
        [Tooltip("Play the twists at all. Off: every day plays the plain rules.")]
        public bool enabled = true;
        [Tooltip("Flag Frenzy: grab and capture XP multiplier. Loaned-gun days: XP multiplier for eliminations with the loaned gun.")]
        [Range(1, 5)] public int xpMultiplier = 2;
        [Tooltip("Power Surge: power-up respawn time multiplier (0.5 = twice as fast).")]
        [Range(0.1f, 1f)] public float powerSurgeRespawnScale = 0.5f;
        [Tooltip("Tough Bots: tiers the bots move up on top of the automatic choice.")]
        [Range(0, 3)] public int toughBotTiers = 1;
        [Tooltip("Tough Bots: extra XP on the match result, as a share of the result line's XP.")]
        [Range(0f, 1f)] public float toughBotsResultBonus = 0.25f;
        [Tooltip("QA: play the twist (and the daily challenges) of this many days from today. 0 in production.")]
        public int previewDayOffset;

        /// <summary>Today's twist on the server's clock, shifted by <see cref="previewDayOffset"/>.</summary>
        public CTF_DailyTwist Today(DateTime utcNow) => enabled ? CTF_Daily.Today(Now(utcNow)) : CTF_DailyTwist.None;

        /// <summary>The clock the twist and the kit's daily challenges both read.</summary>
        public DateTime Now(DateTime utcNow) => utcNow.AddDays(previewDayOffset);
    }
}
