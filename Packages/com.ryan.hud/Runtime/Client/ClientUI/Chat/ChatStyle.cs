using RyanAssets.Shared.Declarations;
using RyanAssets.UI;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.Chat {
    /// <summary>Formats chat lines as TextMeshPro rich text, using the active <see cref="UITheme"/>.</summary>
    public static class ClientChatHelper {
        /// <summary>A player's line: bold, name-coloured username followed by the escaped message.</summary>
        public static string FormatPlayerMessage(string username, string message) {
            Color nameColor = UITheme.Active.NameColor(username);
            return $"<b><color=#{UITheme.ToRichTextHex(nameColor)}>{EscapeRichText(username)}</color></b>  {EscapeRichText(message)}";
        }

        /// <summary>
        /// A line from the game rather than a player. Server text may carry its own rich text, except
        /// chatbot replies, which repeat what players typed and are escaped.
        /// </summary>
        public static string FormatSystemMessage(string message, SystemMessageSource source) {
            UITheme theme = UITheme.Active;
            (Color color, bool escape) = source switch {
                SystemMessageSource.PlayerAdd or SystemMessageSource.PlayerRemove => (theme.textSecondary, false),
                SystemMessageSource.LocalPlayerJoinMessage => (theme.textMuted, false),
                SystemMessageSource.ChatbotResponse => (theme.warning, true),
                SystemMessageSource.CommandError => (theme.danger, false),
                _ => (theme.systemText, false),
            };
            string body = escape ? EscapeRichText(message) : message;
            return $"<color=#{UITheme.ToRichTextHex(color)}>{body}</color>";
        }

        public static string EscapeRichText(string text) {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            // <noparse> keeps tags literal; closing tags typed by the player are neutralised first.
            return "<noparse>" + text.Replace("</noparse>", "</ noparse>") + "</noparse>";
        }
    }
}
