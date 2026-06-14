using NativeWebSocket;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using RyanAssets.Core;

namespace RyanAssets.NetworkService {
    public class BackendSocket : MonoBehaviour {
        public static BackendSocket Instance;
        static string base_address;
        readonly List<WebSocket> active_sockets = new();
        static Dictionary<string, string> socket_headers = new();
        void Awake() {
            Instance = this;
        }
        void Update() {
            foreach (WebSocket socket in active_sockets) {
                socket.DispatchMessageQueue();
            }
        }
        (string, JObject) OnSocketMessage(byte[] bytes) {
            string data = Encoding.UTF8.GetString(bytes);
            JObject j = BackendNetwork.ParseJSON(data);
            if (j == null)
                return (data, null);
            return (null, j);
        }
        public async void StartSocket(string url, Action<(string, JObject)> onMessage = null, Action onClose = null, Dictionary<string, string> headers = null) {
            for (int i = 0; /* no condition */; i++) { 
                try {
                    //Debug.Log($"URL: ws://{base_address}{url}");
                    WebSocket ws = new($"ws://{base_address}{url}", headers != null ? headers : socket_headers);
                    ws.OnMessage += (bytes) => onMessage?.Invoke(OnSocketMessage(bytes));
                    ws.OnClose += (_) => {
                        active_sockets.Remove(ws);
                        onClose?.Invoke();
                    };
                    ws.OnError += (e) => onMessage?.Invoke(($"Socket Error: {e}", null));
                    active_sockets.Add(ws);
                    await ws.Connect();
                    break;
                } catch (Exception ex) {
                    Debug.LogError(ex);
                    await Task.Delay(TimeSpan.FromSeconds(RequestHelper.GetRetryDelay(i)));
                }
            }
        }
        public static void SetBaseAddress(string address) {
            base_address = address;
        }
#if UNITY_SERVER
        public static void SetServerHeader(string header_name, string header_value) {
            socket_headers[header_name] = header_value;
        }
#endif
        void OnDestroy() {
            foreach (var ws in active_sockets) {
                _ = ws.Close();
            }
        }
    }
}