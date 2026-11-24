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
        const string Warn = "<color=#FF9A8A>";

        /// <summary>The day's modifier (range and fire rate shown as the server plays them); set by TD_Hud from the state.</summary>
        [System.NonSerialized] public TD_Modifier modifier;

        /// <summary>Shows an empty pad: building the selected tower (or what unlocks it).</summary>
        /// <param name="unlockLevel">The level that unlocks the tower, or 0 when the player owns it.</param>
        public void ShowBuild(TD_TowerKind kind, Sprite towerPortrait, int cash, int unlockLevel = 0) {
            var t = TD_Rules.Tower(kind);
            Header(kind, towerPortrait, $"Build {t.name}", t.blurb, 0);
            Stats(kind, 1, false);
            if (unlockLevel > 0) Action($"Unlocks at level {unlockLevel}", actionDone);
            else Action($"Build  ${t.cost}", cash >= t.cost ? actionReady : actionBlocked);
            sellLabel.gameObject.SetActive(false);
            fader.Set(true);
        }

        /// <summary>Shows a built tower, its upgrade (at <paramref name="upgradeCost"/>, the viewer's price) and its sale value.</summary>
        public void ShowTower(TD_TowerSnap tower, Sprite towerPortrait, int cash, string sellKey, bool canSell, int upgradeCost) {
            var t = TD_Rules.Tower(tower.kind);
            string owner = string.IsNullOrEmpty(tower.owner) ? "Unclaimed" : $"Built by {tower.owner}";
            if (tower.veteran) owner += $"  <color=#7CFF8A>Veteran +{Mathf.RoundToInt((TD_Retention.VeteranDamage - 1f) * 100f)}%</color>";
            Header(tower.kind, towerPortrait, t.name, owner, tower.level, tower.veteran ? TD_Retention.VeteranDamage : 1f);
            bool max = tower.level >= TD_Rules.MaxLevel;
            Stats(tower.kind, tower.level, !max, tower.veteran ? TD_Retention.VeteranDamage : 1f);
            if (max) Action("Max level", actionDone);
            else Action($"Upgrade to Lv {tower.level + 1}  ${upgradeCost}", cash >= upgradeCost ? actionReady : actionBlocked);
            int refund = RefundOf(tower), standard = TD_Rules.SellRefund(tower.kind, tower.level);
            // The builder's perks (Salvage Crew, Field Engineers) change the figure: name the bonus, not a rate.
            string note = refund == standard ? $"({Mathf.RoundToInt(TD_Rules.SellRefundRate * 100f)}% back)" : refund > standard ? "(Salvage Crew)" : "";
            sellLabel.gameObject.SetActive(true);
            sellLabel.text = canSell
                ? $"<b>{sellKey}</b> ×2  Sell for ${refund}  <color=#B4BCC8>{note}</color>"
                : $"<color=#B4BCC8>Only {tower.owner} can sell this tower</color>";
            fader.Set(true);
        }

        public void Hide() => fader.Set(false);

        void Header(TD_TowerKind kind, Sprite towerPortrait, string heading, string sub, int level, float damageScale = 1f) {
            portrait.sprite = towerPortrait;
            accentBar.color = TD_Palette.Tower(kind);
            title.text = heading;
            subtitle.text = sub;
            for (int i = 0; i < stars.Length; i++) {
                stars[i].gameObject.SetActive(level > 0);
                stars[i].color = i < level ? starOn : starOff;
            }
            tagsLabel.text = Tags(kind, Mathf.Max(1, level), damageScale);
        }

        /// <summary>What selling pays: the server's figure (with the builder's Salvage perk), or the base rate from older servers.</summary>
        public static int RefundOf(in TD_TowerSnap tower) => tower.refund > 0 ? tower.refund : TD_Rules.SellRefund(tower.kind, tower.level);

        void Stats(TD_TowerKind kind, int level, bool showNext, float damageScale = 1f) {
            int next = Mathf.Min(level + 1, TD_Rules.MaxLevel);
            damageValue.text = Value(TD_Rules.Damage(kind, level) * damageScale, TD_Rules.Damage(kind, next) * damageScale, showNext, "0");
            rangeValue.text = Value(TD_Daily.Range(kind, level, modifier), TD_Daily.Range(kind, next, modifier), showNext, "0", "m");
            rateValue.text = Value(TD_Daily.FireRate(kind, level, modifier), TD_Daily.FireRate(kind, next, modifier), showNext, "0.0", "/s");
        }

        static string Value(float now, float next, bool showNext, string format, string unit = "") =>
            showNext ? $"{now.ToString(format)}{unit}  {Up}→ {next.ToString(format)}{unit}</color>" : $"{now.ToString(format)}{unit}";

        void Action(string text, Color color) {
            actionLabel.text = text;
            actionBack.color = color;
        }

        /// <summary>
        /// Short capability list at <paramref name="level"/>, e.g. "Hits air  ·  Slows 45%", with a warning
        /// when its hits (times <paramref name="damageScale"/>, Veteran Crews) bounce off shields.
        /// </summary>
        public static string Tags(TD_TowerKind kind, int level, float damageScale = 1f) {
            var t = TD_Rules.Tower(kind);
            var parts = new System.Collections.Generic.List<string> { t.hitsAir ? "Hits air" : "Ground only" };
            if (TD_Rules.BouncesOffShields(TD_Rules.Damage(kind, level) * damageScale)) parts.Add($"{Warn}Bounces off shields</color>");
            if (t.splash > 0f) parts.Add("Splash");
            if (t.slow > 0f) parts.Add($"Slows {TD_Rules.SlowPercent(kind, level)}%"); // the server slows by TD_Rules.SlowFactor too
            if (TD_Rules.Pierces(kind)) parts.Add("Pierces armour");
            if (t.chain > 0) parts.Add($"Chains to {TD_Rules.ChainTargets(kind, level)}");
            if (t.minRange > 0f) parts.Add($"Min range {t.minRange:0}m");
            return string.Join("  ·  ", parts);
        }
    }
}
