using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// One clickable doctrine card in the offer panel: icon, name, rank and what it does, in its
    /// family's accent (economy, troops, armour, base, flag; elite cards in purple), with a
    /// "Today's front" ribbon on the card the day features. Authored by the HUD builder; this only
    /// writes the card's contents.
    /// </summary>
    public sealed class WV_DoctrineCard : MonoBehaviour {
        [SerializeField] Button button;
        [SerializeField] Image background;
        [SerializeField] Image accent;
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] TextMeshProUGUI rankLabel;
        [SerializeField] TextMeshProUGUI descriptionLabel;
        [Tooltip("The family's name, under the rank (\"TROOPS\").")]
        [SerializeField] TextMeshProUGUI familyLabel;
        [Tooltip("The \"Today's front\" ribbon, shown on the day's featured card.")]
        [SerializeField] GameObject featuredRibbon;

        [Header("Colours (the family accents are WV_DoctrineCard.AccentFor, shared with the campaign card)")]
        [SerializeField] Color commonBackground = new(0.11f, 0.12f, 0.15f, 0.96f);
        [SerializeField] Color eliteBackground = new(0.17f, 0.11f, 0.22f, 0.97f);
        [Tooltip("How much of the accent tints a common card's body.")]
        [SerializeField, Range(0f, 0.3f)] float backgroundTint = 0.08f;

        /// <summary>The card was clicked.</summary>
        public event Action Clicked;

        public Button Button => button;

        void Awake() {
            if (button != null)
                button.onClick.AddListener(() => Clicked?.Invoke());
        }

        static readonly Color EconomyAccent = new(0.95f, 0.72f, 0.32f, 1f);
        static readonly Color TroopsAccent = new(0.48f, 0.86f, 0.45f, 1f);
        static readonly Color ArmourAccent = new(0.45f, 0.70f, 1f, 1f);
        static readonly Color BaseAccent = new(1f, 0.52f, 0.36f, 1f);
        static readonly Color FlagAccent = new(0.36f, 0.88f, 0.86f, 1f);
        static readonly Color EliteAccent = new(0.78f, 0.45f, 1f, 1f);

        /// <summary>
        /// The accent a doctrine is drawn in, on its card and its campaign-card slot: one per family
        /// (economy gold, troops green, armour blue, base orange, flag teal), elite cards purple.
        /// </summary>
        public static Color AccentFor(WV_DoctrineDefinition doctrine) {
            if (doctrine == null)
                return EconomyAccent;
            if (doctrine.Rarity == WV_DoctrineRarity.Elite)
                return EliteAccent;
            return WV_DoctrineCatalog.Family(doctrine) switch {
                WV_DoctrineFamily.Troops => TroopsAccent,
                WV_DoctrineFamily.Armour => ArmourAccent,
                WV_DoctrineFamily.Base => BaseAccent,
                WV_DoctrineFamily.Flag => FlagAccent,
                _ => EconomyAccent
            };
        }

        static string FamilyName(WV_DoctrineDefinition doctrine) =>
            doctrine.Rarity == WV_DoctrineRarity.Elite ? "ELITE" : WV_DoctrineCatalog.Family(doctrine) switch {
                WV_DoctrineFamily.Troops => "TROOPS",
                WV_DoctrineFamily.Armour => "ARMOUR",
                WV_DoctrineFamily.Base => "BASE",
                WV_DoctrineFamily.Flag => "FLAG",
                _ => "ECONOMY"
            };

        /// <summary>
        /// Shows <paramref name="doctrine"/> at the commander's <paramref name="currentRank"/>;
        /// <paramref name="featured"/> marks the day's featured card.
        /// </summary>
        public void Show(WV_DoctrineDefinition doctrine, int currentRank, Sprite sprite, bool featured = false) {
            bool elite = doctrine.Rarity == WV_DoctrineRarity.Elite;
            Color tint = AccentFor(doctrine);
            if (background != null) {
                Color body = Color.Lerp(commonBackground, tint, backgroundTint);
                body.a = commonBackground.a;
                background.color = elite ? eliteBackground : body;
            }
            if (accent != null)
                accent.color = tint;
            if (icon != null) {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
                icon.color = tint;
            }
            if (titleLabel != null)
                titleLabel.text = doctrine.Title;
            if (rankLabel != null)
                rankLabel.text = elite ? "Elite - once a round" : WV_DoctrineCatalog.DescribeRank(doctrine, currentRank);
            if (familyLabel != null) {
                familyLabel.text = FamilyName(doctrine);
                familyLabel.color = tint;
            }
            if (descriptionLabel != null)
                descriptionLabel.text = doctrine.Description;
            if (featuredRibbon != null)
                featuredRibbon.SetActive(featured);
            if (button != null)
                button.interactable = true;
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
