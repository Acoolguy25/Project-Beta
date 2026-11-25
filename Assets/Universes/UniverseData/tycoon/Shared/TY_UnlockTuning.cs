using System;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// How strong each tycoon level unlock is (see <see cref="TY_Retention"/> for the levels). Serialized on
    /// the server runner; the unlock descriptions players read are written from these values.
    /// </summary>
    [Serializable]
    public sealed class TY_UnlockTuning {
        [Tooltip("Lucky Coins: gold coins pay this many times as much.")]
        [Min(1f)] public float luckyCoinsMultiplier = 1.5f;
        [Tooltip("Big Hopper: collectors hold this many times as much.")]
        [Min(1f)] public float bigHopperCapacity = 1.5f;
        [Tooltip("Coin Rain: the wait between gold coins is multiplied by this (0.5 = twice as often).")]
        [Range(0.1f, 1f)] public float coinRainInterval = 0.5f;
        [Tooltip("Rush VIP: added to the rush-hour multiplier for this player (x2 becomes x3).")]
        [Min(0f)] public float rushVipBonus = 1f;
        [Tooltip("Token Press: extra perk tokens per rebirth.")]
        [Min(0)] public int tokenPressTokens = 1;
        [Tooltip("Tycoon Legend: permanent income multiplier.")]
        [Min(1f)] public float legendIncome = 1.25f;

        public void Validate() {
            luckyCoinsMultiplier = Mathf.Max(1f, luckyCoinsMultiplier);
            bigHopperCapacity = Mathf.Max(1f, bigHopperCapacity);
            coinRainInterval = Mathf.Clamp(coinRainInterval, 0.1f, 1f);
            rushVipBonus = Mathf.Max(0f, rushVipBonus);
            tokenPressTokens = Mathf.Max(0, tokenPressTokens);
            legendIncome = Mathf.Max(1f, legendIncome);
        }
    }
}
