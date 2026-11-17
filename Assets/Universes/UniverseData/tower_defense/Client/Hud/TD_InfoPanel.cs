using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// Card for the pad or tower the player is looking at: what it is, its stats (and what the next
    /// level adds) and the action E performs.
    /// </summary>
    public sealed class TD_InfoPanel : MonoBehaviour {
        public TD_Fader fader;
        public Image portrait, accentBar, actionBack;
        public TMP_Text title, subtitle, damageValue, rangeValue, rateValue, actionLabel, tagsLabel;
        [Tooltip("What selling the focused tower pays back (hidden on an empty pad).")]
        public TMP_Text sellLabel;
        public Image[] stars;
        public Color starOn = new(1f, 0.8f, 0.24f), starOff = new(1f, 1f, 1f, 0.15f);
        public Color actionReady = new(0.2f, 0.78f, 0.48f), actionBlocked = new(0.95f, 0.33f, 0.36f), actionDone = new(1f, 1f, 1f, 0.12f);

        const string Up = "<color=#7CFF8A>";

        /// <summary>Shows an empty pad: building the selected tower.</summary>
        public void ShowBuild(TD_TowerKind kind, Sprite towerPortrait, int cash) {
            var t = TD_Rules.Tower(kind);
            Header(kind, towerPortrait, $"Build {t.name}", t.blurb, 0);
            Stats(kind, 1, false);
            Action($"Build  ${t.cost}", cash >= t.cost ? actionReady : actionBlocked);
            sellLabel.gameObject.SetActive(false);
            fader.Set(true);
        }

        /// <summary>Shows a built tower, its upgrade and its sale value.</summary>
        public void ShowTower(TD_TowerSnap tower, Sprite towerPortrait, int cash, string sellKey, bool canSell) {
            var t = TD_Rules.Tower(tower.kind);
            string owner = string.IsNullOrEmpty(tower.owner) ? "Unclaimed" : $"Built by {tower.owner}";
            Header(tower.kind, towerPortrait, t.name, owner, tower.level);
            bool max = tower.level >= TD_Rules.MaxLevel;
            Stats(tower.kind, tower.level, !max);
            if (max) Action("Max level", actionDone);
            else {
                int cost = TD_Rules.UpgradeCost(tower.kind, tower.level);
                Action($"Upgrade to Lv {tower.level + 1}  ${cost}", cash >= cost ? actionReady : actionBlocked);
            }
            sellLabel.gameObject.SetActive(true);
            sellLabel.text = canSell
                ? $"<b>{sellKey}</b> ×2  Sell for ${TD_Rules.SellRefund(tower.kind, tower.level)}  <color=#B4BCC8>({Mathf.RoundToInt(TD_Rules.SellRefundRate * 100f)}% back)</color>"
                : $"<color=#B4BCC8>Only {tower.owner} can sell this tower</color>";
            fader.Set(true);
        }

        public void Hide() => fader.Set(false);

        void Header(TD_TowerKind kind, Sprite towerPortrait, string heading, string sub, int level) {
            portrait.sprite = towerPortrait;
            accentBar.color = TD_Palette.Tower(kind);
            title.text = heading;
            subtitle.text = sub;
            for (int i = 0; i < stars.Length; i++) {
                stars[i].gameObject.SetActive(level > 0);
                stars[i].color = i < level ? starOn : starOff;
            }
            tagsLabel.text = Tags(kind, Mathf.Max(1, level));
        }

        void Stats(TD_TowerKind kind, int level, bool showNext) {
            int next = Mathf.Min(level + 1, TD_Rules.MaxLevel);
            damageValue.text = Value(TD_Rules.Damage(kind, level), TD_Rules.Damage(kind, next), showNext, "0");
            rangeValue.text = Value(TD_Rules.Range(kind, level), TD_Rules.Range(kind, next), showNext, "0", "m");
            rateValue.text = Value(TD_Rules.FireRate(kind, level), TD_Rules.FireRate(kind, next), showNext, "0.0", "/s");
        }

        static string Value(float now, float next, bool showNext, string format, string unit = "") =>
            showNext ? $"{now.ToString(format)}{unit}  {Up}→ {next.ToString(format)}{unit}</color>" : $"{now.ToString(format)}{unit}";

        void Action(string text, Color color) {
            actionLabel.text = text;
            actionBack.color = color;
        }

        /// <summary>Short capability list at <paramref name="level"/>, e.g. "Hits air  ·  Slows 45%".</summary>
        public static string Tags(TD_TowerKind kind, int level) {
            var t = TD_Rules.Tower(kind);
            var parts = new System.Collections.Generic.List<string> { t.hitsAir ? "Hits air" : "Ground only" };
            if (t.splash > 0f) parts.Add("Splash");
            if (t.slow > 0f) parts.Add($"Slows {TD_Rules.SlowPercent(kind, level)}%"); // the server slows by TD_Rules.SlowFactor too
            if (kind == TD_TowerKind.Sniper) parts.Add("Pierces armour");
            return string.Join("  ·  ", parts);
        }
    }
}
