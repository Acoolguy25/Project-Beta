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
        [Tooltip("The action row's key cap (hidden when the row only states a fact, such as a fully upgraded tower).")]
        public GameObject actionKey;
        public TMP_Text title, subtitle, damageValue, rangeValue, rateValue, actionLabel, tagsLabel;
        [Tooltip("What selling the focused tower pays back (hidden on an empty pad).")]
        public TMP_Text sellLabel;
        [Tooltip("A built tower's targeting mode (and the key that cycles it) and what it has done this game.")]
        public TMP_Text targetLabel, recordLabel;
        public Image[] stars;
        [Tooltip("Level 3 towers: the two elite forms (index 0 = TD_TowerPath.First), shown instead of the action and sell rows.")]
        public GameObject choices;
        public Image[] choiceBack;
        public TMP_Text[] choiceKey, choiceName, choiceBlurb;
        [Range(0f, 1f)] public float choiceTint = 0.28f, choiceLockedTint = 0.08f;
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
            ShowChoices(false);
            // Same rows as a built tower's card, so the card keeps its size: how it will aim, and what it sells back for.
            ShowRecord(true);
            if (targetLabel != null) targetLabel.text = $"Target  <b>{TD_Targeting.Name(TD_Targeting.Default(kind))}</b>   <color=#B4BCC8>change it once built</color>";
            if (recordLabel != null) recordLabel.text = $"<color=#B4BCC8>Sells back for ${TD_Rules.SellRefund(kind, 1)}</color>";
            fader.Set(true);
        }

        /// <summary>
        /// Shows a built tower, its upgrade (at <paramref name="upgradeCost"/>, the viewer's price) and its sale
        /// value. At level 3 the upgrade row becomes the two elite forms (<paramref name="eliteDiscount"/> is the
        /// viewer's price cut; <paramref name="eliteUnlocked"/> whether they may buy one, else the cards say when).
        /// </summary>
        public void ShowTower(TD_TowerSnap tower, Sprite towerPortrait, int cash, string sellKey, string targetKey, bool canSell, int upgradeCost,
            bool eliteUnlocked = false, float eliteDiscount = 0f, string firstKey = "E", string secondKey = "Z") {
            var t = TD_Rules.Tower(tower.kind);
            bool elite = TD_Elites.IsElite(tower.path);
            string owner = string.IsNullOrEmpty(tower.owner) ? "Unclaimed" : $"Built by {tower.owner}";
            // Elite towers name their form in the title; the subtitle stays one line.
            if (elite) owner = $"<color=#{ColorUtility.ToHtmlStringRGB(TD_Palette.Elite(tower.path))}>Elite</color>  ·  {(string.IsNullOrEmpty(tower.owner) ? "Unclaimed" : tower.owner)}";
            if (tower.veteran) owner += $"  <color=#7CFF8A>Veteran +{Mathf.RoundToInt((TD_Retention.VeteranDamage - 1f) * 100f)}%</color>";
            float veteran = tower.veteran ? TD_Retention.VeteranDamage : 1f;
            Header(tower.kind, towerPortrait, TD_Elites.Name(tower.kind, tower.path), owner, tower.level, veteran, tower.path);
            bool max = tower.level >= TD_Rules.MaxLevel;
            bool choosing = max && !elite;
            Stats(tower.kind, tower.level, !max, veteran, tower.path);
            ShowChoices(choosing);
            if (choosing) Choices(tower.kind, cash, eliteUnlocked, eliteDiscount, firstKey, secondKey);
            else if (max) Action(elite ? TD_Elites.Get(tower.kind, tower.path).blurb : "Max level", actionDone);
            else Action($"Upgrade to Lv {tower.level + 1}  ${upgradeCost}", cash >= upgradeCost ? actionReady : actionBlocked);
            int refund = RefundOf(tower), standard = TD_Rules.SellRefund(tower.kind, tower.level);
            // The builder's perks (Salvage Crew, Field Engineers) change the figure: name the bonus, not a rate.
            string note = refund == standard ? $"({Mathf.RoundToInt(TD_Rules.SellRefundRate * 100f)}% back)" : refund > standard ? "(Salvage Crew)" : "";
            sellLabel.gameObject.SetActive(!choosing);
            sellLabel.text = canSell
                ? $"<b>{sellKey}</b> ×2  Sell for ${refund}  <color=#B4BCC8>{note}</color>"
                : $"<color=#B4BCC8>Only {tower.owner} can sell this tower</color>";
            ShowRecord(true);
            if (targetLabel != null) {
                string mode = $"Target  <b>{TD_Targeting.Name(tower.target)}</b>";
                targetLabel.text = canSell ? $"{mode}   <color=#B4BCC8><b>{targetKey}</b> change</color>" : mode;
            }
            if (recordLabel != null)
                recordLabel.text = tower.kills > 0 || tower.damage > 0
                    ? $"{tower.kills} {(tower.kills == 1 ? "kill" : "kills")}  ·  {TD_Targeting.Short(tower.damage)} damage"
                    : "<color=#B4BCC8>No hits yet</color>";
            fader.Set(true);
        }

        public void Hide() => fader.Set(false);

        /// <summary>Swaps the action row for the two elite cards (or back).</summary>
        void ShowChoices(bool show) {
            if (choices != null) choices.SetActive(show);
            if (actionBack != null) actionBack.gameObject.SetActive(!show);
        }

        void Choices(TD_TowerKind kind, int cash, bool unlocked, float discount, string firstKey, string secondKey) {
            if (choiceName == null) return;
            for (int i = 0; i < choiceName.Length && i < TD_Elites.PathCount; i++) {
                var path = (TD_TowerPath)(i + 1);
                var elite = TD_Elites.Get(kind, path);
                int cost = TD_Elites.Cost(kind, path, discount);
                bool affordable = cash >= cost;
                string price = !unlocked ? $"<color=#B4BCC8>Lv {TD_Retention.EliteLevel}</color>" : affordable ? $"${cost}" : $"{Warn}${cost}</color>";
                choiceName[i].text = $"{elite.name}  {price}";
                choiceBlurb[i].text = elite.blurb;
                if (choiceKey != null && i < choiceKey.Length && choiceKey[i] != null) choiceKey[i].text = i == 0 ? firstKey : secondKey;
                if (choiceBack != null && i < choiceBack.Length && choiceBack[i] != null) {
                    Color c = TD_Palette.Elite(path);
                    c.a = unlocked && affordable ? choiceTint : choiceLockedTint;
                    choiceBack[i].color = c;
                }
            }
        }

        void ShowRecord(bool show) {
            if (targetLabel != null) targetLabel.gameObject.SetActive(show);
            if (recordLabel != null) recordLabel.gameObject.SetActive(show);
        }

        void Header(TD_TowerKind kind, Sprite towerPortrait, string heading, string sub, int level, float damageScale = 1f, TD_TowerPath path = TD_TowerPath.None) {
            bool elite = TD_Elites.IsElite(path);
            portrait.sprite = towerPortrait;
            accentBar.color = elite ? TD_Palette.Elite(path) : TD_Palette.Tower(kind);
            title.text = heading;
            subtitle.text = sub;
            for (int i = 0; i < stars.Length; i++) {
                stars[i].gameObject.SetActive(level > 0);
                stars[i].color = elite ? TD_Palette.Elite(path) : i < level ? starOn : starOff;
            }
            tagsLabel.text = Tags(kind, Mathf.Max(1, level), damageScale, path);
        }

        /// <summary>What selling pays: the server's figure (with the builder's Salvage perk), or the base rate from older servers.</summary>
        public static int RefundOf(in TD_TowerSnap tower) => tower.refund > 0 ? tower.refund : TD_Rules.SellRefund(tower.kind, tower.level);

        void Stats(TD_TowerKind kind, int level, bool showNext, float damageScale = 1f, TD_TowerPath path = TD_TowerPath.None) {
            int next = Mathf.Min(level + 1, TD_Rules.MaxLevel);
            damageValue.text = Value(TD_Elites.Damage(kind, level, path) * damageScale, TD_Rules.Damage(kind, next) * damageScale, showNext, "0");
            rangeValue.text = Value(TD_Elites.Range(kind, level, path, modifier), TD_Daily.Range(kind, next, modifier), showNext, "0", "m");
            rateValue.text = Value(TD_Elites.FireRate(kind, level, path, modifier), TD_Daily.FireRate(kind, next, modifier), showNext, "0.0", "/s");
        }

        static string Value(float now, float next, bool showNext, string format, string unit = "") =>
            showNext ? $"{now.ToString(format)}{unit}  {Up}→ {next.ToString(format)}{unit}</color>" : $"{now.ToString(format)}{unit}";

        void Action(string text, Color color) {
            actionLabel.text = text;
            actionBack.color = color;
            if (actionKey != null) actionKey.SetActive(color != actionDone);
        }

        /// <summary>
        /// Short capability list at <paramref name="level"/>, e.g. "Hits air  ·  Slows 45%", with a warning
        /// when its hits (times <paramref name="damageScale"/>, Veteran Crews) bounce off shields.
        /// </summary>
        public static string Tags(TD_TowerKind kind, int level, float damageScale = 1f, TD_TowerPath path = TD_TowerPath.None) {
            var t = TD_Rules.Tower(kind);
            var elite = TD_Elites.Get(kind, path);
            var parts = new System.Collections.Generic.List<string> { t.hitsAir ? "Hits air" : "Ground only" };
            if (TD_Rules.BouncesOffShields(TD_Elites.Damage(kind, level, path) * damageScale)) parts.Add($"{Warn}Bounces off shields</color>");
            if (t.splash > 0f) parts.Add("Splash");
            if (elite.airBurst > 0f) parts.Add("Air bursts");
            if (elite.stunSeconds > 0f) parts.Add("Stuns");
            if (elite.brittle) parts.Add("Brittle");
            if (t.slow > 0f) parts.Add($"Slows {TD_Elites.SlowPercent(kind, level, path)}%"); // the server slows by TD_Elites.SlowFactor too
            if (TD_Rules.Pierces(kind)) parts.Add("Pierces armour");
            if (t.chain > 0) parts.Add($"Chains to {TD_Elites.ChainTargets(kind, level, path)}");
            if (t.minRange > 0f) parts.Add($"Min range {t.minRange:0}m");
            return string.Join("  ·  ", parts);
        }
    }
}
