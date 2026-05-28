using TMPro;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.Chat {
    [CreateAssetMenu(menuName = "Tools/Chat Validator")]
    public class ChatValidator_TMP_Input : TMP_InputValidator {
        public override char Validate(ref string text, ref int pos, char ch) {
            if (ch == ' ') {
                if (pos > 0 && text[pos - 1] == ' ')
                    return '\0';
            }

            text = text.Insert(pos, ch.ToString());
            pos++;

            return ch;
        }
    }
}