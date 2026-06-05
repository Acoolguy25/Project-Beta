using UnityEngine;
using System;

namespace RyanAssets.Input {
    public class GameSettingsControls: MonoBehaviour {
        public static Action leaveToggledEvent, resetToggledEvent;
        public void OnToggleLeave() {
            Debug.Log("Leave pressed");
            leaveToggledEvent?.Invoke();
        }
        public void OnToggleReset() {
            resetToggledEvent?.Invoke();
        }
    }
}