using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Universes.UniverseData.chatbot_service.Server {
    public enum CS_ClaudeModel {
        Haiku_5_5 = 0,
        Sonnet_5_5 = 1,
        Opus_5_5 = 2,
        Fable_5_2 = 3,
        Custom = 100
    }
    public enum CS_Effort {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>
    /// Minimal server-side client for the Anthropic Messages API. Keeps its own conversation history.
    /// The API key is never serialized: it is read from the ANTHROPIC_API_KEY environment variable,
    /// or from a local key file that must stay out of source control.
    /// </summary>
    public class CS_AnthropicClient {
        const string Endpoint = "https://api.anthropic.com/v1/messages";
        const string ApiVersion = "2023-06-01";
        const string FallbackBeta = "server-side-fallback-2026-07-01";

        public string ModelId { get; }
        public string SystemPrompt { get; set; } = "";
        readonly string apiKey;
        readonly string effort;
        readonly int maxTokens;
        readonly int maxHistoryMessages;
        readonly List<JObject> history = new();
        readonly SemaphoreSlim gate = new(1, 1);
        CancellationTokenSource resetCts = new();

        public CS_AnthropicClient(string modelId, CS_Effort effort, int maxTokens, int maxHistoryMessages, string apiKeyFilePath) {
            ModelId = modelId;
            this.effort = effort.ToString().ToLowerInvariant();
            this.maxTokens = maxTokens;
            this.maxHistoryMessages = Mathf.Max(2, maxHistoryMessages);
            apiKey = ResolveApiKey(apiKeyFilePath);
        }

        public bool HasApiKey => !string.IsNullOrEmpty(apiKey);

        public static string ToModelId(CS_ClaudeModel model, string customModelId) => model switch {
            CS_ClaudeModel.Haiku_5_5 => "claude-haiku-5-5",
            CS_ClaudeModel.Sonnet_5_5 => "claude-sonnet-5-5",
            CS_ClaudeModel.Opus_5_5 => "claude-opus-5-5",
            CS_ClaudeModel.Fable_5_2 => "claude-fable-5-2",
            _ => customModelId?.Trim()
        };

        static string ResolveApiKey(string keyFilePath) {
            string key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (!string.IsNullOrWhiteSpace(key))
                return key.Trim();
            if (!string.IsNullOrWhiteSpace(keyFilePath)) {
                string path = Path.IsPathRooted(keyFilePath) ? keyFilePath : Path.Combine(Application.persistentDataPath, keyFilePath);
                if (File.Exists(path))
                    return File.ReadAllText(path).Trim();
                Debug.LogError($"[Chatbot] No ANTHROPIC_API_KEY environment variable and no key file at '{path}'.");
            } else {
                Debug.LogError("[Chatbot] No ANTHROPIC_API_KEY environment variable and no key file configured.");
            }
            return null;
        }

        bool SupportsServerFallback =>
            ModelId == "claude-opus-5-5" || ModelId == "claude-sonnet-5-5" || ModelId == "claude-fable-5-2";

        /// <summary>Sends one player line and returns Claude's reply. Requests are serialized so history stays ordered.</summary>
        public async UniTask<string> Chat(string userText, bool addToHistory, CancellationToken token) {
            if (!HasApiKey)
                return "(The chatbot has no Anthropic API key configured on the server.)";
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, resetCts.Token);
            await gate.WaitAsync(linked.Token);
            try {
                var userMsg = new JObject { ["role"] = "user", ["content"] = userText };
                var messages = new JArray();
                foreach (var m in history)
                    messages.Add(m);
                messages.Add(userMsg);

                var body = new JObject {
                    ["model"] = ModelId,
                    ["max_tokens"] = maxTokens,
                    ["system"] = SystemPrompt,
                    ["messages"] = messages,
                    ["output_config"] = new JObject { ["effort"] = effort }
                };
                if (SupportsServerFallback)
                    body["fallbacks"] = "default";

                using var req = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST);
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = 60;
                req.SetRequestHeader("content-type", "application/json");
                req.SetRequestHeader("x-api-key", apiKey);
                req.SetRequestHeader("anthropic-version", ApiVersion);
                if (SupportsServerFallback)
                    req.SetRequestHeader("anthropic-beta", FallbackBeta);

                try {
                    await req.SendWebRequest().WithCancellation(linked.Token);
                } catch (UnityWebRequestException e) {
                    Debug.LogError($"[Chatbot] Anthropic request failed ({e.ResponseCode}): {e.Text}");
                    return "(The chatbot is having connection trouble. Try again!)";
                }

                var json = JObject.Parse(req.downloadHandler.text);
                string stopReason = (string) json["stop_reason"];
                if (stopReason == "refusal")
                    return "Nice try, but I'm not going to answer that one!";

                var sb = new StringBuilder();
                if (json["content"] is JArray content) {
                    foreach (var block in content) {
                        if ((string) block["type"] == "text")
                            sb.Append((string) block["text"]);
                    }
                }
                string reply = sb.ToString().Trim();
                if (reply.Length == 0) {
                    Debug.LogWarning($"[Chatbot] Empty reply (stop_reason={stopReason}).");
                    reply = "...";
                }

                if (addToHistory) {
                    history.Add(userMsg);
                    history.Add(new JObject { ["role"] = "assistant", ["content"] = reply });
                    // Trim oldest user/assistant pairs so history always starts on a user turn.
                    while (history.Count > maxHistoryMessages)
                        history.RemoveRange(0, 2);
                }
                return reply;
            } finally {
                gate.Release();
            }
        }

        /// <summary>Cancels in-flight and queued requests and clears history.</summary>
        public void Reset() {
            resetCts.Cancel();
            resetCts.Dispose();
            resetCts = new CancellationTokenSource();
            history.Clear();
        }
    }
}
