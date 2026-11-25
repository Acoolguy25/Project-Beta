using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// Presentation of each build lane: title, Material icon and colour. Used by the buy pads in
    /// the world and the HUD build list, so a lane looks the same everywhere.
    /// </summary>
    public static class TY_Lanes {
        /// <summary>The lanes the HUD lists, in order (the rebirth has its own card).</summary>
        public static readonly TY_ItemKind[] BuildLanes = { TY_ItemKind.Dropper, TY_ItemKind.Upgrader, TY_ItemKind.Building, TY_ItemKind.Utility, TY_ItemKind.Landmark };

        /// <summary>Every lane with a buy pad on the plot, in pad order (the server checks them, clients draw them).</summary>
        public static readonly TY_ItemKind[] PadLanes = { TY_ItemKind.Dropper, TY_ItemKind.Upgrader, TY_ItemKind.Building, TY_ItemKind.Utility, TY_ItemKind.Rebirth, TY_ItemKind.Landmark, TY_ItemKind.Invest };

        public static string Title(TY_ItemKind kind) => kind switch {
            TY_ItemKind.Dropper => "Droppers",
            TY_ItemKind.Upgrader => "Upgraders",
            TY_ItemKind.Building => "Buildings",
            TY_ItemKind.Utility => "Collector",
            TY_ItemKind.Landmark => "Landmarks",
            TY_ItemKind.Invest => "Invest",
            _ => "Rebirth",
        };

        /// <summary>Icon file name under the tycoon's UI/Icons folder (icon_&lt;name&gt;.png).</summary>
        public static string Icon(TY_ItemKind kind) => kind switch {
            TY_ItemKind.Dropper => "precision_manufacturing",
            TY_ItemKind.Upgrader => "auto_awesome",
            TY_ItemKind.Building => "storefront",
            TY_ItemKind.Utility => "savings",
            TY_ItemKind.Landmark => "account_balance",
            TY_ItemKind.Invest => "trending_up",
            _ => "autorenew",
        };

        public static Color Color(TY_ItemKind kind) => kind switch {
            TY_ItemKind.Dropper => Hex(0xFF9F43),
            TY_ItemKind.Upgrader => Hex(0x2ED8A3),
            TY_ItemKind.Building => Hex(0x4DA8FF),
            TY_ItemKind.Utility => Hex(0xB98CFF),
            TY_ItemKind.Landmark => Hex(0xFFD24D),
            TY_ItemKind.Invest => Hex(0x5BE37D),
            _ => Hex(0xFF6BD6),
        };

        static Color Hex(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
