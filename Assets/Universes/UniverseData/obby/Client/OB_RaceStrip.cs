using System.Collections.Generic;
using RyanAssets.GameFeel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// The race strip on the stage card: a dot on the progress track for every other racer and your
    /// place beside the stage name. Passes are shown on the place label itself (a green pop when you
    /// gain a place, red when someone passes you) instead of a toast, so they never fight the
    /// checkpoint toast that arrives at the same moment, and stay small in an eight-player start.
    /// </summary>
    public sealed class OB_RaceStrip : MonoBehaviour {
        [Tooltip("The progress track other racers are marked on (the stage card's bar).")]
        public RectTransform racerTrack;
        [Tooltip("Inactive marker under the track, copied once per other racer.")]
        public Image racerDotTemplate;
        [Tooltip("Your place in the race, beside the stage name.")]
        public TMP_Text placeLabel;
        public OB_Fx fx;

        [Header("Passes")]
        [Tooltip("Colour of the place label at rest.")]
        public Color restColor = OB_Palette.Gold;
        public Color gainColor = OB_Palette.Go;
        public Color lossColor = OB_Palette.Lava;
        [Tooltip("Seconds the gain/loss colour takes to fade back.")]
        [Min(0.05f)] public float flashSeconds = 2.2f;
        [Tooltip("Scale the place label pops in from on a gain (loss pops are smaller).")]
        public float gainPopFrom = 1.6f;
        public float lossPopFrom = 1.3f;
        [Min(0.01f)] public float popSeconds = 0.45f;
        [Tooltip("Shortest gap between two shown passes; the place text still updates in between.")]
        [Min(0f)] public float minPassGap = 2.5f;

        OB_StandingsBroadcast standings;
        int standingsSeed, shownPlace = -1, shownCount = -1;
        bool hasStandings, shown, flashing;
        readonly HashSet<int> ahead = new(), aheadBefore = new();
        readonly List<Image> racerDots = new();
        float flashStart = float.NegativeInfinity, nextPass;
        Color flashColor;

        void Awake() {
            racerDotTemplate.gameObject.SetActive(false);
            placeLabel.text = "";
            placeLabel.color = restColor;
        }

        void Update() {
            if (!flashing) return;
            float t = Mathf.Clamp01((Time.unscaledTime - flashStart) / flashSeconds);
            placeLabel.color = Color.Lerp(flashColor, restColor, t);
            // The last frame lands exactly on the rest colour.
            if (t >= 1f) flashing = false;
        }

        /// <summary>A new course: forget the old field.</summary>
        public void Clear() {
            hasStandings = false;
            standingsSeed = 0;
            ahead.Clear();
            Refresh(0, 0, false);
        }

        /// <summary>
        /// Takes new standings for the course <paramref name="seed"/>. Passes are only shown while
        /// <paramref name="racing"/>, between two updates of the same course.
        /// </summary>
        public void Apply(in OB_StandingsBroadcast message, int me, int stageCount, bool racing) {
            bool continuing = hasStandings && standingsSeed == message.seed;
            standings = message;
            standingsSeed = message.seed;
            hasStandings = true;
            aheadBefore.Clear();
            aheadBefore.UnionWith(ahead);
            OB_Scoring.AheadOf(message.racers, me, ahead);
            if (continuing && racing && message.racers.Length > 1 && OB_Scoring.IndexOf(message.racers, me) >= 0) {
                OB_Scoring.Overtakes(aheadBefore, ahead, message.racers, out int gained, out int lost);
                if ((gained > 0 || lost > 0) && Time.unscaledTime >= nextPass) {
                    nextPass = Time.unscaledTime + minPassGap;
                    // Net result: two gained and one lost in one update is still a gain.
                    bool gain = gained >= lost;
                    flashColor = gain ? gainColor : lossColor;
                    flashStart = Time.unscaledTime;
                    flashing = true;
                    placeLabel.color = flashColor;
                    Punch.Pop(placeLabel.rectTransform, gain ? gainPopFrom : lossPopFrom, popSeconds);
                    if (fx != null) {
                        if (gain) fx.GainedPlace();
                        else fx.LostPlace();
                    }
                }
            }
            Refresh(me, stageCount, racing);
        }

        /// <summary>Places a marker for every other racer and writes your place (shown while racing or on the results).</summary>
        public void Refresh(int me, int stageCount, bool show) {
            var racers = hasStandings ? standings.racers : null;
            int dots = 0;
            if (racers != null && stageCount > 0) {
                foreach (var racer in racers) {
                    if (racer.clientId == me) continue;
                    if (dots == racerDots.Count) racerDots.Add(Instantiate(racerDotTemplate, racerTrack));
                    var dot = racerDots[dots++];
                    dot.gameObject.SetActive(true);
                    float x = racer.finished ? 1f : Mathf.Clamp01((float)racer.stage / stageCount);
                    var rect = dot.rectTransform;
                    rect.anchorMin = new Vector2(x, rect.anchorMin.y);
                    rect.anchorMax = new Vector2(x, rect.anchorMax.y);
                    dot.color = racer.finished ? OB_Palette.Gold : OB_Palette.Paper;
                }
            }
            for (int i = dots; i < racerDots.Count; i++) racerDots[i].gameObject.SetActive(false);

            int place = racers != null ? OB_Scoring.PlaceOf(racers, me) : 0;
            int count = racers?.Length ?? 0;
            bool visible = show && place > 0 && count > 1;
            if (visible == shown && place == shownPlace && count == shownCount) return;
            shown = visible;
            shownPlace = place;
            shownCount = count;
            placeLabel.text = visible ? $"{OB_Scoring.Ordinal(place)}<size=65%><alpha=#99> / {count}</size>" : "";
        }
    }
}
