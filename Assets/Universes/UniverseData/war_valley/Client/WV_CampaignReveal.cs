using System;
using RyanAssets.GameFeel;
using RyanAssets.Retention.Client;
using RyanAssets.Shared.Global;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// The campaign's big moments beside the round summary card: the stars a win earned, filling one
    /// by one, and a tile for every tier the round unlocked ("TIER UNLOCKED  Elite"), drawn like the
    /// retention kit's level-unlock tile. The kit's card lists only level unlocks, so War Valley's tier
    /// unlocks get their own tiles here. Shown while the summary card is, from <see cref="WV_RoundEnded"/>.
    /// </summary>
    public sealed class WV_CampaignReveal : MonoBehaviour {
        [Tooltip("The column the tiles stack in; placed beside the summary card each frame.")]
        [SerializeField] RectTransform column;
        [SerializeField] RetentionSummaryCard summary;
        [Tooltip("Theme icons by name; the HUD builder points this at the retention HUD's table.")]
        [SerializeField] RetentionIcons icons;

        [Header("Stars tile")]
        [SerializeField] RectTransform starsTile;
        [SerializeField] TextMeshProUGUI starsTitle;
        [SerializeField] TextMeshProUGUI starsLine;
        [SerializeField] Image starsEmblem;
        [SerializeField] Image[] stars = Array.Empty<Image>();

        [Header("Unlock tiles (one per tier a round can open)")]
        [SerializeField] RectTransform[] unlockTiles = Array.Empty<RectTransform>();
        [SerializeField] Image[] unlockIcons = Array.Empty<Image>();
        [SerializeField] Image[] unlockDiscs = Array.Empty<Image>();
        [SerializeField] TextMeshProUGUI[] unlockTitles = Array.Empty<TextMeshProUGUI>();
        [SerializeField] TextMeshProUGUI[] unlockLines = Array.Empty<TextMeshProUGUI>();

        [Header("Feel")]
        [SerializeField, Min(0f)] float gap = 16f;
        [SerializeField, Min(0f)] float tileGap = 12f;
        [Tooltip("Seconds after the summary card opens before the first tile pops in.")]
        [SerializeField, Min(0f)] float firstDelay = 0.9f;
        [SerializeField, Min(0.05f)] float tileStepSeconds = 0.8f;
        [SerializeField, Min(0.05f)] float starStepSeconds = 0.4f;
        [SerializeField] Color starOn = new(1f, 0.8f, 0.3f, 1f);
        [SerializeField] Color starOff = new(1f, 1f, 1f, 0.16f);
        [SerializeField] Color unlockColor = new(0.66f, 0.47f, 1f, 1f);
        [Tooltip("Size of the confetti over the screen when a tier unlocks.")]
        [SerializeField, Min(0.1f)] float confettiScale = 2.5f;

        WV_RoundEnded result;
        bool armed;
        bool playing;
        float startAt;
        int step;
        float nextAt;
        int starsTarget;
        int starsShown;
        float nextStarAt;
        int unlockCount;
        readonly Vector3[] corners = new Vector3[4];

        void Awake() => HideAll();

        /// <summary>This commander's result: what to reveal once their summary card is up.</summary>
        public void SetResult(WV_RoundEnded value) {
            result = value;
            HideAll();
            bool starred = value.victory && !value.lateJoin && WV_Campaign.HasStars((WV_DifficultyTier)value.difficulty) && value.stars > 0;
            starsTarget = starred ? Mathf.Clamp(value.stars, 0, stars.Length) : 0;
            unlockCount = Mathf.Min(unlockTiles.Length, value.unlockedNames?.Length ?? 0);
            armed = starred || unlockCount > 0;
            playing = false;
        }

        void HideAll() {
            if (starsTile != null)
                starsTile.gameObject.SetActive(false);
            foreach (RectTransform tile in unlockTiles) {
                if (tile != null)
                    tile.gameObject.SetActive(false);
            }
        }

        void LateUpdate() {
            if (!armed)
                return;
            bool up = summary != null && summary.Visible;
            // The tier vote hides the retention HUD (and with it the summary card) without closing
            // the card, so the tiles beside it step aside for the vote too and come back after it.
            if (column != null)
                column.gameObject.SetActive(!SharedGlobalEvents.isVoting);
            float now = Time.unscaledTime;
            if (!up) {
                if (playing) {
                    // The card was closed: the reveal goes with it.
                    HideAll();
                    armed = false;
                    playing = false;
                }
                return;
            }
            if (!playing) {
                playing = true;
                startAt = now + firstDelay;
                step = 0;
                nextAt = startAt;
            }
            Place();
            if (now >= nextAt)
                NextTile(now);
            TickStars(now);
        }

        /// <summary>Keeps the column beside the summary card's top-right corner.</summary>
        void Place() {
            if (column == null || summary == null)
                return;
            ((RectTransform)summary.transform).GetWorldCorners(corners);
            var parent = (RectTransform)column.parent;
            Vector3 topRight = parent.InverseTransformPoint(corners[2]);
            column.localPosition = new Vector3(topRight.x + gap, topRight.y, 0f);
        }

        void NextTile(float now) {
            int index = 0;
            // Step 0 is the stars tile (when there are stars), then one step per unlock.
            if (starsTarget > 0) {
                if (step == index) {
                    ShowStarsTile();
                    nextStarAt = now + 0.35f;
                    nextAt = now + tileStepSeconds + starsTarget * starStepSeconds;
                    step++;
                    Layout();
                    return;
                }
                index++;
            }
            for (int i = 0; i < unlockCount; i++, index++) {
                if (step != index)
                    continue;
                ShowUnlock(i);
                nextAt = now + tileStepSeconds;
                step++;
                Layout();
                return;
            }
            nextAt = float.PositiveInfinity;
        }

        void Layout() {
            float y = 0f;
            if (starsTile != null && starsTile.gameObject.activeSelf) {
                starsTile.anchoredPosition = new Vector2(0f, -y);
                y += starsTile.sizeDelta.y + tileGap;
            }
            foreach (RectTransform tile in unlockTiles) {
                if (tile == null || !tile.gameObject.activeSelf)
                    continue;
                tile.anchoredPosition = new Vector2(0f, -y);
                y += tile.sizeDelta.y + tileGap;
            }
        }

        void ShowStarsTile() {
            if (starsTile == null)
                return;
            var tier = (WV_DifficultyTier)result.difficulty;
            if (starsTitle != null)
                starsTitle.text = result.firstWin ? $"FIRST {tier.ToString().ToUpperInvariant()} WIN" : tier.ToString().ToUpperInvariant();
            if (starsEmblem != null) {
                starsEmblem.color = WV_TierStyle.Accent(tier);
                if (icons != null)
                    starsEmblem.sprite = icons.Get(WV_TierStyle.Icon(tier));
            }
            if (starsLine != null)
                starsLine.text = result.stars >= WV_Campaign.MaxStars
                    ? "Three stars: the flag never fell below " + result.threeStarPercent + "%"
                    : $"3 stars: keep the flag above {result.threeStarPercent}% (it kept {result.lowestFlagPercent}%)";
            foreach (Image star in stars) {
                if (star != null)
                    star.color = starOff;
            }
            starsShown = 0;
            starsTile.gameObject.SetActive(true);
            Punch.Pop(starsTile, 0.7f, 0.3f);
            GameSfx.Play(SfxCue.Whoosh, 0.7f);
        }

        void TickStars(float now) {
            if (starsShown >= starsTarget || starsTile == null || !starsTile.gameObject.activeSelf || now < nextStarAt)
                return;
            Image star = stars[starsShown];
            starsShown++;
            nextStarAt = now + starStepSeconds;
            if (star == null)
                return;
            star.color = starOn;
            Punch.Pop(star.transform, 0.4f, 0.35f);
            GameSfx.Play(SfxCue.Pickup, 1f, GameSfx.Semitones(starsShown * 3f));
        }

        void ShowUnlock(int index) {
            RectTransform tile = unlockTiles[index];
            if (tile == null)
                return;
            string name = result.unlockedNames[index];
            WV_DifficultyTier tier = TierNamed(index);
            if (index < unlockTitles.Length && unlockTitles[index] != null)
                unlockTitles[index].text = name.ToUpperInvariant();
            if (index < unlockLines.Length && unlockLines[index] != null)
                unlockLines[index].text = result.unlockedDescriptions != null && index < result.unlockedDescriptions.Length
                    ? result.unlockedDescriptions[index] + " Vote for it next round."
                    : "Vote for it next round.";
            if (index < unlockDiscs.Length && unlockDiscs[index] != null)
                unlockDiscs[index].color = unlockColor;
            if (index < unlockIcons.Length && unlockIcons[index] != null && icons != null)
                unlockIcons[index].sprite = icons.Get(WV_TierStyle.Icon(tier));
            tile.gameObject.SetActive(true);
            Punch.Pop(tile, 0.6f, 0.35f);
            GameSfx.Play(SfxCue.Win, 0.9f);
            Camera camera = Camera.main;
            if (camera != null)
                GameFx.Play(FxKind.Confetti, camera.ViewportToWorldPoint(new Vector3(0.45f, 0.75f, 8f)), confettiScale);
        }

        /// <summary>The tier behind the <paramref name="index"/>-th unlock: the set bits of the mask, lowest first.</summary>
        WV_DifficultyTier TierNamed(int index) {
            int seen = 0;
            foreach (WV_DifficultyTier tier in WV_Campaign.Tiers) {
                if (!WV_Campaign.IsInMask(result.unlockedTiers, tier))
                    continue;
                if (seen++ == index)
                    return tier;
            }
            return WV_DifficultyTier.Veteran;
        }
    }
}
