using FishNet.Broadcast;

namespace Universes.UniverseData.war_valley.Shared {
    // The wire contract for doctrines and the campaign. Field names, order and types are part of the
    // contract: append new fields at the end of a struct and new broadcasts at the end of the file.

    /// <summary>A hand of doctrine cards dealt to one commander, who picks one.</summary>
    public struct WV_DoctrineOffer : IBroadcast {
        /// <summary>Echoed by the pick, so a pick for an outdated hand is ignored.</summary>
        public int offerId;
        /// <summary><see cref="WV_DoctrineId"/> values, one per card.</summary>
        public byte[] doctrines;
        /// <summary>The commander's current rank in each card's doctrine, parallel to <see cref="doctrines"/>.</summary>
        public byte[] ranks;
        /// <summary>Redraws left this round.</summary>
        public byte rerolls;
        /// <summary>Waves held when the hand was dealt (0 for the opening hand), for the panel's title.</summary>
        public int wavesHeld;
        /// <summary>Added in retention try 1 phase B: a line for the title ("Featured by today's front",
        /// "Catch-up hand 1 of 2"), or empty.</summary>
        public string note;
        /// <summary>Added in retention try 2: the card the day's front features, as its index plus one
        /// (0: none), so the card can carry a "Today's front" ribbon.</summary>
        public byte featuredSlot;
    }

    /// <summary>A commander's answer to a <see cref="WV_DoctrineOffer"/>: a card, or a redraw.</summary>
    public struct WV_DoctrinePickRequest : IBroadcast {
        public int offerId;
        /// <summary>Index into the offer's cards. Ignored for a redraw.</summary>
        public byte choice;
        public bool reroll;
    }

    /// <summary>
    /// A commander's doctrines this round, sent after every pick and at every round start. Also
    /// closes the offer panel when <see cref="pendingOfferId"/> is 0.
    /// </summary>
    public struct WV_DoctrineState : IBroadcast {
        /// <summary>Rank per <see cref="WV_DoctrineId"/>, indexed by its value.</summary>
        public byte[] ranks;
        /// <summary>The hand still waiting for a pick, or 0.</summary>
        public int pendingOfferId;
        /// <summary>The doctrine just picked, or 255 when this state does not follow a pick.</summary>
        public byte picked;
    }

    /// <summary>One commander's campaign: the round's tier, their stars and records, and their next goal.</summary>
    public struct WV_CampaignStatus : IBroadcast {
        /// <summary>A <see cref="WV_DifficultyTier"/> value: the tier being played (or last played).</summary>
        public byte difficulty;
        /// <summary>Best stars per tier, indexed by <see cref="WV_DifficultyTier"/>.</summary>
        public byte[] tierStars;
        /// <summary>One bit per tier this commander has unlocked (<see cref="WV_Campaign.UnlockedMask"/>).</summary>
        public byte unlockedTiers;
        public int bestWave;
        public int bestEndlessWave;
        public int victories;
        /// <summary>The next thing to aim for, in words.</summary>
        public string nextGoal;
        /// <summary>The tier's display name, as the server's tuning has it.</summary>
        public string difficultyName;
        /// <summary>Added in retention try 1 phase B: the day's <see cref="WV_Front"/>.</summary>
        public byte front;
        /// <summary>The day's front in words ("Today: Iron Storm - enemies +15% health, +25% XP").</summary>
        public string frontText;
    }

    // ---- added in retention try 2

    /// <summary>
    /// The round's tier ballot, sent to every commander just before the vote opens, so War Valley's own
    /// tier panel can draw the options (the shared vote carries the same options in the same order and
    /// counts the votes). The panel opens only once this has arrived, so it never opens empty.
    /// </summary>
    public struct WV_TierBallot : IBroadcast {
        /// <summary>Increases with every ballot, so a panel can tell a new vote from the last one.</summary>
        public int ballotId;
        /// <summary><see cref="WV_DifficultyTier"/> values, in the vote's option order.</summary>
        public byte[] tiers;
        /// <summary>Each tier's display name, parallel to <see cref="tiers"/>.</summary>
        public string[] names;
        /// <summary>Each tier's one-line description, parallel to <see cref="tiers"/>.</summary>
        public string[] descriptions;
        /// <summary>Each tier's extra XP in percent (30 = pays 30% more), parallel to <see cref="tiers"/>.</summary>
        public short[] xpBonusPercent;
        /// <summary>How long the vote runs, in seconds, counted by the client from when this arrives.</summary>
        public float seconds;
    }

    /// <summary>
    /// A doctrine took effect: whose it is, which, and the things on the field it changed (network object
    /// ids), so every client can show the pick land on that commander's army, buildings or flag.
    /// </summary>
    public struct WV_DoctrineEffect : IBroadcast {
        public int clientId;
        /// <summary>A <see cref="WV_DoctrineId"/> value.</summary>
        public byte doctrine;
        /// <summary>Network object ids of the troops, vehicles, buildings or flag it acted on (capped).</summary>
        public int[] objectIds;
    }

    /// <summary>The round's briefing, sent once the tier is settled: what this round is, for the start banner.</summary>
    public struct WV_RoundBriefing : IBroadcast {
        /// <summary>A <see cref="WV_DifficultyTier"/> value.</summary>
        public byte difficulty;
        public string difficultyName;
        /// <summary>A <see cref="WV_Front"/> value.</summary>
        public byte front;
        /// <summary>The front's rules in words ("plated enemies +15% health, an extra Punk every wave, +25% XP").</summary>
        public string frontRules;
        /// <summary>What the tier changes, in one line ("Tougher, faster waves").</summary>
        public string tierDescription;
        /// <summary>The tier's extra XP in percent (0 on Recruit).</summary>
        public short xpBonusPercent;
    }

    /// <summary>A commander closed (or finished reading) their round summary, so the next vote need not wait for them.</summary>
    public struct WV_SummaryRead : IBroadcast {
        public byte reserved;
    }
}
