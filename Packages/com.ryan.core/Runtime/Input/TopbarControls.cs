using UnityEngine;
using System;

namespace RyanAssets.Input {
    public class TopbarControls: MonoBehaviour {
        public static bool IsMenuOpen;
        public static Action closeToggledEvent, menuToggledEvent, playerListEvent, chatActivateEvent;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init(){
            menuToggledEvent = null;
            playerListEvent = null;
            chatActivateEvent = null;
            IsMenuOpen = false;
        }
        public void OnToggleMenu() {
            if (IsMenuOpen) {
                closeToggledEvent?.Invoke();
                IsMenuOpen = false;
                return;
            }
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