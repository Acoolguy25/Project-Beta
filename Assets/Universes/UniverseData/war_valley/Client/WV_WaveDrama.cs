using System;
using System.Collections.Generic;
using RyanAssets.GameFeel;
using RyanAssets.UI.GameHud;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// Stages the round for the commander: a horn and a banner as each wave begins, arrows at the edge
    /// of the screen pointing to the sides it comes from, an alarm and a red edge flash when the flag is
    /// hit (with an arrow to the flag if it is off screen), and a banner and fanfare when the round is
    /// won or lost.
    /// <para>
    /// Driven by the server's <see cref="WV_WaveStarted"/> and <see cref="WV_RoundEnded"/> broadcasts,
    /// which the client controller hands over, and by the flag's replicated health. Every piece is
    /// authored in the HUD prefab and wired here; this component only shows, hides, and places them.
    /// </para>
    /// </summary>
    public sealed class WV_WaveDrama : MonoBehaviour {
        [Header("Banner")]
        [SerializeField] HudBanner banner;
        [SerializeField] Sprite waveIcon;
        [SerializeField] Sprite bossIcon;
        [SerializeField] Sprite victoryIcon;
        [SerializeField] Sprite defeatIcon;
        [SerializeField, Min(0.5f)] float waveBannerSeconds = 4.5f;
        [SerializeField, Min(0.5f)] float resultBannerSeconds = 8f;
        [SerializeField] Color waveAccent = new(1f, 0.72f, 0.3f, 1f);
        [SerializeField] Color surgeAccent = new(1f, 0.45f, 0.25f, 1f);
        [SerializeField] Color bossAccent = new(0.95f, 0.25f, 0.2f, 1f);
        [SerializeField] Color victoryAccent = new(0.45f, 0.9f, 0.5f, 1f);
        [SerializeField] Color defeatAccent = new(0.85f, 0.25f, 0.22f, 1f);
        [Tooltip("Size of the banner's second line (theme, advice, boss), as a percentage of the subtitle's.")]
        [SerializeField, Range(50, 100)] int detailTextPercent = 85;
        [Tooltip("Size of the confetti burst over the flag when the round is won.")]
        [SerializeField, Min(0.1f)] float victoryConfettiScale = 4f;

        [Header("Sound")]
        [Tooltip("Volume of the kit's war horn blown as a wave begins.")]
        [SerializeField, Range(0f, 1f)] float hornVolume = 1f;
        [Tooltip("A boss wave's horn is pitched down by this many semitones.")]
        [SerializeField, Range(0f, 12f)] float bossHornDrop = 3f;
        [Tooltip("Camera kick when a boss wave begins.")]
        [SerializeField, Range(0f, 1f)] float bossKick = 0.25f;
        [SerializeField, Min(0.05f)] float bossKickSeconds = 0.4f;

        [Header("Approach Arrows")]
        [Tooltip("One arrow per approach, in WV_Approach order (west, north, south, east). Each is a " +
                 "container whose first child is the arrow image, rotated to point at the approach.")]
        [SerializeField] RectTransform[] approachArrows = Array.Empty<RectTransform>();
        [Tooltip("Seconds the arrows stay up after a wave begins.")]
        [SerializeField, Min(1f)] float arrowSeconds = 30f;
        [Tooltip("Gap kept between an arrow and the left and right screen edges, in canvas pixels.")]
        [SerializeField, Min(0f)] float edgeMargin = 80f;
        [Tooltip("Gap kept below the top edge, clear of the top bar, in canvas pixels.")]
        [SerializeField, Min(0f)] float topMargin = 110f;
        [Tooltip("Gap kept above the bottom edge, clear of the command bar and the funds and selection " +
                 "panels, in canvas pixels.")]
        [SerializeField, Min(0f)] float bottomMargin = 300f;
        [Tooltip("Gap kept between the arrows and the bottom of the wave banner while it shows, in canvas pixels.")]
        [SerializeField, Min(0f)] float bannerClearance = 24f;
        [Tooltip("Height above an approach's entrance an on-screen arrow hovers (m).")]
        [SerializeField, Min(0f)] float arrowHeight = 12f;

        [Header("Flag Alarm")]
        [Tooltip("Arrow to the flag, shown at the screen edge while it is under attack and out of view.")]
        [SerializeField] RectTransform flagArrow;
        [Tooltip("Seconds between alarms while the flag keeps being hit.")]
        [SerializeField, Min(1f)] float flagAlarmCooldown = 7f;
        [Tooltip("Seconds the flag arrow stays up after the last hit.")]
        [SerializeField, Min(1f)] float flagArrowSeconds = 6f;
        [Tooltip("Colour of the edge vignette while the flag is under attack. Drawn under the HUD.")]
        [SerializeField] Color flagDangerColor = new(1f, 0.16f, 0.1f, 1f);
        [Tooltip("Peak opacity of the edge flash on each alarm.")]
        [SerializeField, Range(0f, 1f)] float flagFlashAlpha = 0.3f;
        [SerializeField, Min(0.1f)] float flagFlashSeconds = 1.4f;
        [Tooltip("While the flag keeps being hit at or below this share of its health, the screen edges " +
                 "keep a heartbeat that grows as it falls; it fades once the attack stops.")]
        [SerializeField, Range(0f, 1f)] float flagLowHealth = 0.35f;
        [Tooltip("Peak opacity of the heartbeat at an almost-fallen flag.")]
        [SerializeField, Range(0f, 1f)] float flagDangerAlpha = 0.45f;
        [Tooltip("Heartbeat strength the moment the flag drops to the low-health line; it rises to full " +
                 "as the flag nears zero.")]
        [SerializeField, Range(0f, 1f)] float flagDangerFloor = 0.4f;
        [SerializeField, Min(0f)] float flagHeartbeatPerSecond = 1.3f;
        [SerializeField] string flagAlarmMessage = "<color=#FF5A4A>The flag is under attack! ({0}%)</color>";

        readonly List<WV_Approach> activeApproaches = new();
        Action<string> showHint;
        Canvas canvas;
        float arrowsUntil;
        float flagArrowUntil;
        float nextFlagAlarm;
        long lastFlagHealth = -1;
        WV_Flag watchedFlag;
        float lastDanger;
        bool alarmOnHint;
        readonly Vector3[] bannerCorners = new Vector3[4];

        /// <summary>Where alarm text goes: the HUD's hint line, given by the controller.</summary>
        public void Bind(Action<string> hint) => showHint = hint;

        void Awake() {
            canvas = GetComponentInParent<Canvas>();
            HideArrows();
            SetArrowVisible(flagArrow, false);
        }

        void OnDisable() {
            HideArrows();
            SetArrowVisible(flagArrow, false);
            if (lastDanger > 0f)
                ScreenFx.ClearDanger();
            lastDanger = 0f;
        }

        // --- Waves --------------------------------------------------------------

        public void ShowWave(WV_WaveStarted wave) {
            var theme = (WV_WaveTheme)wave.theme;
            WV_WavePlan.DecodeApproaches(wave.approaches, activeApproaches);
            arrowsUntil = Time.unscaledTime + arrowSeconds;

            Color accent = wave.hasBoss ? bossAccent : wave.isSurge ? surgeAccent : waveAccent;
            // The title stays short enough for one line of the banner; the theme leads the
            // subtitle instead, and only when it is not a plain assault.
            string title = wave.waveNumber >= wave.waveCount
                ? "FINAL WAVE"
                : $"WAVE {wave.waveNumber}/{wave.waveCount}";
            // The subtitle is broken on purpose into at most two lines that each fit the banner:
            // who comes and from where, then what kind of wave it is. A plain assault needs no
            // second line; a boss wave names its boss there instead of the theme's advice.
            string subtitle = $"{wave.enemyCount} {(wave.enemyCount == 1 ? "enemy" : "enemies")} from {DescribeSides()}";
            string themeName = WV_WavePlan.GetThemeName(theme).ToUpperInvariant();
            if (wave.hasBoss)
                subtitle += $"\n<size={detailTextPercent}%>{themeName} - led by a {WV_WaveBoss.DefaultName}</size>";
            else if (theme != WV_WaveTheme.Standard)
                subtitle += $"\n<size={detailTextPercent}%>{themeName} - {WV_WavePlan.GetThemeHint(theme)}</size>";
            if (banner != null)
                banner.Show(wave.hasBoss ? bossIcon : waveIcon, accent, title, subtitle, waveBannerSeconds);

            GameSfx.Play(SfxCue.Horn, hornVolume, wave.hasBoss ? GameSfx.Semitones(-bossHornDrop) : 1f);
            if (wave.hasBoss)
                CameraShake.Kick(bossKick, bossKickSeconds);
        }

        public void ShowRoundEnd(WV_RoundEnded result) {
            HideArrows();
            arrowsUntil = 0f;
            // The round is over, so an alarm still on the hint line no longer applies.
            if (alarmOnHint)
                showHint?.Invoke(string.Empty);
            alarmOnHint = false;
            // One fact per line, so neither wraps and leaves a word alone.
            string subtitle = result.victory
                ? $"All {result.wavesHeld} waves held\n{result.enemiesDefeated:N0} enemies defeated"
                : $"Fell on wave {result.waveReached} after holding {result.wavesHeld}\n{result.enemiesDefeated:N0} enemies defeated";
            if (banner != null)
                banner.Show(result.victory ? victoryIcon : defeatIcon,
                    result.victory ? victoryAccent : defeatAccent,
                    result.victory ? "VICTORY" : "DEFEAT", subtitle, resultBannerSeconds);
            GameSfx.Play(result.victory ? SfxCue.Win : SfxCue.Lose);
            if (result.victory && WV_Flag.Instance != null)
                GameFx.Play(FxKind.Confetti, WV_Flag.Instance.transform.position, victoryConfettiScale);
        }

        string DescribeSides() {
            if (activeApproaches.Count >= WV_WavePlan.ApproachCount)
                return "every side";
            var names = new List<string>(activeApproaches.Count);
            foreach (WV_Approach approach in activeApproaches)
                names.Add("the " + approach.ToString().ToLowerInvariant());
            return names.Count switch {
                0 => "the valley",
                1 => names[0],
                _ => string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1]
            };
        }

        // --- Per frame ----------------------------------------------------------

        void LateUpdate() {
            Camera camera = Camera.main;
            WatchFlag();
            if (camera == null) {
                HideArrows();
                SetArrowVisible(flagArrow, false);
                return;
            }

            float now = Time.unscaledTime;
            for (int i = 0; i < approachArrows.Length; i++) {
                RectTransform arrow = approachArrows[i];
                bool show = now < arrowsUntil && activeApproaches.Contains((WV_Approach)i);
                SetArrowVisible(arrow, show);
                if (show)
                    PlaceArrow(arrow, camera, WV_WavePlan.GetEntrance((WV_Approach)i) + Vector3.up * arrowHeight);
            }

            // The flag arrow is only for a flag the commander cannot see: a flag anywhere in the
            // view already shows its own bar, and an arrow there would sit on top of it.
            bool flagShown = now < flagArrowUntil && watchedFlag != null && !IsInView(camera, watchedFlag.transform.position);
            if (flagShown)
                PlaceArrow(flagArrow, camera, watchedFlag.transform.position + Vector3.up * arrowHeight);
            SetArrowVisible(flagArrow, flagShown);
        }

        /// <summary>True when <paramref name="world"/> is in front of the camera and inside the viewport.</summary>
        static bool IsInView(Camera camera, Vector3 world) {
            Vector3 viewport = camera.WorldToViewportPoint(world);
            return viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
        }

        /// <summary>
        /// Places an arrow over a world point, or pinned to the screen edge toward it when the point is
        /// off screen. Returns true when the point is on screen.
        /// </summary>
        bool PlaceArrow(RectTransform arrow, Camera camera, Vector3 world) {
            if (arrow == null)
                return false;
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            Vector2 size = new Vector2(Screen.width, Screen.height) / scale;
            Vector3 screen = camera.WorldToScreenPoint(world);
            bool behind = screen.z < 0f;
            Vector2 point = new Vector2(screen.x, screen.y) / scale;
            if (behind)
                point = size - point;

            var min = new Vector2(edgeMargin, bottomMargin);
            Vector2 max = size - new Vector2(edgeMargin, GetTopClearance(size, scale));
            bool onScreen = !behind && point.x >= min.x && point.x <= max.x && point.y >= min.y && point.y <= max.y;

            // Directions are taken from the middle of the free area, so an arrow pinned to an edge
            // points the same way the point lies from the battlefield the commander is looking at.
            Vector2 centre = (min + max) * 0.5f;
            Vector2 direction = point - centre;
            if (onScreen) {
                // Over the point itself, pointing down at it.
                arrow.anchoredPosition = point;
                SetArrowAngle(arrow, 180f);
                return true;
            }

            if (direction.sqrMagnitude < 0.01f)
                direction = Vector2.down;
            Vector2 half = max - centre;
            float stretch = Mathf.Min(
                Mathf.Abs(direction.x) > 0.001f ? half.x / Mathf.Abs(direction.x) : float.MaxValue,
                Mathf.Abs(direction.y) > 0.001f ? half.y / Mathf.Abs(direction.y) : float.MaxValue);
            arrow.anchoredPosition = centre + direction * stretch;
            SetArrowAngle(arrow, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
            return false;
        }

        /// <summary>
        /// How far below the top edge arrows must stay: under the top bar, and under the wave banner
        /// while it shows, so an approach straight ahead never lands its arrow on the banner.
        /// </summary>
        float GetTopClearance(Vector2 size, float scale) {
            if (banner == null || !banner.Visible)
                return topMargin;
            ((RectTransform)banner.transform).GetWorldCorners(bannerCorners);
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            // Corner 0 is the bottom-left.
            float bottom = RectTransformUtility.WorldToScreenPoint(eventCamera, bannerCorners[0]).y / scale;
            return Mathf.Max(topMargin, size.y - bottom + bannerClearance);
        }

        /// <summary>Turns the arrow image (the container's first child) and leaves its label upright.</summary>
        static void SetArrowAngle(RectTransform arrow, float degrees) {
            if (arrow.childCount > 0)
                arrow.GetChild(0).localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        void HideArrows() {
            foreach (RectTransform arrow in approachArrows)
                SetArrowVisible(arrow, false);
        }

        static void SetArrowVisible(RectTransform arrow, bool visible) {
            if (arrow != null && arrow.gameObject.activeSelf != visible)
                arrow.gameObject.SetActive(visible);
        }

        // --- Flag ---------------------------------------------------------------

        /// <summary>
        /// Raises the alarm when the flag loses health: a siren, an edge flash, a hint and an arrow
        /// to it. A badly hurt flag that is still being hit keeps a heartbeat on the screen edges,
        /// which fades as soon as the attack stops, so the screen is never left tinted.
        /// </summary>
        void WatchFlag() {
            WV_Flag flag = WV_Flag.Instance;
            if (flag != watchedFlag) {
                watchedFlag = flag;
                lastFlagHealth = flag != null ? flag.Health.Value : -1;
            }
            if (flag == null || flag.IsDead) {
                SetDanger(0f);
                return;
            }

            long health = flag.Health.Value;
            long max = flag.MaxHealth.Value;
            float fraction = max > 0 ? Mathf.Clamp01(health / (float)max) : 1f;
            float now = Time.unscaledTime;
            bool underAttack = now < flagArrowUntil;
            SetDanger(underAttack && fraction <= flagLowHealth && flagLowHealth > 0f
                ? Mathf.Lerp(1f, flagDangerFloor, fraction / flagLowHealth)
                : 0f);

            bool hit = lastFlagHealth >= 0 && health < lastFlagHealth;
            lastFlagHealth = health;
            if (!hit)
                return;
            flagArrowUntil = now + flagArrowSeconds;
            if (now < nextFlagAlarm)
                return;
            nextFlagAlarm = now + flagAlarmCooldown;
            GameSfx.Play(SfxCue.Alarm);
            ScreenFx.DangerFlash(flagDangerColor, flagFlashSeconds, flagFlashAlpha);
            showHint?.Invoke(string.Format(flagAlarmMessage, Mathf.RoundToInt(fraction * 100f)));
            alarmOnHint = true;
        }

        /// <summary>Forwards the heartbeat strength to the screen edges, only when it changes.</summary>
        void SetDanger(float strength) {
            if (Mathf.Approximately(strength, lastDanger))
                return;
            ScreenFx.SetDanger(strength, flagDangerColor, flagDangerAlpha, flagHeartbeatPerSecond);
            lastDanger = strength;
        }
    }
}
