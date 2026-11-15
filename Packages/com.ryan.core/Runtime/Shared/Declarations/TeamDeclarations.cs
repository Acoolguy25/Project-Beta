using System.Collections;
using UnityEngine;

using UnityEngine.Serialization;

namespace RyanAssets.Shared.Declarations {
    public enum TeamColor : short {
        None = 0,
        White, // Spectator
        Blue, // Sheriff
        Red,  // Murderer
        Green, // Innocent
        Orange,
        Purple,
        Cyan,
        Pink,
        Lime,
        Black,
        Grey,
        Teal,
        Yellow
    };

    [System.Serializable]
    public class TeamConfig {
        [SerializeField, FormerlySerializedAs("team")]
        public TeamColor realTeam = TeamColor.White;
        [SerializeField]
        public TeamColor displayTeam = TeamColor.White;
        public TeamConfig() {

        }
        public TeamConfig(TeamColor realTeam) {
            this.realTeam = realTeam;
            this.displayTeam = realTeam;
        }
        public TeamConfig(TeamColor realTeam, TeamColor displayTeam) {
            this.realTeam = realTeam;
            this.displayTeam = displayTeam;
        }
        public static Color32 TeamToColor(TeamColor teamColor) {
            return teamColor switch {
                TeamColor.White => Color.white,
                TeamColor.Blue => Color.blue,
                TeamColor.Red => Color.red,
                TeamColor.Green => Color.green,
                TeamColor.Orange => new Color32(255, 184, 51, 255),
                TeamColor.Purple => new Color32(184, 97, 255, 255),
                TeamColor.Cyan => new Color32(26, 230, 224, 255),
                TeamColor.Pink => new Color32(255, 92, 194, 255),
                TeamColor.Lime => new Color32(184, 209, 51, 255),
                TeamColor.Black => new Color32(26, 26, 26, 255),
                TeamColor.Grey => new Color32(153, 153, 153, 255),
                TeamColor.Teal => new Color32(0, 255, 255, 255),
                TeamColor.Yellow => new Color32(255, 235, 4, 255),
                TeamColor.None => Color.white,
                _ => Color.white
            };
        }
        public static string ColorRichText(string text, Color32 color) {
            string hex = $"{color.r:X2}{color.g:X2}{color.b:X2}";
            return $"<color=#{hex}>{EscapeRichText(text)}</color>";
        }
        static string EscapeRichText(string text) {
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
        public Color32 realTeamColor => TeamToColor(realTeam);
        public Color32 displayTeamColor => TeamToColor(displayTeam);
    };
}
