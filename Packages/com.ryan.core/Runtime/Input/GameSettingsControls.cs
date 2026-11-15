using UnityEngine;
using System;

namespace RyanAssets.Input {
    public class GameSettingsControls: MonoBehaviour {
        public static Action leaveToggledEvent, resetToggledEvent;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init(){
            leaveToggledEvent = null;
            resetToggledEvent = null;
        }
        public void OnToggleLeave() {
            // Debug.Log("Leave pressed");
            leaveToggledEvent?.Invoke();
        }
        public void OnToggleReset() {
            resetToggledEvent?.Invoke();
        }
    }
}