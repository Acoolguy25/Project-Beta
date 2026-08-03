using System;
using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Shared.Declarations {
    [Serializable]
    public enum VoteEnum : sbyte {
        None = -1,
        MM_VoteMode,
    }

    /// <summary>Small, server-authoritative description of the currently running vote.</summary>
    [Serializable]
    public struct SharedVoteHeader {
        public VoteEnum voteId;
        public float endTime;

        public SharedVoteHeader(VoteEnum voteId, float endTime = float.MinValue) {
            this.voteId = voteId;
            this.endTime = endTime;
        }
    }

    [Serializable]
    public struct ClientVoteInfo {
        public VoteEnum voteId;
        public string title;
        public string description;
        public ClientVoteOption[] options;
    }

    /// <summary>Presentation-only option data. Its optionId is its index in the vote's option array.</summary>
    [Serializable]
    public struct ClientVoteOption {
        [NonSerialized] public int optionId;
        public string title;
        public string description;
        public Sprite image;
    }

    public class VoteDeclarations : MonoBehaviour {
        [SerializeField] List<ClientVoteInfo> voteCategories = new();

        public static VoteDeclarations Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() => Instance = null;

        void Awake() {
            if (Instance != null && Instance != this) {
                Debug.LogWarning($"Multiple {nameof(VoteDeclarations)} instances found; using the first one.", this);
                return;
            }
            Instance = this;
            foreach (ClientVoteInfo voteInfo in voteCategories) {
                for (int i = 0; i < voteInfo.options.Length; i++) {
                    voteInfo.options[i].optionId = i;
                }
            }
        }

        void OnDestroy() {
            if (Instance == this)
                Instance = null;
        }

        public static ClientVoteInfo GetVoteInfo(VoteEnum voteId) {
            foreach (ClientVoteInfo candidate in Instance.voteCategories) {
                if (candidate.voteId != voteId)
                    continue;
              
                return candidate;
            }

            return default;
        }

        public static int GetOptionCount(VoteEnum voteId) =>
            GetVoteInfo(voteId).options.Length;
    }
}
