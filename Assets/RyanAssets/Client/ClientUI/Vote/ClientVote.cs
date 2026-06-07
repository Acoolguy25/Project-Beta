using System;
using System.Collections.Generic;
using FishNet;
using RyanAssets.Client.ClientCore;
using RyanAssets.Shared.Player;
using RyanAssets.Shared.Requests;
using RyanAssets.UI.ListGrid;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Vote {
    public class ClientVote : ListGridUI<SharedVoteOption> {
        [Header("Window")]
        [SerializeField] RectTransform window;
        [SerializeField] Vector2 maximizedSize = new(720f, 430f);
        [SerializeField] Vector2 minimizedSize = new(280f, 54f);
        [SerializeField] CanvasGroup canvasGroup;

        [Header("Text")]
        [SerializeField] TextMeshProUGUI titleText;
        [SerializeField] TextMeshProUGUI descriptionText;
        [SerializeField] TextMeshProUGUI timerText;
        [SerializeField] TextMeshProUGUI totalText;

        [Header("Controls")]
        [SerializeField] Button noVoteButton;
        [SerializeField] Button minimizeButton;
        [SerializeField] TextMeshProUGUI minimizeText;

        [Header("Options")]
        [SerializeField] RectTransform viewport;
        [SerializeField] GridLayoutGroup grid;
        [SerializeField] ClientVoteOptionPrefab optionPrefab;

        readonly Dictionary<int, ClientVoteOptionPrefab> optionRows = new();
        bool minimized;
        int activeVoteId;
        int selectedOptionId;
        int currentTotal;

        void Awake() {
            if (!HasRequiredReferences()) {
                enabled = false;
                return;
            }

            modelPrefab = optionPrefab.gameObject;
            noVoteButton.onClick.AddListener(SendNoVote);
            minimizeButton.onClick.AddListener(() => SetMinimized(!minimized));
        }

        protected override void Start() {
            base.Start();
            OnCreatePrefab += OnCreateOptionPrefab;
            OnDeletePrefab += OnDeleteOptionPrefab;
            SharedGlobalEvents.OnVoteChanged += Refresh;
            SharedGlobalEvents.OnCurrentVoteChangedEvent += OnVoteChanged;
            ClientConnector.OnDisconnected += Clear;
            Clear();
            Refresh();
        }

        protected override void OnDestroy() {
            SharedGlobalEvents.OnVoteChanged -= Refresh;
            SharedGlobalEvents.OnCurrentVoteChangedEvent -= OnVoteChanged;
            ClientConnector.OnDisconnected -= Clear;
            base.OnDestroy();
        }

        void Update() {
            if (SharedGlobalEvents.Instance == null)
                return;

            SharedVoteInfo vote = SharedGlobalEvents.Instance.CurrentVote;
            if (!vote.isActive)
                return;

            TimeSpan remaining = new DateTime(vote.endUtcTicks, DateTimeKind.Utc) - DateTime.UtcNow;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
            timerText.text = $"{Mathf.CeilToInt((float)remaining.TotalSeconds)}s";
        }

        void OnVoteChanged(SharedVoteInfo vote) {
            if (vote.voteId != activeVoteId)
                selectedOptionId = 0;
            Refresh();
        }

        void Refresh() {
            if (SharedGlobalEvents.Instance == null) {
                Clear();
                return;
            }

            SharedVoteInfo vote = SharedGlobalEvents.Instance.CurrentVote;
            bool visible = vote.isActive && vote.voteId != 0;
            SetVisible(visible);
            if (!visible)
                return;

            activeVoteId = vote.voteId;
            titleText.text = vote.title;
            descriptionText.text = vote.description;

            List<SharedVoteOption> options = GetCurrentOptions(vote.voteId);
            totalText.text = $"{currentTotal} votes";
            ResizeOptionGrid(options.Count);
            optionRows.Clear();
            RefreshPrefabs(options.ToArray());
            SetMinimized(minimized);
        }

        List<SharedVoteOption> GetCurrentOptions(int voteId) {
            List<SharedVoteOption> options = new();
            currentTotal = 0;

            foreach (SharedVoteOption option in SharedGlobalEvents.Instance.VoteOptions) {
                if (option.voteId != voteId)
                    continue;

                options.Add(option);
                currentTotal += option.count;
            }

            return options;
        }

        void OnCreateOptionPrefab(GameObject prefab, SharedVoteOption option) {
            ClientVoteOptionPrefab row = prefab.GetComponent<ClientVoteOptionPrefab>();
            row.Bind(option, currentTotal, option.optionId == selectedOptionId, SendVote);
            optionRows[option.optionId] = row;
            prefab.SetActive(true);
        }

        void OnDeleteOptionPrefab(GameObject prefab) {
            ClientVoteOptionPrefab row = prefab.GetComponent<ClientVoteOptionPrefab>();
            if (row != null)
                row.Cleanup();
        }

        void SendVote(int optionId) {
            if (SharedGlobalEvents.Instance == null)
                return;

            SharedVoteInfo vote = SharedGlobalEvents.Instance.CurrentVote;
            if (!vote.isActive)
                return;

            selectedOptionId = optionId;
            InstanceFinder.ClientManager.Broadcast<VoteRequest>(new VoteRequest { voteId = vote.voteId, optionId = optionId });
            UpdateSelection();
        }

        void SendNoVote() {
            if (SharedGlobalEvents.Instance == null)
                return;

            selectedOptionId = 0;
            if (SharedGlobalEvents.Instance.CurrentVote.isActive)
                InstanceFinder.ClientManager.Broadcast<VoteRequest>(new VoteRequest { voteId = SharedGlobalEvents.Instance.CurrentVote.voteId, optionId = 0 });
            UpdateSelection();
        }

        void UpdateSelection() {
            foreach (KeyValuePair<int, ClientVoteOptionPrefab> pair in optionRows)
                pair.Value.SetSelected(pair.Key == selectedOptionId);
            noVoteButton.GetComponent<Image>().color = selectedOptionId == 0 ? new Color32(92, 98, 108, 230) : new Color32(36, 40, 48, 230);
        }

        void SetMinimized(bool value) {
            minimized = value;
            window.sizeDelta = minimized ? minimizedSize : maximizedSize;
            scrollRect.gameObject.SetActive(!minimized);
            descriptionText.gameObject.SetActive(!minimized && !string.IsNullOrWhiteSpace(descriptionText.text));
            noVoteButton.gameObject.SetActive(!minimized);
            totalText.gameObject.SetActive(!minimized);
            minimizeText.text = minimized ? "+" : "-";
        }

        void ResizeOptionGrid(int optionCount) {
            int columns = optionCount <= 2 ? Mathf.Max(1, optionCount) : 2;
            float width = viewport.rect.width > 1f ? viewport.rect.width : maximizedSize.x - 28f;
            float cellWidth = (width - grid.padding.horizontal - grid.spacing.x * (columns - 1)) / columns;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.cellSize = new Vector2(Mathf.Max(240f, cellWidth), optionCount <= 2 ? 144f : 118f);
        }

        void Clear() {
            ClearPrefabs();
            optionRows.Clear();
            selectedOptionId = 0;
            SetVisible(false);
        }

        void SetVisible(bool visible) {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = visible;
            canvasGroup.interactable = visible;
        }

        bool HasRequiredReferences() {
            bool hasReferences =
                window != null &&
                canvasGroup != null &&
                titleText != null &&
                descriptionText != null &&
                timerText != null &&
                totalText != null &&
                noVoteButton != null &&
                minimizeButton != null &&
                minimizeText != null &&
                viewport != null &&
                grid != null &&
                optionPrefab != null &&
                scrollRect != null;

            if (!hasReferences)
                Debug.LogError($"{nameof(ClientVote)} prefab is missing serialized UI references.", this);

            return hasReferences;
        }
    }
}
