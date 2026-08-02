using FishNet;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Core {
    //public class MyServerRpcAttribute : Attribute
    public static class NetworkHelper {
        public static float GetServerTime() {
            return FishNet.InstanceFinder.TimeManager.Tick / (float) FishNet.InstanceFinder.TimeManager.TickRate;
        }
    }
}