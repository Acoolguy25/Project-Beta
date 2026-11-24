using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// How a tier and a front look wherever the HUD names them (the tier choice, the wave banner, the
    /// campaign card, the round briefing): one colour and one emblem each, so a Veteran round under
    /// Iron Storm reads differently from a Recruit round at a glance.
    /// </summary>
    public static class WV_TierStyle {
        static readonly Color Recruit = new(0.48f, 0.86f, 0.45f, 1f);
        static readonly Color Veteran = new(0.42f, 0.68f, 1f, 1f);
        static readonly Color Elite = new(0.78f, 0.45f, 1f, 1f);
        static readonly Color Nightmare = new(0.96f, 0.30f, 0.26f, 1f);
        static readonly Color Endless = new(1f, 0.72f, 0.30f, 1f);

        public static Color Accent(WV_DifficultyTier tier) => tier switch {
            WV_DifficultyTier.Veteran => Veteran,
            WV_DifficultyTier.Elite => Elite,
            WV_DifficultyTier.Nightmare => Nightmare,
            WV_DifficultyTier.Endless => Endless,
            _ => Recruit
        };

        /// <summary>The tier's emblem, by theme icon name.</summary>
        public static string Icon(WV_DifficultyTier tier) => tier switch {
            WV_DifficultyTier.Veteran => "military_tech",
            WV_DifficultyTier.Elite => "star_medal",
            WV_DifficultyTier.Nightmare => "crowned_skull",
            WV_DifficultyTier.Endless => "hourglass",
            _ => "shield"
        };

        /// <summary>The front's emblem, by theme icon name.</summary>
        public static string FrontIcon(WV_Front front) => front switch {
            WV_Front.WarProfiteers => "coins",
            WV_Front.IronStorm => "templar_shield",
            WV_Front.SupplyLines => "backpack",
            WV_Front.EngineerCorps => "castle",
            WV_Front.RapidDeployment => "rally_troops",
            WV_Front.ArmoredFront => "battle_tank",
            _ => "compass"
        };

        /// <summary>The front's colour.</summary>
        public static Color FrontAccent(WV_Front front) => front switch {
            WV_Front.WarProfiteers => new Color(1f, 0.8f, 0.3f, 1f),
            WV_Front.IronStorm => new Color(0.66f, 0.74f, 0.86f, 1f),
            WV_Front.SupplyLines => new Color(0.46f, 0.86f, 0.62f, 1f),
            WV_Front.EngineerCorps => new Color(1f, 0.6f, 0.3f, 1f),
            WV_Front.RapidDeployment => new Color(0.44f, 0.84f, 1f, 1f),
            WV_Front.ArmoredFront => new Color(0.62f, 0.7f, 0.5f, 1f),
            _ => new Color(0.95f, 0.72f, 0.32f, 1f)
        };
    }
}
