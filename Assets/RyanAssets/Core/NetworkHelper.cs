using FishNet;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Core {
    //public class MyServerRpcAttribute : Attribute
    public static class NetworkHelper {
        public static float ServerTime => GetServerTime();
        public static float GetServerTime() {
            return (float) FishNet.InstanceFinder.TimeManager.TicksToTime(FishNet.InstanceFinder.TimeManager.Tick);
        }
    }
}