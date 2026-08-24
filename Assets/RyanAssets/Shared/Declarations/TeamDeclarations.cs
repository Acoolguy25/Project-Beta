using System.Collections;
using UnityEngine;

namespace RyanAssets.Shared.Declarations {
    public enum TeamColor : short {
        None = 0,
        Ghost, // Spectator
        Lobby, // Lobby / Spectator
        Blue, // Sheriff
        Red,  // Murderer
        Green // Innocent
    };
    [System.Serializable]
    public class TeamConfig {
        [SerializeField]
        public TeamColor team = TeamColor.Ghost;
        [SerializeField]
        public TeamColor displayTeam = TeamColor.Ghost;
        public TeamConfig() {

        }
        public TeamConfig(TeamColor team) {
            this.team = team;
            this.displayTeam = team;
        }
        public TeamConfig(TeamColor team, TeamColor displayTeam) {
            this.team = team;
            this.displayTeam = displayTeam;
        }
        public static Color32 TeamToColor(TeamColor teamColor) {
            return teamColor switch {
                TeamColor.Ghost or TeamColor.Lobby => Color.grey,
                TeamColor.Blue => Color.blue,
                TeamColor.Red => Color.red,
                TeamColor.Green => Color.green,
                TeamColor.None => Color.white,
                _ => Color.white
            };
        }
        public Color32 realTeamColor => TeamToColor(team);
        public Color32 displayTeamColor => TeamToColor(displayTeam);
    };
}