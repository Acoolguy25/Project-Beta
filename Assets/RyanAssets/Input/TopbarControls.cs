using UnityEngine;
using System;

namespace RyanAssets.Input {
    public class TopbarControls: MonoBehaviour {
        public static Action menuToggledEvent, playerListEvent, chatActivateEvent;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init(){
            menuToggledEvent = null;
            playerListEvent = null;
            chatActivateEvent = null;
        }
        public void OnToggleMenu() {
            menuToggledEvent?.Invoke();
        }
        public void OnActivateChat() {
            chatActivateEvent?.Invoke();
        }
        public void OnTogglePlayerList() {
            playerListEvent?.Invoke();
        }
    }
}