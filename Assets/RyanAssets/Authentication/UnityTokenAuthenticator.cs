using FishNet.Authenticating;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
#if !UNITY_SERVER
using RyanAssets.PromptService;
#endif
using RyanAssets.NetworkService;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RyanAssets.Authentication {
    public sealed class UnityTokenAuthenticator : Authenticator {
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        public override void InitializeOnce(NetworkManager networkManager) {
            base.InitializeOnce(networkManager);

            NetworkManager.ServerManager.RegisterBroadcast<AuthRequest>(OnAuthRequest);
            NetworkManager.ClientManager.RegisterBroadcast<AuthResponse>(OnAuthResponse);

            NetworkManager.ClientManager.OnClientConnectionState += ClientConnectionState;
        }

        public override void OnRemoteConnection(NetworkConnection conn) {
            // Wait for AuthRequest.
        }

        private void ClientConnectionState(ClientConnectionStateArgs args) {
            if (args.ConnectionState != LocalConnectionState.Started)
                return;

            string token = Unity.Services.Authentication.AuthenticationService.Instance.AccessToken;

            NetworkManager.ClientManager.Broadcast(new AuthRequest {
                Token = token
            });
        }

        private void OnAuthRequest(NetworkConnection conn, AuthRequest req, Channel channel) {
            Task.Run(() => ValidateToken(conn, req.Token));
        }

        private async Task ValidateToken(NetworkConnection conn, string token) {
            if (string.IsNullOrWhiteSpace(token)) {
                Fail(conn, "No access token provided");
                return;
            }

            (string res, JObject json) = await BackendNetwork.PostRequest("/api/internal/v1/user/register");

            if (res == null) {
                OnAuthenticationResult?.Invoke(conn, true);

                NetworkManager.ServerManager.Broadcast(conn, new AuthResponse {
                    Success = true
                });
            } else {
                Fail(conn, res);
            }
        }

        private void Fail(NetworkConnection conn, string reason) {
            OnAuthenticationResult?.Invoke(conn, false);

            NetworkManager.ServerManager.Broadcast(conn, new AuthResponse {
                Success = false,
                Reason = reason
            });

            conn.Disconnect(true);
        }

        private void OnAuthResponse(AuthResponse res, Channel channel) {
            if (res.Success) {
                Debug.Log("Authentcation Succeeded!");
            } else {
                NetworkManager.ClientManager.StopConnection();
                #if !UNITY_SERVER
                    PromptManager.Instance.PromptLocalUser("Authentication Failed", res.Reason, PromptId.Protected, PromptManager.ButtonPreset_OkOnly);
                #endif
                Debug.Log("Authentication Failed: " + res.Reason);
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