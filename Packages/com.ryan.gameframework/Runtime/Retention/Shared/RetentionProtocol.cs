using FishNet.Broadcast;

// Wire contract of the retention kit. APPEND-ONLY: never reorder, retype or remove a field of these
// structs, and never renumber an enum; add new fields at the end. Plain fields only (FishNet
// generates the serializers). Every broadcast is server -> one client except RetentionSnapshotRequest.
namespace RyanAssets.Retention {
    /// <summary>An unlock as the client shows it.</summary>
    public struct RetentionUnlockView {
        public string id;
        public int level;
        public string title;
        public string description;
        /// <summary>Theme icon name.</summary>
        public string icon;
    }

    /// <summary>One of today's challenges with the player's progress.</summary>
    public struct RetentionChallengeView {
        public string id;
        /// <summary>Display text with the target filled in.</summary>
        public string text;
        public int progress;
        public int target;
        public int xpReward;
        public int goldReward;
        /// <summary>Completed and paid.</summary>
        public bool done;
    }

    /// <summary>One line of the match summary card.</summary>
    public struct RetentionRewardView {
        public string label;
        public int xp;
        public int gold;
        /// <summary>Merged rewards ("x5" when above 1).</summary>
        public int count;
        // ---- added in kit v2
        /// <summary>Short text shown with the line ("3 stars"); null or empty when none. A line with no XP and no gold is an info line.</summary>
        public string detail;
    }

    /// <summary>
    /// Everything the retention HUD shows, sent when the player joins, when the day rolls over, after a
    /// match and when the client asks (<see cref="RetentionSnapshotRequest"/>).
    /// </summary>
    public struct RetentionSnapshotBroadcast : IBroadcast {
        public string universeId;
        public int level;
        /// <summary>XP inside the current level, and the level's size (0 at the maximum level).</summary>
        public int levelXp;
        public int levelXpNeeded;
        public long totalXp;
        public int maxLevel;
        public RetentionChallengeView[] challenges;
        /// <summary>Seconds until the challenges and the streak day roll over (UTC midnight).</summary>
        public int secondsToReset;
        /// <summary>Current streak in days; 0 when it lapsed (or never started).</summary>
        public int streakDays;
        /// <summary>Today's streak bonus is already paid.</summary>
        public bool streakClaimedToday;
        /// <summary>What the next streak claim pays (today's if unclaimed, else tomorrow's).</summary>
        public int streakNextXp;
        public int streakNextGold;
        public int bestStreak;
        /// <summary>False when <see cref="nextUnlock"/> is empty (everything owned).</summary>
        public bool hasNextUnlock;
        public RetentionUnlockView nextUnlock;
        /// <summary>The player's progress could not be loaded: they play on defaults and nothing is saved this session.</summary>
        public bool notSaved;
        // ---- added in kit v2
        /// <summary>
        /// The <see cref="RetentionDay"/> the challenges belong to. Usually today; one day ahead when this
        /// server's clock is a little behind the one that last wrote the save. Progress messages carry it,
        /// so a message about another day's list is recognised and a fresh snapshot requested.
        /// </summary>
        public int challengeDay;
    }

    /// <summary>Client -> server: send me a snapshot (the HUD asks until one arrives). Rate-limited by the server.</summary>
    public struct RetentionSnapshotRequest : IBroadcast {
        /// <summary>Unused; keeps the struct from being empty for FishNet's generated serializer.</summary>
        public byte reserved;
    }

    /// <summary>Game XP the player just earned (the level chip's bar moves and shows "+XP").</summary>
    public struct RetentionXpBroadcast : IBroadcast {
        public int xp;
        public string reason;
        /// <summary>The player's level after the gain.</summary>
        public int level;
        public int levelXp;
        public int levelXpNeeded;
        public long totalXp;
        /// <summary>False when the gain should move the bar silently (no "+XP" popup).</summary>
        public bool announce;
    }

    /// <summary>The player reached a new level outside a match summary.</summary>
    public struct RetentionLevelUpBroadcast : IBroadcast {
        public int previousLevel;
        public int level;
        public RetentionUnlockView[] unlocks;
        public bool hasNextUnlock;
        public RetentionUnlockView nextUnlock;
    }

    /// <summary>
    /// One of today's challenges completed and paid (sent once per challenge and day). Plain progress
    /// moves with the lean <see cref="RetentionProgressBroadcast"/> instead.
    /// </summary>
    public struct RetentionChallengeBroadcast : IBroadcast {
        /// <summary>Index in the snapshot's challenge list.</summary>
        public int index;
        public string id;
        public string text;
        public int progress;
        public int target;
        public bool completed;
        public int xpReward;
        public int goldReward;
        // ---- added in kit v2
        /// <summary>The snapshot's <see cref="RetentionSnapshotBroadcast.challengeDay"/> this message belongs to.</summary>
        public int day;
    }

    /// <summary>The end-of-match card: what the player earned and how their level moved.</summary>
    public struct RetentionSummaryBroadcast : IBroadcast {
        public string title;
        public RetentionRewardView[] lines;
        public int totalXp;
        public int totalGold;
        public int levelBefore;
        public int levelXpBefore;
        public int levelXpNeededBefore;
        public int levelAfter;
        public int levelXpAfter;
        public int levelXpNeededAfter;
        /// <summary>Unlocks earned this match, in level order.</summary>
        public RetentionUnlockView[] unlocks;
        /// <summary>The streak day this match paid (0 = no streak bonus this time).</summary>
        public int streakDays;
        public bool hasNextUnlock;
        public RetentionUnlockView nextUnlock;
    }

    // ---- added in kit v2

    /// <summary>
    /// Progress on one of today's challenges, without completing it. Coalesced by the server (a few per
    /// second at most), and lean on purpose: the text, target and rewards are in the snapshot already.
    /// </summary>
    public struct RetentionProgressBroadcast : IBroadcast {
        /// <summary>The snapshot's <see cref="RetentionSnapshotBroadcast.challengeDay"/> this belongs to.</summary>
        public int day;
        /// <summary>Index in the snapshot's challenge list.</summary>
        public int index;
        public int progress;
    }
}
