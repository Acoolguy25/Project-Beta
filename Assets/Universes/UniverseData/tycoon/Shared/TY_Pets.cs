using System;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>How rare a pet is: rarer pets are hatched less often and boost income more.</summary>
    public enum TY_Rarity : byte { Common, Uncommon, Rare, Epic, Legendary }

    /// <summary>Body plan the authoring builds a pet's blocky model from.</summary>
    public enum TY_PetShape : byte { Dog, Cat, Duck, Bunny, Fox, Owl, Pig, Mole, Dragon, Unicorn }

    /// <summary>One collectible pet ("Brick Buddy").</summary>
    public readonly struct TY_Pet {
        public readonly string name;
        public readonly TY_Rarity rarity;
        public readonly TY_PetShape shape;
        /// <summary>Main and detail colours of the model.</summary>
        public readonly Color body, accent;

        public TY_Pet(string name, TY_Rarity rarity, TY_PetShape shape, Color body, Color accent) {
            this.name = name; this.rarity = rarity; this.shape = shape; this.body = body; this.accent = accent;
        }
    }

    /// <summary>
    /// Brick Buddies: pets hatched from the pet crate on the owner's plot. A hatched pet is kept for good
    /// (through rebirths); hatching one again levels it up. The best pet follows its owner and boosts their
    /// income, and every kind found adds a small collection bonus, so the whole set is worth chasing.
    /// Shared so the server (rolls, income) and clients (crate sign, toasts, followers) agree.
    /// <para>Pet indices are saved: only ever append to <see cref="All"/>.</para>
    /// </summary>
    public static class TY_Pets {
        /// <summary>Highest level of a pet: hatching it again after that refunds part of the price.</summary>
        public const int MaxLevel = 5;
        /// <summary>Each level after the first adds this share of the pet's base bonus (level 5 = x1.8).</summary>
        public const double LevelStep = 0.2;
        /// <summary>Income added per kind of pet found (the collection bonus).</summary>
        public const double FoundBonus = 0.02;

        static Color Hex(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        public static readonly TY_Pet[] All = {
            /*0*/ new("Brick Pup", TY_Rarity.Common, TY_PetShape.Dog, Hex(0xC0573E), Hex(0xF2E2C4)),
            /*1*/ new("Clay Kitty", TY_Rarity.Common, TY_PetShape.Cat, Hex(0xE0A15E), Hex(0xFFF1DC)),
            /*2*/ new("Mortar Duck", TY_Rarity.Uncommon, TY_PetShape.Duck, Hex(0xF4F1E8), Hex(0xFF9A1F)),
            /*3*/ new("Cinder Bunny", TY_Rarity.Uncommon, TY_PetShape.Bunny, Hex(0x8E939E), Hex(0xFFB3C7)),
            /*4*/ new("Kiln Fox", TY_Rarity.Rare, TY_PetShape.Fox, Hex(0xFF7A26), Hex(0xFFFFFF)),
            /*5*/ new("Granite Owl", TY_Rarity.Rare, TY_PetShape.Owl, Hex(0x6F7F99), Hex(0xFFD24D)),
            /*6*/ new("Golden Piggy", TY_Rarity.Epic, TY_PetShape.Pig, Hex(0xFFC93C), Hex(0xFF8FB8)),
            /*7*/ new("Robo Mole", TY_Rarity.Epic, TY_PetShape.Mole, Hex(0x9AA7B8), Hex(0x3DE0FF)),
            /*8*/ new("Brick Dragon", TY_Rarity.Legendary, TY_PetShape.Dragon, Hex(0xD9342B), Hex(0xFFC93C)),
            /*9*/ new("Crystal Unicorn", TY_Rarity.Legendary, TY_PetShape.Unicorn, Hex(0xEAF6FF), Hex(0xB98CFF)),
        };

        public static int Count => All.Length;

        public static double RarityBonus(TY_Rarity rarity) => rarity switch {
            TY_Rarity.Common => 0.05,
            TY_Rarity.Uncommon => 0.10,
            TY_Rarity.Rare => 0.20,
            TY_Rarity.Epic => 0.35,
            _ => 0.60,
        };

        public static string RarityName(TY_Rarity rarity) => rarity switch {
            TY_Rarity.Common => "Common",
            TY_Rarity.Uncommon => "Uncommon",
            TY_Rarity.Rare => "Rare",
            TY_Rarity.Epic => "Epic",
            _ => "Legendary",
        };

        public static Color RarityColor(TY_Rarity rarity) => rarity switch {
            TY_Rarity.Common => Hex(0xC9D1DC),
            TY_Rarity.Uncommon => Hex(0x5BE37D),
            TY_Rarity.Rare => Hex(0x4DA8FF),
            TY_Rarity.Epic => Hex(0xC77DFF),
            _ => Hex(0xFFC93C),
        };

        /// <summary>The rarity's colour as rich-text hex ("#FFC93C").</summary>
        public static string RarityHex(TY_Rarity rarity) => "#" + ColorUtility.ToHtmlStringRGB(RarityColor(rarity));

        public static bool IsValid(int pet) => pet >= 0 && pet < All.Length;

        /// <summary>Income bonus of <paramref name="pet"/> at <paramref name="level"/> (0 = not found: no bonus).</summary>
        public static double Bonus(int pet, int level) {
            if (!IsValid(pet) || level <= 0) return 0d;
            return RarityBonus(All[pet].rarity) * (1d + LevelStep * (Math.Min(level, MaxLevel) - 1));
        }

        static int Level(byte[] levels, int pet) => levels != null && pet < levels.Length ? levels[pet] : 0;

        /// <summary>The pet that follows its owner and boosts them: the one with the biggest bonus (the rarer on a tie), or -1.</summary>
        public static int Best(byte[] levels) {
            int best = -1;
            double bestBonus = 0d;
            for (int i = 0; i < All.Length; i++) {
                double bonus = Bonus(i, Level(levels, i));
                if (bonus > 0d && bonus >= bestBonus) { best = i; bestBonus = bonus; }
            }
            return best;
        }

        /// <summary>Kinds of pet found.</summary>
        public static int Found(byte[] levels) {
            int n = 0;
            for (int i = 0; i < All.Length; i++) if (Level(levels, i) > 0) n++;
            return n;
        }

        /// <summary>Income multiplier of a collection: the best pet's bonus times the collection bonus.</summary>
        public static double Multiplier(byte[] levels) {
            int best = Best(levels);
            return (1d + (best >= 0 ? Bonus(best, Level(levels, best)) : 0d)) * (1d + FoundBonus * Found(levels));
        }

        /// <summary>A collection with room for every pet (an old or shorter save is padded, unknown pets dropped, levels clamped).</summary>
        public static byte[] Repair(byte[] levels) {
            var repaired = new byte[All.Length];
            if (levels != null)
                for (int i = 0; i < repaired.Length && i < levels.Length; i++) repaired[i] = (byte)Math.Clamp((int)levels[i], 0, MaxLevel);
            return repaired;
        }

        /// <summary>"Kiln Fox Lv 2 (+24%)": a pet for signs and toasts.</summary>
        public static string Describe(int pet, int level) =>
            IsValid(pet) ? $"{All[pet].name} Lv {level} (+{Bonus(pet, level) * 100d:0}%)" : "";
    }

    /// <summary>What a hatch did to the collection.</summary>
    public readonly struct TY_Hatch {
        public readonly int pet;
        /// <summary>The pet's level after the hatch.</summary>
        public readonly int level;
        /// <summary>First time this kind was found.</summary>
        public readonly bool isNew;
        /// <summary>The pet was already at <see cref="TY_Pets.MaxLevel"/>: nothing levelled, part of the price comes back.</summary>
        public readonly bool maxed;

        public TY_Hatch(int pet, int level, bool isNew, bool maxed) { this.pet = pet; this.level = level; this.isNew = isNew; this.maxed = maxed; }

        /// <summary>Flags for the event's extra number (bit 0 new, bit 1 maxed), so clients read the same result.</summary>
        public long Flags => (isNew ? 1L : 0L) | (maxed ? 2L : 0L);
        public static bool IsNewFlag(long flags) => (flags & 1L) != 0;
        public static bool MaxedFlag(long flags) => (flags & 2L) != 0;
    }

    /// <summary>
    /// The pet crate's tuning: its price (seconds of the owner's income, never below a floor), the rarity
    /// odds and the pity counter that guarantees an Epic or better after a dry run. Serialized on the server
    /// runner; the price reaches clients in the wallet.
    /// </summary>
    [Serializable]
    public sealed class TY_PetCrate {
        [Tooltip("A crate costs this many seconds of the owner's income (rush hours left out)...")]
        [Min(1f)] public float incomeSeconds = 75f;
        [Tooltip("...but never less than this.")]
        [Min(1)] public long minimumPrice = 250;
        [Tooltip("Relative odds of each rarity, Common to Legendary.")]
        public int[] rarityWeights = { 55, 27, 12, 5, 1 };
        [Tooltip("After this many crates in a row without an Epic or Legendary, the next one is at least Epic (0 = no pity).")]
        [Min(0)] public int pityCrates = 15;
        [Tooltip("Share of the price refunded when the hatched pet is already at its highest level.")]
        [Range(0f, 1f)] public float maxedRefund = 0.5f;
        [Tooltip("Game XP per crate opened.")]
        [Min(0)] public int xp = 8;

        public void Validate() {
            incomeSeconds = Mathf.Max(1f, incomeSeconds);
            minimumPrice = Math.Max(1, minimumPrice);
            int rarities = Enum.GetValues(typeof(TY_Rarity)).Length;
            if (rarityWeights == null || rarityWeights.Length != rarities) rarityWeights = new[] { 55, 27, 12, 5, 1 };
            for (int i = 0; i < rarityWeights.Length; i++) rarityWeights[i] = Math.Max(0, rarityWeights[i]);
            if (Array.TrueForAll(rarityWeights, w => w == 0)) rarityWeights[0] = 1;
            pityCrates = Math.Max(0, pityCrates);
            maxedRefund = Mathf.Clamp01(maxedRefund);
            xp = Math.Max(0, xp);
        }

        /// <summary>The crate's price for an owner earning <paramref name="incomePerSecond"/> (rounded to two significant digits).</summary>
        public long Price(double incomePerSecond) {
            double raw = double.IsNaN(incomePerSecond) || incomePerSecond <= 0d ? 0d : incomePerSecond * incomeSeconds;
            return Math.Max(minimumPrice, TY_Catalog.RoundPrice(Math.Min(raw, long.MaxValue / 4d)));
        }

        /// <summary>Crates left until the pity guarantee, given crates opened since the last Epic or better (0 when it is off).</summary>
        public int UntilPity(int sinceEpic) => pityCrates <= 0 ? 0 : Math.Max(1, pityCrates - Math.Max(0, sinceEpic));

        /// <summary>The rarity of a roll in [0, 1); at least Epic once the pity counter is due.</summary>
        public TY_Rarity RollRarity(double roll, int sinceEpic) {
            bool pity = pityCrates > 0 && sinceEpic >= pityCrates - 1;
            int from = pity ? (int)TY_Rarity.Epic : 0;
            long total = 0;
            for (int i = from; i < rarityWeights.Length; i++) total += rarityWeights[i];
            if (total <= 0) return pity ? TY_Rarity.Epic : TY_Rarity.Common;
            double pick = Math.Clamp(roll, 0d, 0.999999d) * total;
            for (int i = from; i < rarityWeights.Length; i++) {
                pick -= rarityWeights[i];
                if (pick < 0d) return (TY_Rarity)i;
            }
            return (TY_Rarity)(rarityWeights.Length - 1);
        }

        /// <summary>A pet of <paramref name="rarity"/>, picked by <paramref name="roll"/> in [0, 1).</summary>
        public static int PickPet(TY_Rarity rarity, double roll) {
            int count = 0;
            foreach (TY_Pet pet in TY_Pets.All) if (pet.rarity == rarity) count++;
            if (count == 0) return 0;
            int n = Math.Min(count - 1, (int)(Math.Clamp(roll, 0d, 0.999999d) * count));
            for (int i = 0; i < TY_Pets.All.Length; i++)
                if (TY_Pets.All[i].rarity == rarity && n-- == 0) return i;
            return 0;
        }

        /// <summary>Adds <paramref name="pet"/> to <paramref name="levels"/> (a repaired collection) and says what happened.</summary>
        public static TY_Hatch Apply(byte[] levels, int pet) {
            int before = levels[pet];
            if (before >= TY_Pets.MaxLevel) return new TY_Hatch(pet, before, false, true);
            levels[pet] = (byte)(before + 1);
            return new TY_Hatch(pet, before + 1, before == 0, false);
        }

        /// <summary>The pity counter after hatching <paramref name="rarity"/>.</summary>
        public static int NextSinceEpic(int sinceEpic, TY_Rarity rarity) => rarity >= TY_Rarity.Epic ? 0 : Math.Max(0, sinceEpic) + 1;
    }
}
