using UnityEngine;
using System;

namespace RyanAssets.Input {
    public class PromptControls: MonoBehaviour {
        public static Action confirmEvent, denyEvent;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init(){
            confirmEvent = null;
            denyEvent = null;
        }
        public void OnConfirmPrompt(){
            confirmEvent?.Invoke();
        }
        public void OnDenyPrompt(){
            denyEvent?.Invoke();
        }
    }
}