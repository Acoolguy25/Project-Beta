using UnityEngine;
using System;

namespace RyanAssets.Input {
    public class PromptControls: MonoBehaviour {
        public static Action confirmEvent, denyEvent;
        public void ConfirmPrompt(){
            confirmEvent?.Invoke();
        }
        public void DenyPrompt(){
            denyEvent?.Invoke();
        }
    }
}