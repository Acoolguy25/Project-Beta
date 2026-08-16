using FishNet;
using FishNet.Object.Synchronizing;
using RyanAssets.Client.ClientCore;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Player;
using RyanAssets.Shared.Requests;
using RyanAssets.TweenService;
using RyanAssets.UI.ListGrid;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Vote {
    public class ClientVote : ListGridUI<ClientVoteOption> {
        CanvasGroup canvasGroup;
        [SerializeField]
        TextMeshProUGUI titleText, descriptionText, timerText;
        [SerializeField]
        Button noVoteButton;
        [SerializeField]
        TextMeshProUGUI noVoteCountText;
        [SerializeField]
        Button skipVoteButton;
        [SerializeField]
        TextMeshProUGUI skipVoteCountText;

        readonly System.Collections.Generic.Dictionary<int, TextMeshProUGUI> optionCountTexts = new();
        readonly System.Collections.Generic.Dictionary<Image, Color> buttonColors = new();
        Image selectedButtonImage;
        bool subscribed;

        protected override void Start() {
            base.Start();
            canvasGroup = GetComponent<CanvasGroup>();

            OnCreatePrefab += BindOption;
            if (noVoteButton != null)
                noVoteButton.onClick.AddListener(() => SubmitVote(-1));
            if (skipVoteButton != null)
                skipVoteButton.onClick.AddListener(() => SubmitVote(VoteRequest.SkipVoteOptionId, skipVoteButton));
            ClientConnector.OnDisconnected += Clear;
            SharedGlobalEvents.OnInstanceReady += OnSharedEventsReady;
            PlayerData.OnPlayerAdded += OnPlayerChanged;
            PlayerData.OnPlayerRemoved += OnPlayerChanged;
            Subscribe();
            Refresh();
        }

        void OnEnable() {
            Subscribe();
            Refresh();
        }

        protected override void OnDestroy() {
            Unsubscribe();
            ClientConnector.OnDisconnected -= Clear;
            SharedGlobalEvents.OnInstanceReady -= OnSharedEventsReady;
            PlayerData.OnPlayerAdded -= OnPlayerChanged;
            PlayerData.OnPlayerRemoved -= OnPlayerChanged;
            base.OnDestroy();
        }

        void OnSharedEventsReady() {
            Subscribe();
            Refresh();
        }

        void Update() {
            if (timerText == null || !SharedGlobalEvents.isVoting)
                return;

            timerText.text = $"{Mathf.CeilToInt(Mathf.Max(1f, SharedGlobalEvents.Instance.SharedVoteHeader.Value.endTime - RyanAssets.Core.NetworkHelper.ServerTime))}s";
        }

        void Subscribe() {
            if (subscribed || SharedGlobalEvents.Instance == null)
                return;

            SharedGlobalEvents.Instance.SharedVoteHeader.OnChange += OnVoteHeaderChanged;
            SharedGlobalEvents.Instance.VoteTotals.OnChange += OnVoteTotalsChanged;
            SharedGlobalEvents.Instance.SkipVoteCount.OnChange += OnSkipVoteCountChanged;
            subscribed = true;
        }

        void Unsubscribe() {
            if (!subscribed || SharedGlobalEvents.Instance == null)
                return;

            SharedGlobalEvents.Instance.SharedVoteHeader.OnChange -= OnVoteHeaderChanged;
            SharedGlobalEvents.Instance.VoteTotals.OnChange -= OnVoteTotalsChanged;
            SharedGlobalEvents.Instance.SkipVoteCount.OnChange -= OnSkipVoteCountChanged;
            subscribed = false;
        }

        void OnVoteHeaderChanged(SharedVoteHeader previous, SharedVoteHeader next, bool asServer) => Refresh();

        void OnVoteTotalsChanged(SyncListOperation operation, int index, int oldValue, int newValue, bool asServer) {
            if (operation == SyncListOperation.Complete)
                UpdateVoteCounts();
        }

        void OnSkipVoteCountChanged(int previous, int next, bool asServer) => UpdateVoteCounts();

        void OnPlayerChanged(PlayerData player) => UpdateVoteCounts();

        void Refresh() {
            Subscribe();
            bool visible = SharedGlobalEvents.isVoting;
            SetVisible(visible);
            if (!visible)
                return;

            ClientVoteInfo voteInfo = VoteDeclarations.GetVoteInfo(SharedGlobalEvents.Instance.SharedVoteHeader.Value.voteId);
            if (titleText != null)
                titleText.text = voteInfo.title;
            if (descriptionText != null) {
                descriptionText.text = voteInfo.description;
                descriptionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(voteInfo.description));
            }
            optionCountTexts.Clear();
            FadeSelectedButton(noVoteButton); // no vote is selected by default
            RefreshPrefabs(voteInfo.options ?? System.Array.Empty<ClientVoteOption>());
            UpdateVoteCounts();
        }

        void BindOption(GameObject prefab, ClientVoteOption option) {
            Transform root = prefab.transform;
            TextMeshProUGUI optionTitle = FindText(root, "OptionTitle");
            TextMeshProUGUI optionDescription = FindText(root, "OptionDescription");
            TextMeshProUGUI optionVoteCount = FindText(root, "OptionVoteCount");
            Image optionImage = FindTransform(root, "OptionImage")?.GetComponent<Image>();
            Button button = prefab.GetComponent<Button>();

            if (optionTitle != null)
                optionTitle.text = option.title;
            if (optionDescription != null)
                optionDescription.text = option.description;
            if (optionImage != null) {
                optionImage.sprite = option.image;
                optionImage.enabled = option.image != null;
            }
            if (button != null) {
                int optionId = option.optionId;
                if (optionVoteCount != null)
                    optionCountTexts[optionId] = optionVoteCount;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SubmitVote(optionId, button));
            }

            UpdateVoteCounts();
        }

        void SubmitVote(int optionId) => SubmitVote(optionId, optionId < 0 ? noVoteButton : null);

        void SubmitVote(int optionId, Button button) {
            FadeSelectedButton(button);
            InstanceFinder.ClientManager.Broadcast(new VoteRequest { optionId = optionId });
        }

        void UpdateVoteCounts() {
            SharedGlobalEvents events = SharedGlobalEvents.Instance;
            if (events == null)
                return;

            int selectedVotes = 0;
            for (int i = 0; i < events.VoteTotals.Count; i++) {
                int count = events.VoteTotals[i];
                selectedVotes += count;
                if (optionCountTexts.TryGetValue(i, out TextMeshProUGUI label) && label != null)
                    label.text = $"{count} vote{(count == 1 ? string.Empty : "s")}";
            }

            if (noVoteCountText != null) {
                int noVoteCount = Mathf.Max(0, PlayerData.Players.Count - selectedVotes - events.SkipVoteCount.Value);
                noVoteCountText.text = $"No Vote ({noVoteCount})";
            }

            if (skipVoteCountText != null)
                skipVoteCountText.text = $"Skip Vote ({events.SkipVoteCount.Value}/{PlayerData.Players.Count})";
        }

        void FadeSelectedButton(Button button) {
            if (button == null)
                return;

            Image image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (image == null)
                return;

            if (!buttonColors.TryGetValue(image, out Color initialColor)) {
                initialColor = image.color;
                buttonColors.Add(image, initialColor);
            }

            if (selectedButtonImage != null && selectedButtonImage != image && buttonColors.TryGetValue(selectedButtonImage, out Color previousColor))
                FadeImage(selectedButtonImage, previousColor);

            selectedButtonImage = image;
            FadeImage(image, new Color(0.20f, 0.72f, 0.34f, initialColor.a));
        }

        static void FadeImage(Image image, Color color) {
            if (TweenManager.Instance == null) {
                image.color = color;
                return;
            }
            TweenManager.Instance.ClearTweens(image);
            Color startColor = image.color;
            TweenManager.Instance.RegisterTween(0.2f, percent => image.color = Color.Lerp(startColor, color, percent), targetObject: image);
        }

        void Clear() {
            ClearPrefabs();
            optionCountTexts.Clear();
            selectedButtonImage = null;
            SetVisible(false);
        }

        void SetVisible(bool visible) {
            if (canvasGroup == null)
                return;
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        Transform FindTransform(string path) => transform.Find(path);
        TextMeshProUGUI FindText(string path) => FindTransform(path)?.GetComponentInChildren<TextMeshProUGUI>(true);

        static Transform FindTransform(Transform root, string name) {
            foreach (Transform child in root) {
                if (child.name == name)
                    return child;
                Transform nested = FindTransform(child, name);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        static TextMeshProUGUI FindText(Transform root, string name) =>
            FindTransform(root, name)?.GetComponent<TextMeshProUGUI>();
    }
}
