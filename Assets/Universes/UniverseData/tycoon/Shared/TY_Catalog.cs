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

        public TY_Item(string name, long price, int prereq, TY_ItemKind kind, double value, float interval, Vector3 item, float yaw = 0f) {
            this.name = name; this.price = price; this.prereq = prereq; this.kind = kind;
            this.value = value; this.interval = interval; itemPosition = item; itemYaw = yaw;
        }

        public bool IsAutoCollector => kind == TY_ItemKind.Utility && value <= 0;

        /// <summary>One-line effect shown on buy pads and build cards.</summary>
        public string Effect => kind switch {
            TY_ItemKind.Dropper => $"+${value / interval:0.#}/s",
            TY_ItemKind.Upgrader => $"Bricks x{value:0.##}",
            TY_ItemKind.Building => $"+{value * 100:0}% income",
            TY_ItemKind.Utility => IsAutoCollector ? "Auto-collects cash" : $"Holds ${TY_Catalog.Money(value)}",
            TY_ItemKind.Rebirth => $"x{1 + TY_Catalog.RebirthBonus:0.#} income, +1 token",
            _ => "",
        };
    }

    /// <summary>
    /// The tycoon's build order and economy. Shared by the server (prices, income, unlocks) and clients
    /// (which pads to show, where bricks drop, what the HUD promises), so both always agree.
    /// <para>
    /// Items are grouped in lanes by <see cref="TY_ItemKind"/>. Each lane is a chain (every item needs
    /// the previous one in its lane), so a lane only ever shows one buy pad at a time; some links
    /// also need an item from another lane, which paces the build.
    /// </para>
    /// </summary>
    public static class TY_Catalog {
        public const float PlotSize = 40f;
        public const float BeltZ = -4f, BeltStartX = -17f, BeltEndX = 15f, BeltSpeed = 4f, BeltTop = 0.9f;
        /// <summary>Income multiplier each rebirth adds.</summary>
        public const float RebirthBonus = 0.5f;
        /// <summary>What a collector holds before any vault is bought.</summary>
        public const long BaseCapacity = 400;

        public static readonly Vector3 FurnacePosition = new(17f, 0f, BeltZ);
        public static readonly Vector3 CollectPosition = new(15.5f, 0f, 1f);
        public static readonly Vector3 ClaimPosition = new(0f, 0f, 17.5f);
        public static readonly Vector3 SpawnPosition = new(0f, 0f, 10f);
        public static readonly Vector3 ButtonSize = new(3.2f, 1f, 3.2f);
        public static readonly Vector3 PadSize = new(3.6f, 1f, 3.6f);
        /// <summary>Area (plot space, x/z min-max) where gold coins can appear.</summary>
        public static readonly Rect GoldenArea = new(-16f, 0f, 30f, 15f);

        /// <summary>Where each lane's buy pad sits (plot space).</summary>
        public static Vector3 LanePosition(TY_ItemKind kind) => kind switch {
            TY_ItemKind.Dropper => new Vector3(-13.5f, 0f, 6f),
            TY_ItemKind.Upgrader => new Vector3(-7f, 0f, 6f),
            TY_ItemKind.Building => new Vector3(7f, 0f, 6f),
            TY_ItemKind.Utility => new Vector3(13.5f, 0f, 7f),
            _ => new Vector3(0f, 0f, 14f),
        };

        static Vector3 Dropper(int k) => new(-16f + k * 3.25f, 0f, BeltZ);
        static Vector3 Lot(float x, float z) => new(x, 0f, z);

        // Indices are referenced by prereq; keep the comments in sync when reordering.
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
            /*16*/ new("Golden Statue", 15000, 15, TY_ItemKind.Building, 0.2, 0, Lot(-14f, 15f)),

            /*17*/ new("Vault", 120, 0, TY_ItemKind.Utility, 1500, 0, Lot(17.5f, 4.8f)),
            /*18*/ new("Big Vault", 1000, 17, TY_ItemKind.Utility, 6000, 0, Lot(17.3f, 9.4f)),
            /*19*/ new("Mega Vault", 5000, 18, TY_ItemKind.Utility, 25000, 0, Lot(16.8f, 15f)),
            /*20*/ new("Auto-Collector", 10000, 19, TY_ItemKind.Utility, 0, 0, Lot(18.7f, 1f)),
            /*21*/ new("Bank Tower", 20000, 20, TY_ItemKind.Utility, 120000, 0, Lot(13.5f, -14f)),

            /*22*/ new("REBIRTH", 50000, -1, TY_ItemKind.Rebirth, 0, 0, Vector3.zero),
        };

        public static int Count => Items.Length;
        public static int RebirthIndex { get; } = System.Array.FindIndex(Items, i => i.kind == TY_ItemKind.Rebirth);
        public static ulong StarterMask => 1ul;
        public static ulong AllButRebirth { get; } = ((1ul << Count) - 1ul) & ~(1ul << RebirthIndex);
        /// <summary>Items that need building, i.e. everything except the rebirth.</summary>
        public static int BuildableCount => Count - 1;

        public static bool Owns(ulong purchased, int index) => index >= 0 && (purchased & (1ul << index)) != 0;

        public static int OwnedCount(ulong purchased) {
            int n = 0;
            for (ulong m = purchased & AllButRebirth; m != 0; m &= m - 1) n++;
            return n;
        }

        /// <summary>Whether item <paramref name="index"/> has a visible buy pad for a plot with <paramref name="purchased"/>.</summary>
        public static bool Available(ulong purchased, int index) {
            if (index < 0 || index >= Count || Owns(purchased, index)) return false;
            var item = Items[index];
            if (item.kind == TY_ItemKind.Rebirth) return (purchased & AllButRebirth) == AllButRebirth;
            return item.prereq < 0 || Owns(purchased, item.prereq);
        }

        /// <summary>The next item of a lane that isn't bought yet (available or still locked), or -1 when the lane is done.</summary>
        public static int NextInLane(ulong purchased, TY_ItemKind kind) {
            for (int i = 0; i < Count; i++)
                if (Items[i].kind == kind && !Owns(purchased, i)) return i;
            return -1;
        }

        public static IEnumerable<int> AvailableItems(ulong purchased) {
            for (int i = 0; i < Count; i++) if (Available(purchased, i)) yield return i;
        }

        // ---------------------------------------------------------------- economy
        public static double RebirthMultiplier(int rebirths) => 1.0 + RebirthBonus * Mathf.Max(0, rebirths);

        /// <summary>Product of every bought upgrader that bricks pass on the way to the furnace.</summary>
        public static double UpgradeMultiplier(ulong purchased) {
            double m = 1;
            for (int i = 0; i < Count; i++)
                if (Items[i].kind == TY_ItemKind.Upgrader && Owns(purchased, i)) m *= Items[i].value;
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
            UpgradeMultiplier(purchased) * (1.0 + BuildingBonus(purchased)) * RebirthMultiplier(rebirths) * TY_Perks.IncomeMultiplier(perks);

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
