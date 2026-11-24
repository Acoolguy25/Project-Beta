using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// The daily reward chest on the owner's plot (authored by TY_Authoring). Ready: the gold chest glows
    /// and bobs under a large sign naming today's prize; opened: a plain chest under a small sign counting
    /// down to the next chest and naming its prize. Clients show it only on the local player's own plot; the server
    /// decides when it pays.
    /// </summary>
    public sealed class TY_ChestView : MonoBehaviour {
        [Tooltip("Shown while today's chest is waiting.")]
        public GameObject readyModel;
        [Tooltip("Shown once today's chest is opened.")]
        public GameObject openedModel;
        [Tooltip("Light, beam and sparkle shown while the chest is ready.")]
        public GameObject readyGlow;
        [Tooltip("The large sign over a ready chest, and its text.")]
        public GameObject readySign;
        public TMP_Text label;
        [Tooltip("The small sign over an opened chest, and its text.")]
        public GameObject waitingSign;
        public TMP_Text waitingLabel;
        [Tooltip("Ready: {0} = streak day, {1} = prize.")]
        public string readyFormat = "DAILY CHEST\n<size=62%>Day {0}: step here!</size>\n<size=52%>{1}</size>";
        [Tooltip("Opened: {0} = the next chest's streak day, {1} = time until it, {2} = its prize.")]
        public string waitingFormat = "Day {0} chest in {1}\n<size=78%><color=#FFD95A>{2}</color></size>";
        public Color readyColor = new(1f, 0.85f, 0.3f), waitingColor = new(0.86f, 0.88f, 0.92f);

        bool shownReady;
        long shownMinutes = -1;
        int shownDay = -1;
        string shownPrize;
        bool hasShown;

        /// <param name="day">Streak day of the chest the sign names (today's while ready, else the next one's).</param>
        /// <param name="prize">What that chest holds ("10 min of income, +1 perk token").</param>
        /// <param name="secondsLeft">Seconds until the next chest (ignored while ready).</param>
        public void Show(bool ready, float secondsLeft, int day, string prize) {
            // Minutes are enough for a countdown that runs for hours; rewrite only when the text changes.
            long minutes = ready ? -1 : (long)Mathf.Ceil(Mathf.Max(0f, secondsLeft) / 60f);
            if (hasShown && ready == shownReady && minutes == shownMinutes && day == shownDay && prize == shownPrize) return;
            hasShown = true;
            shownPrize = prize;
            shownReady = ready;
            shownMinutes = minutes;
            shownDay = day;
            if (readyModel != null) readyModel.SetActive(ready);
            if (openedModel != null) openedModel.SetActive(!ready);
            if (readyGlow != null) readyGlow.SetActive(ready);
            if (readySign != null) readySign.SetActive(ready);
            if (waitingSign != null) waitingSign.SetActive(!ready);
            int shown = Mathf.Max(1, day);
            if (ready && label != null) {
                label.text = string.Format(readyFormat, shown, prize);
                label.color = readyColor;
            }
            if (!ready && waitingLabel != null) {
                waitingLabel.text = string.Format(waitingFormat, shown, TY_OfflineEarnings.Duration(minutes * 60d), prize);
                waitingLabel.color = waitingColor;
            }
        }
    }
}
