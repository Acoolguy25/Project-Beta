using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Levels.Shared;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using System;
using UnityEngine;

namespace RyanAssets.Levels.Server
{
    public static class LevelsServer
    {
        public static bool AwardPlayerXP(PlayerData playerData, ulong xp) {
            if (xp == 0) {
                Debug.LogWarning($"AwardPlayerXP called with 0 XP for player {playerData.GetPlayerName()}");
                return true;
            }
            int oldLevel = LevelsCalc.GetRank(playerData.xp.Value);
            try {
                playerData.xp.Value = checked(playerData.xp.Value + xp);
            } catch(OverflowException) { 
                Debug.LogError($"Player {playerData.GetPlayerName()} would overflow their XP");
                playerData.xp.Value = ulong.MaxValue;
            }
            ServerPlayerSave.MarkDirty(playerData.Owner);
            int newLevel = LevelsCalc.GetRank(playerData.xp.Value);
            for (int curLevel = oldLevel + 1; curLevel <= newLevel; curLevel++) {
                ServerChat.SendSystemMessage(new($"{playerData.GetPlayerName()} has reached level {curLevel}!", RyanAssets.Shared.Declarations.SystemMessageSource.PlayerLevelUp));
            }
            return true;
        }
        public static bool AwardPlayerXP(NetworkConnection player, ulong xp) {
            if (PlayerData.TryGetPlayerData(player, out PlayerData playerData))
                return AwardPlayerXP(playerData, xp);
            else
                return false;
        }
        public static bool AwardPlayerGold(PlayerData playerData, ulong gold) {
            if (gold == 0) {
                Debug.LogWarning($"AwardPlayerGold called with 0 gold for player {playerData.GetPlayerName()}");
                return true;
            }
            try {
                playerData.gold.Value = checked(playerData.gold.Value + gold);
            }
            catch (OverflowException) {
                Debug.LogError($"Player {playerData.GetPlayerName()} would overflow their gold");
                playerData.gold.Value = ulong.MaxValue;
            }
            ServerPlayerSave.MarkDirty(playerData.Owner);
            return true;
        }
        public static bool AwardPlayerGold(NetworkConnection player, ulong gold) {
            if (PlayerData.TryGetPlayerData(player, out PlayerData playerData))
                return AwardPlayerGold(playerData, gold);
            else
                return false;
        }
        public static bool AwardPlayerXPAndGold(PlayerData playerData, ulong xp, ulong gold) {
            return AwardPlayerGold(playerData, gold) && AwardPlayerXP(playerData, xp);
        }
        public static bool AwardPlayerXPAndGold(NetworkConnection player, ulong xp, ulong gold) {
            if (PlayerData.TryGetPlayerData(player, out PlayerData playerData))
                return AwardPlayerXPAndGold(playerData, xp, gold);
            else
                return false;
        }
        public static bool AwardPlayerXPAndGold(NetworkConnection player, ulong reward) {
            return AwardPlayerXPAndGold(player, reward, reward);
        }
    }
}
