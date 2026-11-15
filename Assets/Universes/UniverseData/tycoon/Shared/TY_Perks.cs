using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>Permanent upgrades bought with rebirth tokens.</summary>
    public enum TY_PerkId : byte { Income, Vault, HeadStart, Speed }

    /// <summary>A player's level in each perk (sent in the wallet).</summary>
    public struct TY_PerkLevels {
        public byte income, vault, headStart, speed;
        // Plain fields only: FishNet generates the serializer for this struct (no indexers/properties).
    }

    public readonly struct TY_Perk {
        public readonly TY_PerkId id;
        public readonly string name;
        /// <summary>Material icon name (tycoon UI/Icons/icon_&lt;name&gt;.png).</summary>
        public readonly string icon;
        public readonly int maxLevel;
        /// <summary>Tokens for the first level; each level after costs one more.</summary>
        public readonly int baseCost;
        /// <summary>Effect of one level, as shown in the perk shop ("{0}" is the level total).</summary>
        public readonly string effectFormat;
        public readonly double perLevel;

        public TY_Perk(TY_PerkId id, string name, string icon, int maxLevel, int baseCost, double perLevel, string effectFormat) {
            this.id = id; this.name = name; this.icon = icon; this.maxLevel = maxLevel;
            this.baseCost = baseCost; this.perLevel = perLevel; this.effectFormat = effectFormat;
        }

        public int Cost(int level) => baseCost + level;
        public string Effect(int level) => string.Format(effectFormat, perLevel * level);
    }

    /// <summary>The rebirth perk shop. Shared so the server prices perks and clients show the same numbers.</summary>
    public static class TY_Perks {
        /// <summary>Tokens a rebirth grants.</summary>
        public const int TokensPerRebirth = 1;

        public static readonly TY_Perk[] All = {
            new(TY_PerkId.Income, "Brick Boom", "trending_up", 5, 1, 0.25, "+{0:0%} income"),
            new(TY_PerkId.Vault, "Deep Pockets", "savings", 3, 1, 0.5, "+{0:0%} collector size"),
            new(TY_PerkId.HeadStart, "Head Start", "rocket_launch", 3, 1, 250, "Start with ${0:0}"),
            new(TY_PerkId.Speed, "Sprint Boots", "speed", 3, 1, 0.12, "+{0:0%} walk speed"),
        };

        public static TY_Perk Get(TY_PerkId id) => All[(int)id];

        public static int Level(this TY_PerkLevels levels, TY_PerkId id) => id switch {
            TY_PerkId.Income => levels.income,
            TY_PerkId.Vault => levels.vault,
            TY_PerkId.HeadStart => levels.headStart,
            TY_PerkId.Speed => levels.speed,
            _ => 0,
        };

        public static TY_PerkLevels WithLevel(this TY_PerkLevels levels, TY_PerkId id, int level) {
            byte v = (byte)Mathf.Clamp(level, 0, byte.MaxValue);
            switch (id) {
                case TY_PerkId.Income: levels.income = v; break;
                case TY_PerkId.Vault: levels.vault = v; break;
                case TY_PerkId.HeadStart: levels.headStart = v; break;
                case TY_PerkId.Speed: levels.speed = v; break;
            }
            return levels;
        }

        public static double IncomeMultiplier(TY_PerkLevels levels) => 1.0 + Get(TY_PerkId.Income).perLevel * levels.income;
        public static double CapacityMultiplier(TY_PerkLevels levels) => 1.0 + Get(TY_PerkId.Vault).perLevel * levels.vault;
        public static long StartingCash(TY_PerkLevels levels) => (long)(Get(TY_PerkId.HeadStart).perLevel * levels.headStart);
        public static float SpeedMultiplier(TY_PerkLevels levels) => 1f + (float)Get(TY_PerkId.Speed).perLevel * levels.speed;
    }
}
