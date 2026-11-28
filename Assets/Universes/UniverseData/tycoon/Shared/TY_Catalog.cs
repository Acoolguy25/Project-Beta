using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>What an item does once bought. Each kind is its own build lane with one buy pad.</summary>
    public enum TY_ItemKind : byte {
        /// <summary>Drops bricks on the belt. Value: cash per brick. Interval: seconds between bricks.</summary>
        Dropper,
        /// <summary>A gate on the belt that multiplies brick value. Value: multiplier.</summary>
        Upgrader,
        /// <summary>A building on the lot that adds an income bonus. Value: bonus fraction (0.1 = +10%).</summary>
        Building,
        /// <summary>Collector upgrade. Value: the collector's new capacity; 0 means auto-collect.</summary>
        Utility,
        /// <summary>Resets the plot for a permanent income multiplier and a perk token.</summary>
        Rebirth,
        /// <summary>
        /// A landmark: only for sale after a number of rebirths, and it stands through every rebirth after
        /// that. Value: income multiplier (1.25 = x1.25). Appended: the value is an index into prefab arrays.
        /// </summary>
        Landmark,
        /// <summary>
        /// A repeatable investment, for sale once the factory is complete (like the rebirth): each level adds
        /// income for good, kept through rebirths. Value: income added per level (0.05 = +5%). Appended.
        /// </summary>
        Invest,
        /// <summary>
        /// Scenery bought for looks only: no income. Built decorations stay through every rebirth and are
        /// never needed for the rebirth. Appended.
        /// </summary>
        Decor,
    }

    /// <summary>One purchasable thing on a plot. Positions are in plot space: +Z faces the plaza.</summary>
    public readonly struct TY_Item {
        public readonly string name;
        public readonly long price;
        /// <summary>Index of the item that must be bought first, or -1.</summary>
        public readonly int prereq;
        public readonly TY_ItemKind kind;
        /// <summary>Meaning depends on <see cref="kind"/>.</summary>
        public readonly double value;
        /// <summary>Droppers: seconds between bricks.</summary>
        public readonly float interval;
        public readonly Vector3 itemPosition;
        /// <summary>Yaw (degrees) of the bought model in plot space.</summary>
        public readonly float itemYaw;
        /// <summary>Rebirths the owner needs before the item is for sale (0 = from the start).</summary>
        public readonly int rebirths;
        /// <summary>
        /// Side of the square the bought model covers on the lot, in metres (0 = on the belt or along the
        /// walls, not checked). The authoring builds landmark bases this size, and a test keeps every
        /// footprint clear of the others and of the pads.
        /// </summary>
        public readonly float size;

        public TY_Item(string name, long price, int prereq, TY_ItemKind kind, double value, float interval, Vector3 item, float yaw = 0f, int rebirths = 0, float size = 0f) {
            this.name = name; this.price = price; this.prereq = prereq; this.kind = kind;
            this.value = value; this.interval = interval; itemPosition = item; itemYaw = yaw; this.rebirths = rebirths; this.size = size;
        }

        public bool IsAutoCollector => kind == TY_ItemKind.Utility && value <= 0;

        /// <summary>Bought again and again (never kept as a bit of the build): the rebirth and the investment.</summary>
        public bool IsRepeatable => kind is TY_ItemKind.Rebirth or TY_ItemKind.Invest;

        /// <summary>One-line effect shown on buy pads and build cards (the rebirth's depends on the owner: see <see cref="TY_Catalog.Effect"/>).</summary>
        public string Effect => kind switch {
            TY_ItemKind.Dropper => $"+${value / interval:0.#}/s",
            TY_ItemKind.Upgrader => $"Bricks x{value:0.##}",
            TY_ItemKind.Building => $"+{value * 100:0}% income",
            TY_ItemKind.Utility => IsAutoCollector ? "Auto-collects cash" : $"Holds ${TY_Catalog.Money(value)}",
            TY_ItemKind.Rebirth => "More income, +perk token",
            TY_ItemKind.Landmark => $"x{value:0.##} income, kept",
            TY_ItemKind.Invest => $"+{value * 100:0}% income, kept",
            TY_ItemKind.Decor => "Looks great, no income",
            _ => "",
        };
    }

    /// <summary>
    /// The tycoon's build order and economy. Shared by the server (prices, income, unlocks) and clients
    /// (which pads to show, where bricks drop, what the HUD promises), so both always agree.
    /// <para>
    /// Items are grouped in lanes by <see cref="TY_ItemKind"/>. Each lane is a chain (every item needs
    /// the previous one in its lane), so a lane only ever shows one buy pad at a time; some links
    /// also need an item from another lane, which paces the build. The landmark lane opens one item
    /// per rebirth milestone and keeps what is built through rebirths, so every rebirth has a visible
    /// reward waiting behind it.
    /// </para>
    /// </summary>
    public static class TY_Catalog {
        public const float PlotSize = 40f;
        public const float BeltZ = -4f, BeltStartX = -17f, BeltEndX = 15f, BeltSpeed = 4f, BeltTop = 0.9f;
        /// <summary>Income multiplier each rebirth adds.</summary>
        public const float RebirthBonus = 0.5f;
        /// <summary>Each rebirth costs this many times the one before (the first costs the REBIRTH item's price).</summary>
        public const double RebirthPriceGrowth = 1.75;
        /// <summary>Significant digits a scaled price is rounded to ($87,500 shows and costs $88K).</summary>
        const int PriceDigits = 2;
        /// <summary>What a collector holds before any vault is bought.</summary>
        public const long BaseCapacity = 400;

        public static readonly Vector3 FurnacePosition = new(17f, 0f, BeltZ);
        public static readonly Vector3 CollectPosition = new(15.5f, 0f, 1f);
        public static readonly Vector3 ClaimPosition = new(0f, 0f, 17.5f);
        public static readonly Vector3 SpawnPosition = new(0f, 0f, 10f);
        /// <summary>The owner's daily reward chest, next to where they spawn.</summary>
        public static readonly Vector3 ChestPosition = new(-3.4f, 0f, 10.5f);
        /// <summary>Side of the chest's pad (the authored chest stands on it).</summary>
        public const float ChestPadSize = 2.6f;
        /// <summary>Inner face of the plot's wall (the Brick Walls building): nothing may stand past it.</summary>
        public const float WallInner = PlotSize * 0.5f - 0.6f;
        /// <summary>Half-width of the walkway from the front gate to the spawn point, kept clear of landmarks.</summary>
        public const float GateHalfWidth = 2.5f;
        public static readonly Vector3 ButtonSize = new(3.2f, 1f, 3.2f);
        public static readonly Vector3 PadSize = new(3.6f, 1f, 3.6f);
        /// <summary>
        /// Area (plot space, x/z min-max) where gold coins can appear: the open walkway between the belt
        /// and the buy pads, so a coin never lands inside a building or landmark.
        /// </summary>
        public static readonly Rect GoldenArea = new(-15f, 0f, 29f, 9f);

        /// <summary>Where each lane's buy pad sits (plot space).</summary>
        public static Vector3 LanePosition(TY_ItemKind kind) => kind switch {
            TY_ItemKind.Dropper => new Vector3(-13.5f, 0f, 6f),
            TY_ItemKind.Upgrader => new Vector3(-7f, 0f, 6f),
            TY_ItemKind.Building => new Vector3(7f, 0f, 6f),
            TY_ItemKind.Utility => new Vector3(13.5f, 0f, 7f),
            TY_ItemKind.Landmark => new Vector3(0f, 0f, 6f),
            TY_ItemKind.Invest => new Vector3(4f, 0f, 14f),
            TY_ItemKind.Decor => new Vector3(-10.5f, 0f, 2.2f),
            _ => new Vector3(0f, 0f, 14f),
        };

        /// <summary>Plot-space z of the strip of lawn just outside the front wall, where the decorations stand.</summary>
        public const float YardZ = PlotSize * 0.5f + 2.6f;

        static Vector3 Dropper(int k) => new(-16f + k * 3.25f, 0f, BeltZ);
        static Vector3 Lot(float x, float z) => new(x, 0f, z);

        // Indices are referenced by prereq and saved as bits of a plot's build: only ever append, and
        // keep the comments in sync.
        public static readonly TY_Item[] Items = {
            /* 0*/ new("Starter Dropper", 0, -1, TY_ItemKind.Dropper, 2, 1.5f, Dropper(0)),
            /* 1*/ new("Twin Dropper", 40, 0, TY_ItemKind.Dropper, 3, 1.5f, Dropper(1)),
            /* 2*/ new("Brick Press", 150, 1, TY_ItemKind.Dropper, 5, 1.25f, Dropper(2)),
            /* 3*/ new("Clay Kiln", 500, 2, TY_ItemKind.Dropper, 12, 1.5f, Dropper(3)),
            /* 4*/ new("Steam Dropper", 1500, 3, TY_ItemKind.Dropper, 25, 1.5f, Dropper(4)),
            /* 5*/ new("Clay Drill", 4500, 4, TY_ItemKind.Dropper, 60, 2f, Dropper(5)),
            /* 6*/ new("Mega Drill", 12000, 5, TY_ItemKind.Dropper, 150, 2.5f, Dropper(6)),

            /* 7*/ new("Polisher", 300, 2, TY_ItemKind.Upgrader, 1.25, 0, new Vector3(7.4f, 0f, BeltZ)),
            /* 8*/ new("Washer", 2000, 7, TY_ItemKind.Upgrader, 1.5, 0, new Vector3(10.2f, 0f, BeltZ)),
            /* 9*/ new("Glazer", 8000, 8, TY_ItemKind.Upgrader, 2, 0, new Vector3(13f, 0f, BeltZ)),

            /*10*/ new("Brick Walls", 80, 0, TY_ItemKind.Building, 0.05, 0, Vector3.zero),
            /*11*/ new("Street Lamps", 250, 10, TY_ItemKind.Building, 0.05, 0, Vector3.zero),
            /*12*/ new("Office", 900, 11, TY_ItemKind.Building, 0.1, 0, Lot(-13.5f, -14f)),
            /*13*/ new("Corner Shop", 2500, 12, TY_ItemKind.Building, 0.1, 0, Lot(-4.5f, -14f)),
            /*14*/ new("Apartments", 6000, 13, TY_ItemKind.Building, 0.15, 0, Lot(4.5f, -14f)),
            /*15*/ new("Wind Farm", 9000, 14, TY_ItemKind.Building, 0.15, 0, Lot(-17.6f, 5f)),
            /*16*/ new("Golden Statue", 15000, 15, TY_ItemKind.Building, 0.2, 0, Lot(-14f, 15f), size: 4f),

            /*17*/ new("Vault", 120, 0, TY_ItemKind.Utility, 1500, 0, Lot(17.5f, 4.8f), size: 2.4f),
            /*18*/ new("Big Vault", 1000, 17, TY_ItemKind.Utility, 6000, 0, Lot(17.3f, 9.4f), size: 3.4f),
            /*19*/ new("Mega Vault", 5000, 18, TY_ItemKind.Utility, 25000, 0, Lot(16.8f, 15f), size: 3.6f),
            /*20*/ new("Auto-Collector", 10000, 19, TY_ItemKind.Utility, 0, 0, Lot(18.7f, 1f)),
            /*21*/ new("Bank Tower", 20000, 20, TY_ItemKind.Utility, 120000, 0, Lot(13.5f, -14f)),

            /*22*/ new("REBIRTH", 50000, -1, TY_ItemKind.Rebirth, 0, 0, Vector3.zero),

            // Landmarks: one opens at each rebirth milestone; built ones are kept through every rebirth. The
            // milestones spread out (1, 3, 8, 13, 18) so the plot keeps changing over days of play, not one
            // sitting. They stand in the two yards either side of the gate walkway, clear of the pads and each
            // other (a test checks the footprints).
            /*23*/ new("Solar Farm", 25_000, -1, TY_ItemKind.Landmark, 1.25, 0, Lot(-8.6f, 11.9f), rebirths: 1, size: 4.8f),
            /*24*/ new("Cargo Port", 180_000, 23, TY_ItemKind.Landmark, 1.35, 0, Lot(8.1f, 11.9f), rebirths: 3, size: 4.8f),
            /*25*/ new("Truck Depot", 2_500_000, 24, TY_ItemKind.Landmark, 1.5, 0, Lot(-8.6f, 16.8f), rebirths: 8, size: 4.6f),
            /*26*/ new("Landing Pad", 45_000_000, 25, TY_ItemKind.Landmark, 1.75, 0, Lot(12.8f, 11.9f), rebirths: 13, size: 4.2f),
            /*27*/ new("Brick Rocket", 700_000_000, 26, TY_ItemKind.Landmark, 2, 0, Lot(9.9f, 16.8f), rebirths: 18, size: 4.4f),

            // Bought again and again once the factory is complete: a use for late cash (see InvestPrice).
            /*28*/ new("INVEST", 0, -1, TY_ItemKind.Invest, 0.05, 0, Vector3.zero),

            // Decorations: a lane of their own that earns nothing. They dress the yard outside the front wall, where
            // every player passing on the plaza path sees them (the lot inside is full), and every rebirth keeps
            // them. Positions are the middle of each set; a test keeps them outside the wall.
            /*29*/ new("Flower Beds", 400, 11, TY_ItemKind.Decor, 0, 0, Lot(0f, YardZ)),
            /*30*/ new("Garden Trees", 1_500, 29, TY_ItemKind.Decor, 0, 0, Lot(0f, YardZ + 3f)),
            /*31*/ new("Park Benches", 4_000, 30, TY_ItemKind.Decor, 0, 0, Lot(0f, YardZ + 1.2f)),
            /*32*/ new("Gate Flags", 9_000, 31, TY_ItemKind.Decor, 0, 0, Lot(0f, YardZ - 1.4f)),
            /*33*/ new("Duck Pond", 20_000, 32, TY_ItemKind.Decor, 0, 0, Lot(-12f, YardZ + 5f)),
            /*34*/ new("Fairy Lights", 45_000, 33, TY_ItemKind.Decor, 0, 0, Lot(0f, -18.8f)),
            /*35*/ new("Hot-Air Balloon", 100_000, 34, TY_ItemKind.Decor, 0, 0, Lot(13f, YardZ + 5f)),
        };

        public static int Count => Items.Length;
        public static int RebirthIndex { get; } = Array.FindIndex(Items, i => i.kind == TY_ItemKind.Rebirth);
        public static int InvestIndex { get; } = Array.FindIndex(Items, i => i.kind == TY_ItemKind.Invest);
        public static ulong StarterMask => 1ul;
        /// <summary>Every item that can be built (owned as a bit): everything but the rebirth and the investment.</summary>
        public static ulong AllButRebirth { get; } = ((1ul << Count) - 1ul) & ~(1ul << RebirthIndex) & ~(1ul << InvestIndex);
        /// <summary>Every landmark.</summary>
        public static ulong LandmarkMask { get; } = KindMask(TY_ItemKind.Landmark);
        /// <summary>Every decoration (kept through rebirths, never needed for one).</summary>
        public static ulong DecorMask { get; } = KindMask(TY_ItemKind.Decor);
        /// <summary>Every collector upgrade (kept through rebirths by the Head Office level unlock).</summary>
        public static ulong UtilityMask { get; } = KindMask(TY_ItemKind.Utility);
        /// <summary>The factory a rebirth needs complete: everything except the rebirth, the landmarks and the decorations.</summary>
        public static ulong BaseMask { get; } = AllButRebirth & ~LandmarkMask & ~DecorMask;
        /// <summary>Items in <see cref="BaseMask"/>: the "x/22 built" of the build plan.</summary>
        public static int BaseCount { get; } = OwnedCount(BaseMask);
        public static int LandmarkCount { get; } = OwnedCount(LandmarkMask);

        /// <summary>Every item of one lane.</summary>
        public static ulong KindMask(TY_ItemKind kind) {
            ulong mask = 0;
            for (int i = 0; i < Items.Length; i++) if (Items[i].kind == kind) mask |= 1ul << i;
            return mask;
        }

        public static bool Owns(ulong purchased, int index) => index >= 0 && (purchased & (1ul << index)) != 0;

        /// <summary>Bought items in <paramref name="purchased"/> (the rebirth bit never counts).</summary>
        public static int OwnedCount(ulong purchased) {
            int n = 0;
            for (ulong m = purchased & AllButRebirth; m != 0; m &= m - 1) n++;
            return n;
        }

        /// <summary>Bought items of one lane in <paramref name="purchased"/>.</summary>
        public static int OwnedOfKind(ulong purchased, TY_ItemKind kind) => OwnedCount(purchased & KindMask(kind));

        /// <summary>
        /// Whether item <paramref name="index"/> can be bought now by an owner with <paramref name="rebirths"/>
        /// whose plot has <paramref name="purchased"/>. The rebirth needs the whole factory (<see cref="BaseMask"/>).
        /// </summary>
        public static bool Available(ulong purchased, int index, int rebirths) {
            if (index < 0 || index >= Count || Owns(purchased, index)) return false;
            var item = Items[index];
            if (item.IsRepeatable) return (purchased & BaseMask) == BaseMask;
            if (rebirths < item.rebirths) return false;
            return item.prereq < 0 || Owns(purchased, item.prereq);
        }

        /// <summary>
        /// Why an item is not for sale yet, for pads and build cards ("Unlocks at rebirth 2", "Needs Brick
        /// Press"), or "" when it is. A missing rebirth is named first: that is the goal it gives.
        /// </summary>
        public static string LockReason(ulong purchased, int index, int rebirths) {
            if (Available(purchased, index, rebirths) || index < 0 || index >= Count) return "";
            var item = Items[index];
            if (item.IsRepeatable) return "Build everything first";
            if (rebirths < item.rebirths) return $"Unlocks at rebirth {item.rebirths}";
            return item.prereq >= 0 && !Owns(purchased, item.prereq) ? $"Needs {Items[item.prereq].name}" : "";
        }

        /// <summary>The next item of a lane that isn't bought yet (available or still locked), or -1 when the lane is done.</summary>
        public static int NextInLane(ulong purchased, TY_ItemKind kind) {
            for (int i = 0; i < Count; i++)
                if (Items[i].kind == kind && !Owns(purchased, i)) return i;
            return -1;
        }

        /// <summary>Items for sale now, the rebirth included; the investment is left out (it is optional, never the next step).</summary>
        public static IEnumerable<int> AvailableItems(ulong purchased, int rebirths) {
            for (int i = 0; i < Count; i++) if (i != InvestIndex && Available(purchased, i, rebirths)) yield return i;
        }

        /// <summary>
        /// The item a lane's buy pad offers, or -1 for none: the rebirth and the investment only while they
        /// are for sale, every other lane its next item (possibly still locked, so the pad says why).
        /// </summary>
        public static int PadItem(ulong purchased, TY_ItemKind lane, int rebirths) {
            int repeatable = lane == TY_ItemKind.Rebirth ? RebirthIndex : lane == TY_ItemKind.Invest ? InvestIndex : -1;
            if (repeatable >= 0) return Available(purchased, repeatable, rebirths) ? repeatable : -1;
            return NextInLane(purchased, lane);
        }

        /// <summary>The landmark that rebirth number <paramref name="rebirth"/> opens, or -1.</summary>
        public static int LandmarkOpenedBy(int rebirth) {
            for (int i = 0; i < Count; i++)
                if (Items[i].kind == TY_ItemKind.Landmark && Items[i].rebirths == rebirth) return i;
            return -1;
        }

        /// <summary>What a plot keeps through a rebirth: the starter, every landmark and decoration, and (Head Office) the collector lane.</summary>
        public static ulong RebirthKeeps(ulong purchased, bool keepCollectorLane) =>
            StarterMask | (purchased & (LandmarkMask | DecorMask)) | (keepCollectorLane ? purchased & UtilityMask : 0ul);

        // ---------------------------------------------------------------- prices
        /// <summary>
        /// What item <paramref name="index"/> costs an owner with <paramref name="rebirths"/> and
        /// <paramref name="invested"/> investment levels: the rebirth and the investment get dearer each time.
        /// </summary>
        public static long Price(int index, int rebirths, int invested = 0) =>
            index == RebirthIndex ? RebirthPrice(rebirths) : index == InvestIndex ? InvestPrice(invested, rebirths) : Items[index].price;

        /// <summary>The price of the next rebirth for a player who has done <paramref name="rebirths"/> of them.</summary>
        public static long RebirthPrice(int rebirths) =>
            RoundPrice(Items[RebirthIndex].price * Math.Pow(RebirthPriceGrowth, Mathf.Max(0, rebirths)));

        /// <summary>Each investment level costs this many times the one before.</summary>
        public const double InvestPriceGrowth = 1.8;
        /// <summary>
        /// The first investment level costs this share of the owner's next rebirth. Levels are never reset, so a
        /// later level's price follows both the level and the rebirth count.
        /// </summary>
        public const double InvestRebirthShare = 0.5;

        /// <summary>
        /// The next investment for an owner with <paramref name="level"/> of them and <paramref name="rebirths"/>:
        /// half the next rebirth, growing with every level, so it stays a choice against rebirthing and late
        /// cash always has a use.
        /// </summary>
        public static long InvestPrice(int level, int rebirths) =>
            RoundPrice(RebirthPrice(rebirths) * InvestRebirthShare * Math.Pow(InvestPriceGrowth, Math.Max(0, level)));

        /// <summary>The income multiplier of <paramref name="level"/> investments (+5% each, added up).</summary>
        public static double InvestMultiplier(int level) => 1.0 + Items[InvestIndex].value * Math.Max(0, level);

        /// <summary>Rounds a scaled price to <see cref="PriceDigits"/> significant digits, so it reads cleanly ($88K, $1.5M).</summary>
        public static long RoundPrice(double price) {
            if (double.IsNaN(price) || price <= 0) return 0;
            const double max = long.MaxValue / 4d;
            if (price >= max) return (long)max;
            double step = Math.Pow(10, Math.Max(0, Math.Floor(Math.Log10(price)) - (PriceDigits - 1)));
            return (long)(Math.Round(price / step) * step);
        }

        /// <summary>The effect line for item <paramref name="index"/> on a pad or card (a rebirth names what it brings).</summary>
        public static string Effect(int index, int rebirths) {
            if (index != RebirthIndex) return Items[index].Effect;
            string income = $"x{RebirthMultiplier(rebirths + 1):0.#} income";
            return LandmarkOpenedBy(rebirths + 1) >= 0 ? income + ", new landmark" : income + ", +token";
        }

        // ---------------------------------------------------------------- economy
        public static double RebirthMultiplier(int rebirths) => 1.0 + RebirthBonus * Mathf.Max(0, rebirths);

        /// <summary>Product of every bought upgrader that bricks pass on the way to the furnace.</summary>
        public static double UpgradeMultiplier(ulong purchased) => Product(purchased, TY_ItemKind.Upgrader);

        /// <summary>Product of every built landmark's multiplier.</summary>
        public static double LandmarkMultiplier(ulong purchased) => Product(purchased, TY_ItemKind.Landmark);

        static double Product(ulong purchased, TY_ItemKind kind) {
            double m = 1;
            for (int i = 0; i < Count; i++)
                if (Items[i].kind == kind && Owns(purchased, i)) m *= Items[i].value;
            return m;
        }

        /// <summary>Sum of the income bonuses of every bought building (0.25 = +25%).</summary>
        public static double BuildingBonus(ulong purchased) {
            double b = 0;
            for (int i = 0; i < Count; i++)
                if (Items[i].kind == TY_ItemKind.Building && Owns(purchased, i)) b += Items[i].value;
            return b;
        }

        /// <summary>Cash per second of the droppers alone, before any multiplier.</summary>
        public static double BaseIncome(ulong purchased) {
            double perSecond = 0;
            for (int i = 0; i < Count; i++)
                if (Items[i].kind == TY_ItemKind.Dropper && Owns(purchased, i)) perSecond += Items[i].value / Items[i].interval;
            return perSecond;
        }

        /// <summary>Every multiplier on top of <see cref="BaseIncome"/>.</summary>
        public static double Multiplier(ulong purchased, int rebirths, TY_PerkLevels perks) =>
            UpgradeMultiplier(purchased) * (1.0 + BuildingBonus(purchased)) * RebirthMultiplier(rebirths)
            * TY_Perks.IncomeMultiplier(perks) * LandmarkMultiplier(purchased);

        /// <summary>Cash per second a plot earns into its collector.</summary>
        public static double Income(ulong purchased, int rebirths, TY_PerkLevels perks) =>
            BaseIncome(purchased) * Multiplier(purchased, rebirths, perks);

        /// <summary>The most a collector holds; earnings past it are lost until the owner collects.</summary>
        public static long Capacity(ulong purchased, TY_PerkLevels perks) {
            double cap = BaseCapacity;
            for (int i = 0; i < Count; i++)
                if (Items[i].kind == TY_ItemKind.Utility && Owns(purchased, i) && Items[i].value > cap) cap = Items[i].value;
            return (long)(cap * TY_Perks.CapacityMultiplier(perks));
        }

        public static bool AutoCollects(ulong purchased) {
            for (int i = 0; i < Count; i++)
                if (Items[i].IsAutoCollector && Owns(purchased, i)) return true;
            return false;
        }

        /// <summary>Distance along the belt (from its start) where an item sits, for brick upgrades on clients.</summary>
        public static float BeltDistance(Vector3 plotPosition) => plotPosition.x - BeltStartX;

        /// <summary>Short money text: 950, 12.5K, 3.2M.</summary>
        public static string Money(double amount) => RyanAssets.Rounds.RoundFormat.Money(amount);
    }
}
