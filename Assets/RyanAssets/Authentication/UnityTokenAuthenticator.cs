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
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace RyanAssets.Authentication {
    public sealed class UnityTokenAuthenticator : Authenticator {
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        public override void InitializeOnce(NetworkManager networkManager) {
            base.InitializeOnce(networkManager);

#if UNITY_SERVER
            IsShuttingDown = false;
            KickPlayers = new();
            // Debug.Log("Initialize Auth Server");
            NetworkManager.ServerManager.RegisterBroadcast<AuthRequest>(OnAuthRequest, false);
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
                Token = token
            });
        }
#else
        public static event Action<NetworkConnection, JObject> OnAuthenticationSucceeded;
        public static Dictionary<string, string> KickPlayers;
        public static bool IsShuttingDown;
        private void OnAuthRequest(NetworkConnection conn, AuthRequest req, Channel channel) {
            // Debug.Log("Received OnAuthRequest");
            if (conn.IsAuthenticated) {
                Debug.LogWarning($"Received authentication req from {conn}, kicking..");
                conn.Disconnect(true);
                return;
            }

            _ = ValidateToken(conn, req.Token);
        }
        private async Task ValidateToken(NetworkConnection conn, string token) {
            if (IsShuttingDown) {
                Fail(conn, "This server is shutting down!");
                return;
            } else if (string.IsNullOrWhiteSpace(token)) {
                Fail(conn, "No access token provided");
                return;
            } else if (KickPlayers.TryGetValue(token, out string KickReason)) {
                Fail(conn, KickReason != null ? KickReason : "You do not have permission to join this server");
                return;
            }
            var (res, json) = await BackendNetwork.PostRequest("/api/internal/v1/user/add", accessToken: token);

            if (res == null) {
                NetworkManager.ServerManager.Broadcast(conn, new AuthResponse {
                    Success = true
                }, false);

                OnAuthenticationResult?.Invoke(conn, true);
                OnAuthenticationSucceeded?.Invoke(conn, json);
            } else {
                Fail(conn, res);
            }
        }
        private void Fail(NetworkConnection conn, string reason) {
            Debug.Log("Auth Failed: " + reason);

            NetworkManager.ServerManager.Broadcast(conn, new AuthResponse {
                Success = false,
                Reason = reason
            }, false);

            OnAuthenticationResult?.Invoke(conn, false);
            conn.Disconnect(false);
        }
#endif

        private void OnAuthResponse(AuthResponse res, Channel channel) {
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
    }

    public struct AuthResponse : IBroadcast {
        public bool Success;
        public string Reason;
    }
}
