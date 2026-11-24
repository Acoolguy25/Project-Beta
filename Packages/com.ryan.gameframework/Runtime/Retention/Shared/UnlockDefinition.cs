using System;

namespace RyanAssets.Retention {
    /// <summary>
    /// Something a player earns by reaching a level of a game's <see cref="LevelTrack"/>: a tower, a
    /// weapon, a perk slot... The game decides what it does (ask <c>RetentionService.IsUnlocked</c>);
    /// the kit announces it, reveals it on the match summary and shows the next one as a goal.
    /// </summary>
    /// <example><code>new UnlockDefinition(5, "tesla", "Tesla Tower", "Chains lightning between enemies.", "bolt")</code></example>
    [Serializable]
    public sealed class UnlockDefinition {
        /// <summary>Level that unlocks it (1 = owned from the start).</summary>
        public int level;
        /// <summary>Stable id the game checks; never rename a shipped id (saves don't store unlocks, but games do).</summary>
        public string id;
        /// <summary>Short name ("Tesla Tower").</summary>
        public string title;
        /// <summary>One line on why it is worth having.</summary>
        public string description;
        /// <summary>
        /// Theme icon name (Material or game-icons.net set, see <c>Tools/UIIcons/icons.json</c>), e.g.
        /// "bolt" or "crossed_swords". The HUD prefab carries every theme icon; unknown names show a star.
        /// </summary>
        public string icon;

        public UnlockDefinition() { }

        public UnlockDefinition(int level, string id, string title, string description, string icon) {
            this.level = level;
            this.id = id;
            this.title = title;
            this.description = description;
            this.icon = icon;
        }

        public override string ToString() => $"Lv {level} {id}";
    }
}
