using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    public enum TY_ItemKind : byte { Dropper, Upgrader, Decor, Rebirth }

    /// <summary>One purchasable thing on a plot. Positions are in plot space: +Z faces the plaza.</summary>
    public readonly struct TY_Item {
        public readonly string name;
        public readonly long price;
        /// <summary>Index of the item that must be bought first, or -1.</summary>
        public readonly int prereq;
        public readonly TY_ItemKind kind;
        /// <summary>Droppers: cash per ore. Upgraders: ore value multiplier.</summary>
        public readonly double value;
        /// <summary>Droppers: seconds between ores.</summary>
        public readonly float interval;
        public readonly Vector3 itemPosition;
        public readonly Vector3 buttonPosition;

        public TY_Item(string name, long price, int prereq, TY_ItemKind kind, double value, float interval, Vector3 item, Vector3 button) {
            this.name = name; this.price = price; this.prereq = prereq; this.kind = kind;
            this.value = value; this.interval = interval; itemPosition = item; buttonPosition = button;
        }
    }

    /// <summary>
    /// The tycoon's build order and economy. Shared by the server (prices, income, unlocks) and clients
    /// (which buttons to show, where ore drops), so both always agree.
    /// </summary>
    public static class TY_Catalog {
        public const float PlotSize = 40f;
        public const float BeltZ = -4f, BeltStartX = -17f, BeltEndX = 15f, BeltSpeed = 4f, BeltTop = 0.9f;
        public const float RebirthBonus = 0.5f;
        public static readonly Vector3 FurnacePosition = new(17f, 0f, BeltZ);
        public static readonly Vector3 CollectPosition = new(15.5f, 0f, 3f);
        public static readonly Vector3 ClaimPosition = new(0f, 0f, 17.5f);
        public static readonly Vector3 SpawnPosition = new(0f, 0f, 9f);
        public static readonly Vector3 ButtonSize = new(2.8f, 1f, 2.8f);
        public static readonly Vector3 PadSize = new(3.4f, 1f, 3.4f);

        static float DropperX(int k) => -15f + k * 3.6f;
        static Vector3 Button(int column, int row) => new(-14f + column * 7f, 0f, 6f + row * 5f);
        static Vector3 Dropper(int k) => new(DropperX(k), 0f, BeltZ);

        public static readonly TY_Item[] Items = {
            new("Starter Dropper", 0, -1, TY_ItemKind.Dropper, 2, 1.5f, Dropper(0), Button(2, 0)),
            new("Dropper 2", 30, 0, TY_ItemKind.Dropper, 3, 1.5f, Dropper(1), Button(1, 0)),
            new("Walls", 60, 0, TY_ItemKind.Decor, 0, 0, Vector3.zero, Button(3, 0)),
            new("Dropper 3", 90, 1, TY_ItemKind.Dropper, 5, 1.5f, Dropper(2), Button(1, 0)),
            new("Upgrader", 220, 3, TY_ItemKind.Upgrader, 1.5, 0, new Vector3(9.5f, 0f, BeltZ), Button(0, 0)),
            new("Lamp Posts", 150, 2, TY_ItemKind.Decor, 0, 0, Vector3.zero, Button(3, 1)),
            new("Dropper 4", 450, 4, TY_ItemKind.Dropper, 12, 2f, Dropper(3), Button(0, 1)),
            new("Front Gate", 300, 2, TY_ItemKind.Decor, 0, 0, new Vector3(0f, 0f, 19.5f), Button(4, 0)),
            new("Dropper 5", 900, 6, TY_ItemKind.Dropper, 20, 2f, Dropper(4), Button(1, 1)),
            new("Garden", 600, 5, TY_ItemKind.Decor, 0, 0, new Vector3(-13f, 0f, -13f), Button(4, 1)),
            new("Washer", 1600, 8, TY_ItemKind.Upgrader, 2, 0, new Vector3(12.5f, 0f, BeltZ), Button(0, 2)),
            new("Dropper 6", 3000, 10, TY_ItemKind.Dropper, 40, 2f, Dropper(5), Button(1, 2)),
            new("Lookout Tower", 2500, 7, TY_ItemKind.Decor, 0, 0, new Vector3(13f, 0f, -14f), Button(4, 2)),
            new("Mega Dropper", 6000, 11, TY_ItemKind.Dropper, 120, 3f, Dropper(6), Button(2, 1)),
            new("Golden Statue", 12000, 13, TY_ItemKind.Decor, 0, 0, new Vector3(0f, 0f, -14f), Button(2, 2)),
            new("REBIRTH", 20000, 14, TY_ItemKind.Rebirth, 0, 0, Vector3.zero, Button(3, 2)),
        };

        public static int Count => Items.Length;
        public static int RebirthIndex { get; } = System.Array.FindIndex(Items, i => i.kind == TY_ItemKind.Rebirth);
        public static uint StarterMask => 1u;
        public static uint AllButRebirth => (1u << Count) - 1u & ~(1u << RebirthIndex);

        public static bool Owns(uint purchased, int index) => index >= 0 && (purchased & (1u << index)) != 0;

        /// <summary>Whether item <paramref name="index"/> has a visible buy button for a plot with <paramref name="purchased"/>.</summary>
        public static bool Available(uint purchased, int index) {
            if (index < 0 || index >= Count || Owns(purchased, index)) return false;
            var item = Items[index];
            if (item.kind == TY_ItemKind.Rebirth) return (purchased & AllButRebirth) == AllButRebirth;
            return item.prereq < 0 || Owns(purchased, item.prereq);
        }

        public static double RebirthMultiplier(int rebirths) => 1.0 + RebirthBonus * Mathf.Max(0, rebirths);

        /// <summary>Product of every bought upgrader that ore passes on the way to the furnace.</summary>
        public static double UpgradeMultiplier(uint purchased) {
            double m = 1;
            for (int i = 0; i < Count; i++)
                if (Items[i].kind == TY_ItemKind.Upgrader && Owns(purchased, i)) m *= Items[i].value;
            return m;
        }

        /// <summary>Cash per second a plot earns into its collector.</summary>
        public static double Income(uint purchased, int rebirths) {
            double perSecond = 0;
            for (int i = 0; i < Count; i++)
                if (Items[i].kind == TY_ItemKind.Dropper && Owns(purchased, i)) perSecond += Items[i].value / Items[i].interval;
            return perSecond * UpgradeMultiplier(purchased) * RebirthMultiplier(rebirths);
        }

        public static IEnumerable<int> AvailableItems(uint purchased) {
            for (int i = 0; i < Count; i++) if (Available(purchased, i)) yield return i;
        }

        /// <summary>Distance along the belt (from its start) where an item sits, for ore upgrades on clients.</summary>
        public static float BeltDistance(Vector3 plotPosition) => plotPosition.x - BeltStartX;
    }
}
