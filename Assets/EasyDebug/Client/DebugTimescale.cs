using System.Collections;
using UnityEngine;

namespace EasyDebug.Client.Assets.EasyDebug.Client {
    public class DebugTimeScale : MonoBehaviour {
        [SerializeField]
        float SetTimeScale = 0.1f;
        void Update() {
            Time.timeScale = SetTimeScale;
        }
    }
}