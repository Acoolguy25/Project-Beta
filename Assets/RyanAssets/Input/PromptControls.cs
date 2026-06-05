using UnityEngine;
using System;

namespace RyanAssets.Input {
    public class PromptControls: MonoBehaviour {
        public static Action confirmEvent, denyEvent;
        public void OnConfirmPrompt(){
            confirmEvent?.Invoke();
        }
        public void OnDenyPrompt(){
            denyEvent?.Invoke();
        }
    }
}