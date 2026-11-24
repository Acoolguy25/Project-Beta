using System;
using RyanAssets.GameFeel;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tycoon.Client {
    /// <summary>
    /// "While you were away" card: how long the player was gone and what their factory earned, counted
    /// up on screen. The server has already paid it, but the HUD's wallet holds that cash back until the
    /// card closes: Collect (or the timer) lands it in the wallet with a count-up and a cash fanfare.
    /// Built by TY_HudAuthoring.
    /// </summary>
    public sealed class TY_OfflineCard : MonoBehaviour {
        public HudPopup popup;
        public TMP_Text away, amount, detail;
        public Button collect;
        [Tooltip("Seconds the amount takes to count up.")]
        [Min(0.05f)] public float countSeconds = 1.4f;
        [Tooltip("The card closes by itself after this many seconds.")]
        [Min(1f)] public float autoHideSeconds = 12f;
        [Tooltip("{0} = time away.")]
        public string awayFormat = "You were away for {0}";
        public Transform punchTarget;
        [Range(0f, 1f)] public float landPunch = 0.2f;

        long total;
        float shownAt, hideAt;
        bool counting, open;

        /// <summary>Raised when the card closes (Collect or timeout), with the amount, for the cash feedback.</summary>
        public event Action<long> Closed;

        public bool IsOpen => open;

        /// <summary>The cash this open card is paying (the wallet holds it back until the card closes), else 0.</summary>
        public long Pending => open ? total : 0;

        void Awake() => collect.onClick.AddListener(Close);
        void OnDestroy() => collect.onClick.RemoveListener(Close);

        public void Show(long cash, double secondsAway, string text) {
            total = cash;
            away.text = string.Format(awayFormat, TY_OfflineEarnings.Duration(secondsAway));
            detail.text = text;
            amount.text = "$0";
            shownAt = Time.unscaledTime;
            hideAt = shownAt + autoHideSeconds;
            counting = open = true;
            popup.Show();
        }

        public void Close() {
            if (!open) return;
            open = counting = false;
            amount.text = $"${TY_Catalog.Money(total)}";
            popup.Hide();
            Closed?.Invoke(total);
        }

        void Update() {
            if (!open) return;
            float now = Time.unscaledTime;
            if (counting) {
                float t = Mathf.Clamp01((now - shownAt) / countSeconds);
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                amount.text = $"${TY_Catalog.Money(Math.Floor(total * (double)eased))}";
                if (t >= 1f) {
                    counting = false;
                    if (punchTarget != null) Punch.Scale(punchTarget, landPunch);
                }
            }
            if (now >= hideAt) Close();
        }
    }
}
