using RyanAssets.Shared.Declarations;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.PlayerList {
    public class ClientPlayerListItem : MonoBehaviour {
        [SerializeField]
        Text playerNameText;
        public void UpdatePlayerName(string playerName, TeamColor team) {
            if (team == TeamColor.None)
                playerNameText.text = playerName;
            else if (team == TeamColor.Ghost || team == TeamColor.Lobby)
                playerNameText.text = $"<color=grey>{playerName}</color>";
            else if (team == TeamColor.Blue)
                playerNameText.text = $"<color=blue>{playerName}</color>";
            else if (team == TeamColor.Red)
                playerNameText.text = $"<color=red>{playerName}</color>";
            else if (team == TeamColor.Green)
                playerNameText.text = $"<color=green>{playerName}</color>";
            else
                Debug.LogError($"Unknown team color: {team} for player: {playerName}");
        }
        public void BuildLeaderboard() {

        }
    }
}