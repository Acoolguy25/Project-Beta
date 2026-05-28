using UnityEngine;
namespace RyanAssets.Client.ClientUI.Chat {
    public static class ClientChatHelper {
        static readonly Color32[] Colors =
        {
            new(253,  41,  67, 255),
            new(  1, 162, 255, 255),
            new(  2, 184,  87, 255),
            new(107,  50, 124, 255),
            new(218, 133,  65, 255),
            new(245, 205,  48, 255),
            new(232, 186, 200, 255),
            new(215, 197, 154, 255),
        };

        public static Color32 GetColor(string name) {
            int value = 0;
            int len = name.Length;

            for (int i = 0; i < len; i++) {
                int cValue = name[i];
                int reverseIndex = len - i;

                if (len % 2 == 1)
                    reverseIndex--;

                if (reverseIndex % 4 >= 2)
                    cValue = -cValue;

                value += cValue;
            }

            int index = ((value % Colors.Length) + Colors.Length) % Colors.Length;
            return Colors[index];
        }

        public static string ColorNameRichText(string name) {
            Color32 c = GetColor(name);
            string hex = $"{c.r:X2}{c.g:X2}{c.b:X2}";
            return $"<color=#{hex}>{EscapeRichText(name)}</color>";
        }

        public static string EscapeRichText(string s) {
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}