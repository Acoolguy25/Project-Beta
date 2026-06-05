using UnityEngine;
using System;

namespace RyanAssets.Input {
    public class MenuControls: MonoBehaviour {
        public static Action menuToggledEvent, playerListEvent, chatActivateEvent;
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