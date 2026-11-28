using System;
using FishNet;
using FishNet.Connection;
using RyanAssets.Client.ClientUI.Vote;
using RyanAssets.DataService;
using RyanAssets.GameFeel;
using RyanAssets.Retention.Client;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Requests;
using TMPro;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// War Valley's own tier choice, drawn over the shared vote it runs on: the server's
    /// <see cref="WV_TierBallot"/> names the tiers in the vote's order, the shared vote counts them, and
    /// this panel shows one <see cref="WV_TierCard"/> per tier with the commander's own stars, "NEW",
    /// their best Endless wave, the XP each pays and their next goal. A click sends the shared
    /// <see cref="VoteRequest"/> for that card's option.
    /// <para>
    /// It opens only once the ballot and the vote are both in, with every card already filled, so it
    /// never shows empty; its cards are authored once and never rebuilt, so a click lands on the card
    /// under the cursor. While it shows, the shared vote panel is kept invisible and click-through.
    /// </para>
    /// </summary>
    public sealed class WV_TierChoicePanel : MonoBehaviour {
        [SerializeField] RectTransform window;
        [SerializeField] CanvasGroup group;
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] TextMeshProUGUI subtitleLabel;
        [SerializeField] TextMeshProUGUI timerLabel;
        [SerializeField] TextMeshProUGUI footerLabel;
        [SerializeField] WV_TierCard[] cards = Array.Empty<WV_TierCard>();
        [Tooltip("Theme icons by name; the HUD builder points this at the retention HUD's table.")]
        [SerializeField] RetentionIcons icons;
        [Tooltip("One emblem per card, parallel to the cards, set to the tier's icon.")]
        [SerializeField] UnityEngine.UI.Image[] emblems = Array.Empty<UnityEngine.UI.Image>();

        [SerializeField] string title = "Choose the war";
        [SerializeField] string subtitle = "Harder tiers pay more XP. Win a tier to unlock the next.";
        [SerializeField] string footer = "Click a tier to vote  ·  the vote ends when every commander has voted";
        [SerializeField] string resultFooter = "{0} it is";
        [Tooltip("Seconds the panel stays up after the vote closes, showing the chosen tier.")]
        [SerializeField, Min(0f)] float resultSeconds = 1.8f;
        [Tooltip("Seconds the panel fades in and out over.")]
        [SerializeField, Min(0.01f)] float fadeSeconds = 0.18f;
        [Tooltip("A ballot older than its vote by this much is stale (a vote this client missed the start of).")]
        [SerializeField, Min(1f)] float staleSeconds = 6f;
        [SerializeField] Color timerColor = new(1f, 1f, 1f, 1f);
        [SerializeField] Color timerUrgentColor = new(1f, 0.55f, 0.35f, 1f);
        [SerializeField, Min(0)] int urgentSeconds = 5;

        // Layout read from the authored five-card window, so fewer cards centre in a narrower one.
        float padding, cardWidth, cardStep, fullWidth;
        bool layoutRead;

        WV_TierBallot ballot;
        bool hasBallot;
        float ballotAt;
        WV_CampaignStatus status;
        bool hasStatus;
        bool open;
        bool closing;
        float closeAt;
        int lastBeep = -1;
        int votedFor = -2;
        ClientVote sharedVote;
        CanvasGroup sharedGroup;

        /// <summary>Whether the panel is on screen (the vote, then its result).</summary>
        public bool Visible => open;

        void Awake() {
            for (int i = 0; i < cards.Length; i++) {
                int option = i;
                if (cards[i] != null)
                    cards[i].Clicked += () => Vote(option);
            }
            ReadLayout();
            SetShown(false, instant: true);
        }

        void ReadLayout() {
            if (layoutRead || window == null || cards.Length == 0 || cards[0] == null)
                return;
            layoutRead = true;
            fullWidth = window.sizeDelta.x;
            var first = (RectTransform)cards[0].transform;
            padding = first.anchoredPosition.x;
            cardWidth = first.sizeDelta.x;
            cardStep = cards.Length > 1 && cards[1] != null
                ? ((RectTransform)cards[1].transform).anchoredPosition.x - padding
                : cardWidth;
        }

        /// <summary>The server's ballot for the vote about to open.</summary>
        public void SetBallot(WV_TierBallot value) {
            ballot = value;
            hasBallot = value.tiers != null && value.tiers.Length > 0;
            ballotAt = Time.unscaledTime;
            closing = false;
            votedFor = -2;
            if (open)
                Fill();
        }

        /// <summary>The commander's campaign, for their stars on each tier.</summary>
        public void SetStatus(WV_CampaignStatus value) {
            status = value;
            hasStatus = true;
            if (open && !closing)
                Fill();
        }

        /// <summary>The round's tier is settled (the briefing): the panel stamps it if it is still up.</summary>
        public void ShowChosen(WV_DifficultyTier tier, string tierName) {
            if (!open || !hasBallot)
                return;
            int index = Array.IndexOf(ballot.tiers, (byte)tier);
            for (int i = 0; i < cards.Length; i++) {
                if (cards[i] != null && cards[i].gameObject.activeSelf)
                    cards[i].SetResult(i == index);
            }
            if (footerLabel != null)
                footerLabel.text = string.Format(resultFooter, string.IsNullOrEmpty(tierName) ? tier.ToString() : tierName);
            closing = true;
            closeAt = Mathf.Max(closeAt, Time.unscaledTime + resultSeconds);
        }

        static bool DynamicVoteRunning(out SharedGlobalEvents events) {
            events = SharedGlobalEvents.Instance;
            // The header's id alone: its end time is in server time, which a client clock that drifted
            // after a long frame reads wrongly (the shared panel's timer shows it).
            return events != null && events.SharedVoteHeader.Value.voteId == VoteEnum.Dynamic;
        }

        void LateUpdate() {
            float now = Time.unscaledTime;
            bool running = DynamicVoteRunning(out SharedGlobalEvents events);
            bool fresh = hasBallot && now - ballotAt <= ballot.seconds + staleSeconds;
            bool matches = running && fresh && events.DynamicVote.Value.OptionCount == ballot.tiers.Length;

            if (!open && matches && !closing) {
                Fill();
                SetShown(true, instant: false);
                GameSfx.Play(SfxCue.Whoosh);
            }

            if (open && !closing && !running) {
                // The vote closed before the briefing named the tier: hold on the most voted card.
                closing = true;
                closeAt = now + resultSeconds;
                StampLeader(events);
            }

            if (open && closing && now >= closeAt) {
                SetShown(false, instant: false);
                hasBallot = false;
                closing = false;
            }

            if (open && !closing && events != null) {
                UpdateVotes(events);
                UpdateTimer(now);
            }

            if (group != null) {
                float target = open ? 1f : 0f;
                group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / fadeSeconds);
                if (!open && group.alpha <= 0f && window != null && window.gameObject.activeSelf)
                    window.gameObject.SetActive(false);
            }

            // The shared vote runs underneath; only one of the two may take the clicks.
            if (open)
                HideSharedVote();
        }

        void SetShown(bool value, bool instant) {
            open = value;
            if (window != null && value)
                window.gameObject.SetActive(true);
            if (group != null) {
                group.interactable = value;
                group.blocksRaycasts = value;
                if (instant) {
                    group.alpha = value ? 1f : 0f;
                    if (!value && window != null)
                        window.gameObject.SetActive(false);
                }
            }
            if (value && window != null)
                Punch.Pop(window, 0.92f, 0.25f);
        }

        void Fill() {
            if (!hasBallot)
                return;
            ReadLayout();
            int count = Mathf.Min(ballot.tiers.Length, cards.Length);
            byte[] stars = hasStatus ? status.tierStars : null;
            var starList = new int[WV_Campaign.Tiers.Length];
            for (int i = 0; i < starList.Length; i++)
                starList[i] = stars != null && i < stars.Length ? stars[i] : 0;
            WV_DifficultyTier goal = WV_Campaign.NextTarget(starList);
            byte unlocked = hasStatus ? status.unlockedTiers : (byte)(1 << (int)WV_DifficultyTier.Recruit);

            for (int i = 0; i < cards.Length; i++) {
                WV_TierCard card = cards[i];
                if (card == null)
                    continue;
                if (i >= count) {
                    card.Hide();
                    continue;
                }
                var tier = (WV_DifficultyTier)ballot.tiers[i];
                string tierName = ballot.names != null && i < ballot.names.Length ? ballot.names[i] : tier.ToString();
                string description = ballot.descriptions != null && i < ballot.descriptions.Length ? ballot.descriptions[i] : string.Empty;
                int xp = ballot.xpBonusPercent != null && i < ballot.xpBonusPercent.Length ? ballot.xpBonusPercent[i] : 0;
                card.Show(tier, tierName, description, xp, WV_Campaign.StarsOn(starList, tier),
                    hasStatus ? status.bestEndlessWave : 0, WV_Campaign.IsInMask(unlocked, tier), hasStatus && tier == goal);
                if (i < emblems.Length && emblems[i] != null && icons != null)
                    emblems[i].sprite = icons.Get(WV_TierStyle.Icon(tier));
                var rect = (RectTransform)card.transform;
                rect.anchoredPosition = new Vector2(padding + i * cardStep, rect.anchoredPosition.y);
            }
            if (window != null && count > 0)
                window.sizeDelta = new Vector2(Mathf.Min(fullWidth, padding * 2f + cardWidth + (count - 1) * cardStep), window.sizeDelta.y);
            if (titleLabel != null)
                titleLabel.text = title.ToUpperInvariant();
            if (subtitleLabel != null)
                subtitleLabel.text = subtitle;
            if (footerLabel != null)
                footerLabel.text = footer;
            lastBeep = -1;
        }

        void UpdateVotes(SharedGlobalEvents events) {
            int mine = LocalVote();
            int count = Mathf.Min(ballot.tiers.Length, cards.Length);
            for (int i = 0; i < count; i++) {
                if (cards[i] == null)
                    continue;
                int votes = i < events.VoteTotals.Count ? events.VoteTotals[i] : 0;
                cards[i].SetVotes(votes, mine == i || (mine < 0 && votedFor == i));
            }
        }

        void UpdateTimer(float now) {
            if (timerLabel == null)
                return;
            int left = Mathf.Max(0, Mathf.CeilToInt(ballot.seconds - (now - ballotAt)));
            timerLabel.text = $"{left}s";
            bool urgent = left <= urgentSeconds;
            timerLabel.color = urgent ? timerUrgentColor : timerColor;
            if (urgent && left > 0 && left != lastBeep && left <= 3) {
                lastBeep = left;
                GameSfx.Play(SfxCue.CountdownBeep, 0.6f);
            }
        }

        void StampLeader(SharedGlobalEvents events) {
            int best = -1, top = 0;
            if (events != null) {
                for (int i = 0; i < events.VoteTotals.Count && i < ballot.tiers.Length; i++) {
                    if (events.VoteTotals[i] > top) {
                        top = events.VoteTotals[i];
                        best = i;
                    }
                }
            }
            if (best < 0)
                return;
            for (int i = 0; i < cards.Length; i++) {
                if (cards[i] != null && cards[i].gameObject.activeSelf)
                    cards[i].SetResult(i == best);
            }
            if (footerLabel != null && ballot.names != null && best < ballot.names.Length)
                footerLabel.text = string.Format(resultFooter, ballot.names[best]);
        }

        /// <summary>The local commander's vote as the server holds it, or -1.</summary>
        static int LocalVote() {
            NetworkConnection connection = InstanceFinder.ClientManager != null ? InstanceFinder.ClientManager.Connection : null;
            if (connection == null || PlayerData.Players == null || !PlayerData.Players.TryGetValue(connection, out PlayerData player)
                || player == null)
                return -1;
            return player.voteOption.Value;
        }

        void Vote(int option) {
            if (!open || closing || InstanceFinder.ClientManager == null || !InstanceFinder.ClientManager.Started)
                return;
            votedFor = option;
            GameSfx.Play(SfxCue.UiClick);
            InstanceFinder.ClientManager.Broadcast(new VoteRequest { optionId = option });
            for (int i = 0; i < cards.Length; i++) {
                if (cards[i] != null && cards[i].gameObject.activeSelf)
                    cards[i].SetVotes(i < (SharedGlobalEvents.Instance?.VoteTotals.Count ?? 0)
                        ? SharedGlobalEvents.Instance.VoteTotals[i] : 0, i == option);
            }
        }

        /// <summary>Keeps the shared vote panel invisible and click-through while this one shows.</summary>
        void HideSharedVote() {
            if (sharedVote == null) {
                sharedVote = FindFirstObjectByType<ClientVote>(FindObjectsInactive.Include);
                sharedGroup = sharedVote != null ? sharedVote.GetComponent<CanvasGroup>() : null;
            }
            if (sharedGroup == null)
                return;
            sharedGroup.alpha = 0f;
            sharedGroup.interactable = false;
            sharedGroup.blocksRaycasts = false;
        }
    }
}
