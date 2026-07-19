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
        public static void AwardPlayerXP(NetworkConnection player, ulong xp) {
            PlayerData playerData = PlayerData.GetPlayerData(player);
            if (playerData) {
                int oldLevel = LevelsCalc.GetRank(xp);
                try {
                    playerData.xp.Value = checked(playerData.xp.Value + xp);
                } catch(OverflowException) { 
                    Debug.LogError($"Player {player} would overflow their XP");
                    playerData.xp.Value = ulong.MaxValue;
                }
                ServerPlayerSave.MarkDirty(player);
                int newLevel = LevelsCalc.GetRank(playerData.xp.Value);
                for (int curLevel = oldLevel + 1; curLevel <= newLevel; curLevel++) {
                    ServerChat.SendSystemMessage(new($"You have reached level {curLevel}!", RyanAssets.Shared.Declarations.SystemMessageSource.PlayerLevelUp));
                }
            }
        }
        public static void AwardPlayerGold(NetworkConnection player, ulong gold) {
            PlayerData playerData = PlayerData.GetPlayerData(player);
            if (playerData) {
                try {
                    playerData.gold.Value = checked(playerData.gold.Value + gold);
                }
                catch (OverflowException) {
                    Debug.LogError($"Player {player} would overflow their gold");
                    playerData.gold.Value = ulong.MaxValue;
                }
                ServerPlayerSave.MarkDirty(player);
            }
        }

        public static void AwardPlayerXPAndGold(NetworkConnection player, ulong xp, ulong gold) {
            AwardPlayerGold(player, gold);
            AwardPlayerXP(player, xp);
        }

        public static void AwardPlayerXPAndGold(NetworkConnection player, ulong reward) {
            AwardPlayerXPAndGold(player, reward, reward);
        }
    }
}
