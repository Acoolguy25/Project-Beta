using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// The full-screen overlay behind <see cref="ScreenFx"/>: colour flash, damage and low-health
    /// vignettes, speed lines and the big centre text used by the countdown. Generated as a prefab by
    /// Ryan &gt; Game Feel &gt; Rebuild and created the first time an effect needs it. Its canvas sits
    /// under the HUD; the centre text has its own canvas above it.
    /// </summary>
    public sealed class ScreenFxOverlay : MonoBehaviour {
        [Header("Parts")]
        [SerializeField] Image flash;
        [SerializeField] Image damageVignette;
        [SerializeField] Image lowHealthVignette;
        [SerializeField] RectTransform speedLines;
        [SerializeField] Graphic speedLinesGraphic;
        [SerializeField] TMP_Text centerText;

        [Header("Damage pulse")]
        [SerializeField] Color damageColor = new(0.9f, 0.05f, 0.05f, 0.9f);
        [SerializeField, Min(0.05f)] float damageSeconds = 0.45f;

        [Header("Low health")]
        [SerializeField] Color lowHealthColor = new(0.8f, 0f, 0f, 0.8f);
        [Tooltip("Health fraction at or below which the pulse starts.")]
        [SerializeField, Range(0f, 1f)] float lowHealthThreshold = 0.3f;
        [Tooltip("Pulses per second at the threshold and at zero health.")]
        [SerializeField] Vector2 lowHealthRate = new(0.8f, 1.8f);
        [Tooltip("Vignette alpha between pulses (fraction of the pulse peak).")]
        [SerializeField, Range(0f, 1f)] float lowHealthFloor = 0.35f;

        [Header("Speed lines")]
        [SerializeField] Color speedLinesColor = new(1f, 1f, 1f, 0.75f);
        [Tooltip("Seconds between the lines' random re-rolls (the flicker).")]
        [SerializeField, Min(0.01f)] float speedLinesFlicker = 0.05f;
        [Tooltip("Scale wobble of the lines at full intensity.")]
        [SerializeField, Range(0f, 0.5f)] float speedLinesWobble = 0.06f;
        [Tooltip("Seconds to fade the lines in or out.")]
        [SerializeField, Min(0.01f)] float speedLinesFade = 0.2f;

        [Header("Centre text")]
        [SerializeField, Range(1f, 4f)] float centerPop = 1.8f;
        [Tooltip("Share of each step spent popping in.")]
        [SerializeField, Range(0.05f, 0.9f)] float centerPopShare = 0.25f;
        [Tooltip("Share of each step at whose end the text starts fading.")]
        [SerializeField, Range(0.1f, 1f)] float centerFadeStart = 0.7f;
        [SerializeField] Color countdownColor = Color.white;
        [SerializeField] Color goColor = new(0.35f, 1f, 0.5f);
        [Tooltip("Seconds each countdown number stays up.")]
        [SerializeField, Min(0.2f)] float countdownStep = 1f;
        [Tooltip("Seconds \"GO!\" stays up.")]
        [SerializeField, Min(0.2f)] float goSeconds = 0.9f;

        Color flashColor;
        float flashAge = float.PositiveInfinity, flashDuration = 1f;
        float damageAge = float.PositiveInfinity, damageStrength;
        float lowHealth01 = 1f, lowHealthPhase;
        float speedTarget, speedCurrent, speedNextRoll;

        // Centre text: either a single message or a running countdown.
        Color centerColor;
        float centerAge = float.PositiveInfinity, centerDuration;
        int countdownValue = -1;
        string countdownGo;
        Action countdownDone;

        void Awake() {
            SetAlpha(flash, 0f);
            SetAlpha(damageVignette, 0f);
            SetAlpha(lowHealthVignette, 0f);
            SetAlpha(speedLinesGraphic, 0f);
            if (centerText != null)
                centerText.gameObject.SetActive(false);
        }

        public void Flash(Color color, float seconds) {
            flashColor = color;
            flashDuration = Mathf.Max(0.01f, seconds);
            flashAge = 0f;
        }

        public void DamagePulse(float strength) {
            damageStrength = Mathf.Max(damageAge < damageSeconds ? DamageAlpha() : 0f, Mathf.Clamp01(strength));
            damageAge = 0f;
        }

        public void SetLowHealth(float health01) => lowHealth01 = Mathf.Clamp01(health01);

        public void SetSpeedLines(float intensity) => speedTarget = Mathf.Clamp01(intensity);

        public void ShowText(string text, Color color, float seconds) {
            CancelCountdown();
            StartCenter(text, color, seconds);
        }

        public void Countdown(int from, string goText, Action onGo) {
            CancelCountdown();
            countdownValue = Mathf.Max(1, from);
            countdownGo = goText;
            countdownDone = onGo;
            ShowCountdownStep();
        }

        public void CancelCountdown() {
            countdownValue = -1;
            countdownDone = null;
            centerAge = float.PositiveInfinity;
            if (centerText != null)
                centerText.gameObject.SetActive(false);
        }

        void ShowCountdownStep() {
            if (countdownValue > 0) {
                GameSfx.Play(SfxCue.CountdownBeep);
                StartCenterNumber(countdownValue, countdownColor, countdownStep);
            } else {
                GameSfx.Play(SfxCue.Go);
                StartCenter(countdownGo, goColor, goSeconds);
                Action done = countdownDone;
                countdownDone = null;
                done?.Invoke();
            }
        }

        void StartCenter(string text, Color color, float seconds) {
            if (centerText == null)
                return;
            centerText.SetText(text);
            BeginCenter(color, seconds);
        }

        void StartCenterNumber(int value, Color color, float seconds) {
            if (centerText == null)
                return;
            centerText.SetText("{0}", value);
            BeginCenter(color, seconds);
        }

        void BeginCenter(Color color, float seconds) {
            centerColor = color;
            centerDuration = Mathf.Max(0.05f, seconds);
            centerAge = 0f;
            centerText.gameObject.SetActive(true);
        }

        void Update() {
            float dt = Time.unscaledDeltaTime;
            UpdateFlash(dt);
            UpdateDamage(dt);
            UpdateLowHealth(dt);
            UpdateSpeedLines(dt);
            UpdateCenter(dt);
        }

        void UpdateFlash(float dt) {
            if (flash == null || float.IsPositiveInfinity(flashAge))
                return;
            flashAge += dt;
            float t = flashAge / flashDuration;
            if (t >= 1f) {
                flashAge = float.PositiveInfinity;
                SetAlpha(flash, 0f);
                return;
            }
            Color c = flashColor;
            float u = 1f - t;
            c.a *= u * u;
            flash.color = c;
        }

        float DamageAlpha() {
            float t = damageAge / damageSeconds;
            if (t >= 1f)
                return 0f;
            // Snap in, ease out.
            float u = 1f - t;
            return damageStrength * u * u;
        }

        void UpdateDamage(float dt) {
            if (damageVignette == null || float.IsPositiveInfinity(damageAge))
                return;
            damageAge += dt;
            float alpha = DamageAlpha();
            Color c = damageColor;
            c.a *= alpha;
            damageVignette.color = c;
            if (alpha <= 0f)
                damageAge = float.PositiveInfinity;
        }

        void UpdateLowHealth(float dt) {
            if (lowHealthVignette == null)
                return;
            if (lowHealth01 > lowHealthThreshold || lowHealthThreshold <= 0f) {
                if (lowHealthVignette.color.a > 0f)
                    SetAlpha(lowHealthVignette, 0f);
                lowHealthPhase = 0f;
                return;
            }
            float severity = 1f - lowHealth01 / lowHealthThreshold;
            lowHealthPhase += dt * Mathf.Lerp(lowHealthRate.x, lowHealthRate.y, severity);
            float beat = 0.5f + 0.5f * Mathf.Sin(lowHealthPhase * Mathf.PI * 2f);
            Color c = lowHealthColor;
            c.a *= Mathf.Lerp(lowHealthFloor, 1f, beat) * Mathf.Lerp(0.6f, 1f, severity);
            lowHealthVignette.color = c;
        }

        void UpdateSpeedLines(float dt) {
            if (speedLines == null || speedLinesGraphic == null)
                return;
            if (speedCurrent == 0f && speedTarget == 0f)
                return;
            speedCurrent = Mathf.MoveTowards(speedCurrent, speedTarget, dt / speedLinesFade);
            Color c = speedLinesColor;
            c.a *= speedCurrent;
            speedLinesGraphic.color = c;
            float now = Time.unscaledTime;
            if (now >= speedNextRoll) {
                speedNextRoll = now + speedLinesFlicker;
                speedLines.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                speedLines.localScale = Vector3.one * (1f + UnityEngine.Random.Range(-1f, 1f) * speedLinesWobble * speedCurrent);
            }
        }

        void UpdateCenter(float dt) {
            if (centerText == null || float.IsPositiveInfinity(centerAge))
                return;
            centerAge += dt;
            float t = centerAge / centerDuration;
            if (t >= 1f) {
                centerAge = float.PositiveInfinity;
                if (countdownValue > 0) {
                    countdownValue--;
                    ShowCountdownStep();
                    return;
                }
                countdownValue = -1;
                centerText.gameObject.SetActive(false);
                return;
            }
            float scale = t < centerPopShare ? Mathf.LerpUnclamped(centerPop, 1f, EaseOutBack(t / centerPopShare)) : 1f;
            centerText.rectTransform.localScale = Vector3.one * scale;
            Color c = centerColor;
            c.a *= t < centerFadeStart ? Mathf.Clamp01(t / (centerPopShare * 0.5f)) : 1f - (t - centerFadeStart) / (1f - centerFadeStart);
            centerText.color = c;
        }

        static void SetAlpha(Graphic graphic, float alpha) {
            if (graphic == null)
                return;
            Color c = graphic.color;
            c.a = alpha;
            graphic.color = c;
        }

        static float EaseOutBack(float t) {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
