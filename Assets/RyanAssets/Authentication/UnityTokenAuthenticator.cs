using FishNet.Authenticating;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using UnityEngine;
#if !UNITY_SERVER
using RyanAssets.PromptService;
#endif
using RyanAssets.NetworkService;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using FishNet;
using RyanAssets.DataService;

namespace RyanAssets.Authentication {
    public sealed class UnityTokenAuthenticator : Authenticator {
#pragma warning disable CS0414
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;
#pragma warning restore CS0414
        public GameObject playerDataPrefab;
        Dictionary<NetworkConnection, JObject> joinPlayerData = new();
        readonly Dictionary<NetworkConnection, int> pendingAuthentication = new();
        int authenticationSequence;

        public override void InitializeOnce(NetworkManager networkManager) {
            base.InitializeOnce(networkManager);

#if UNITY_SERVER
            IsShuttingDown = false;
            KickPlayers = new();
            // Debug.Log("Initialize Auth Server");
            NetworkManager.ServerManager.RegisterBroadcast<AuthRequest>(OnAuthRequest, false);
            NetworkManager.ServerManager.OnRemoteConnectionState += ServerConnectionState;
#else
            // Debug.Log("Initialize Auth Client");
            NetworkManager.ClientManager.RegisterBroadcast<AuthResponse>(OnAuthResponse);

            NetworkManager.ClientManager.OnClientConnectionState += ClientConnectionState;
#endif
        }

        public override void OnRemoteConnection(NetworkConnection conn) {
            // Wait for AuthRequest.
            //Debug.Log("Initializing Player...");
        }

#if !UNITY_SERVER
        private void ClientConnectionState(ClientConnectionStateArgs args) {
            // Debug.Log("Client state: " + args.ConnectionState);

            if (args.ConnectionState != LocalConnectionState.Started)
                return;

            // Debug.Log("Sending AuthRequest");

            string token = BackendNetwork.GetAuthorizationToken();
            if (string.IsNullOrWhiteSpace(token)) {
                Debug.LogWarning("Cannot authenticate with server because the Unity access token is empty.");
                NetworkManager.ClientManager.StopConnection();
                return;
            }

            NetworkManager.ClientManager.Broadcast(new AuthRequest {
                Token = token,
                ClientVersion = Application.version
            });
        }
#else
        public static event Action<NetworkConnection, PlayerData, JObject> OnAuthenticationSucceeded;
        public static event Action<NetworkConnection, JObject> EarlyPlayerDisconnected;
        public static Dictionary<string, string> KickPlayers;
        public static bool IsShuttingDown;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            OnAuthenticationSucceeded = null;
            EarlyPlayerDisconnected = null;
        }
        private void OnAuthRequest(NetworkConnection conn, AuthRequest req, FishNet.Transporting.Channel channel) {
            // Debug.Log("Received OnAuthRequest");
            if (conn.IsAuthenticated) {
                Debug.LogWarning($"Received authentication req from {conn}, kicking..");
                conn.Disconnect(true);
                return;
            }

            if (pendingAuthentication.ContainsKey(conn)) return;
            int request = ++authenticationSequence;
            pendingAuthentication[conn] = request;
            ValidateToken(conn, req.Token, req.ClientVersion, request).Forget();
        }
        private async UniTask ValidateToken(NetworkConnection conn, string token, string clientVersion, int request) {
            if (IsShuttingDown) {
                Fail(conn, "This server is shutting down!");
                return;
            } else if (string.IsNullOrWhiteSpace(token)) {
                Fail(conn, "No access token provided");
                return;
            } else if (KickPlayers.TryGetValue(token, out string KickReason)) {
                Fail(conn, KickReason != null ? KickReason : "You do not have permission to join this server");
                return;
            } else if (clientVersion != Application.version) {
                Fail(conn, $"Client version mismatch. Server is running {Application.version}, but client is running {clientVersion}");
                return;
            }
            var (res, json) = await BackendNetwork.PostRequest("/api/internal/v1/user/add", accessToken: token);

            // The connection can disconnect or be pooled and reused while the
            // backend request is in flight. Never authenticate that later session.
            if (!pendingAuthentication.TryGetValue(conn, out int activeRequest) || activeRequest != request
                || !NetworkManager.IsServerStarted || !conn.IsValid || !conn.IsActive || conn.Disconnecting) {
                if (res == null) EarlyPlayerDisconnected?.Invoke(conn, json);
                return;
            }
            pendingAuthentication.Remove(conn);

            if (res == null) {
                joinPlayerData[conn] = json;
                conn.OnLoadedStartScenes += OnPlayerAuthenticated;
                NetworkManager.ServerManager.Broadcast(conn, new AuthResponse {
                    Success = true
                }, false);

                OnAuthenticationResult?.Invoke(conn, true);
            } else {
                Fail(conn, res);
            }
        }
        void OnPlayerAuthenticated(NetworkConnection conn, bool isServer) {
            JObject json;
            if (!joinPlayerData.TryGetValue(conn, out json)) {
                Debug.LogError("Player authenticated but no player data found for connection: " + conn);
                return;
            }
            conn.OnLoadedStartScenes -= OnPlayerAuthenticated;

            joinPlayerData.Remove(conn);
            PlayerData playerData = Instantiate(playerDataPrefab).GetComponent<PlayerData>();
            playerData.Deserialize(json);
            InstanceFinder.ServerManager.Spawn(playerData.gameObject, conn);
            OnAuthenticationSucceeded?.Invoke(conn, playerData, json);
        }
        private void Fail(NetworkConnection conn, string reason) {
            pendingAuthentication.Remove(conn);
            if (!conn.IsValid || !conn.IsActive || conn.Disconnecting) return;
            Debug.Log("Auth Failed: " + reason);

            NetworkManager.ServerManager.Broadcast(conn, new AuthResponse {
                Success = false,
                Reason = reason
            }, false);

            OnAuthenticationResult?.Invoke(conn, false);
            if (!conn.Disconnecting)
                conn.Disconnect(false);
        }
        void ServerConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args) {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;
            pendingAuthentication.Remove(conn);
            conn.OnLoadedStartScenes -= OnPlayerAuthenticated; // Emergency cleanup
            if (joinPlayerData.TryGetValue(conn, out JObject json)) {
                joinPlayerData.Remove(conn);
                EarlyPlayerDisconnected?.Invoke(conn, json);
            }
        }
#endif

        private void OnAuthResponse(AuthResponse res, FishNet.Transporting.Channel channel) {
            if (res.Success) {
                // Debug.Log("Authentication Succeeded!");
            } else {
                Debug.Log("Authentication Failed: " + res.Reason);
                NetworkManager.ClientManager.StopConnection();
#if !UNITY_SERVER
                PromptManager.Instance.PromptLocalUser("Authentication Failed", res.Reason, PromptId.AuthenticationFail, PromptManager.ButtonPreset_OkOnly);
#endif
            }
        }
    }

    public struct AuthRequest : IBroadcast {
        public string Token;
        public string ClientVersion;
    }

    public struct AuthResponse : IBroadcast {
        public bool Success;
        public string Reason;
    }
}
