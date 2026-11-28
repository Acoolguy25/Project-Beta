using System;
using RyanAssets.GameFeel;
using RyanAssets.Input;
using RyanAssets.Retention.Client;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// The hand of doctrine cards a commander picks from between waves, with a redraw button when
    /// they have one. The hand folds to a small "Doctrine ready" pill (its button or the
    /// <see cref="foldInput"/> key) so it never has to cover the fight, and a pick flies the chosen
    /// card to its slot on the campaign card while the others fade. Authored by the HUD builder;
    /// shown and filled by <see cref="WV_DoctrinePresenter"/>.
    /// <para>
    /// The component sits on an always-active root: the hand (shadow, body and cards, so the shadow
    /// hides with it) and the pill are its children, and are what shows and hides.
    /// </para>
    /// </summary>
    public sealed class WV_DoctrineOfferPanel : MonoBehaviour {
        [Tooltip("The open hand: its shadow, glass body, title, cards and redraw button.")]
        [SerializeField] RectTransform hand;
        [Tooltip("Fades the hand out while a picked card flies to the campaign card.")]
        [SerializeField] CanvasGroup handGroup;
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] WV_DoctrineCard[] cards = Array.Empty<WV_DoctrineCard>();
        [SerializeField] Button rerollButton;
        [SerializeField] TextMeshProUGUI rerollLabel;
        [Tooltip("Folds the open hand to the pill.")]
        [SerializeField] Button foldButton;
        [Tooltip("Shown in place of a folded hand; a click opens the hand again.")]
        [SerializeField] RectTransform pill;
        [SerializeField] Button pillButton;
        [SerializeField] TextMeshProUGUI pillLabel;
        [Tooltip("Theme icons by name; the HUD builder points this at the retention HUD's table.")]
        [SerializeField] RetentionIcons icons;

        [Header("Input")]
        [Tooltip("Folds and opens the hand. N: D is the character's strafe and B the structure menu.")]
        [SerializeField] InputAction foldInput = new("Doctrines", InputActionType.Button, "<Keyboard>/n");

        [Header("Feel")]
        [Tooltip("How far each card pops in from, as a scale, staggered left to right.")]
        [SerializeField, Range(0.3f, 1f)] float popFrom = 0.7f;
        [SerializeField, Min(0.05f)] float popSeconds = 0.28f;
        [SerializeField, Min(0f)] float popStaggerSeconds = 0.06f;
        [Tooltip("The picked card's flight to its slot on the campaign card.")]
        [SerializeField, Min(0.1f)] float flySeconds = 0.55f;
        [Tooltip("Share of the flight spent lifting the picked card before it moves.")]
        [SerializeField, Range(0f, 0.6f)] float liftShare = 0.22f;
        [SerializeField, Range(1f, 1.4f)] float liftScale = 1.12f;
        [Tooltip("Seconds the cards not picked take to fade.")]
        [SerializeField, Min(0.05f)] float fadeSeconds = 0.2f;
        [Tooltip("Seconds between the folded pill's pulses.")]
        [SerializeField, Min(0.5f)] float pillPulseSeconds = 2.5f;

        [Header("Text")]
        [SerializeField] string titleFormat = "Choose a doctrine - wave {0}";
        [SerializeField] string openingTitle = "Opening orders - choose a doctrine";
        [Tooltip("Appended to the title when the hand carries a note (a catch-up hand, the day's featured card).")]
        [SerializeField] string noteFormat = "  <color=#F2B851>({0})</color>";
        [SerializeField] string rerollFormat = "Redraw ({0})";
        [Tooltip("The folded pill: {0} is the key.")]
        [SerializeField] string pillFormat = "Doctrine ready  ({0})";
        [Tooltip("Space between the cards and the redraw button; the row folds away with the button.")]
        [SerializeField, Min(0f)] float rerollGap = 10f;

        float[] popAt = Array.Empty<float>();
        byte[] shown = Array.Empty<byte>();
        // The authored layout (for the largest hand, with a redraw row), read once: the hand shrinks
        // around a smaller hand and folds the redraw row away, so it never shows an empty slot.
        Vector2 fullSize;
        float padding, cardWidth, cardStep, rerollRow;
        bool folded;
        float nextPulse;

        // The flight of a picked card.
        WV_DoctrineCard flying;
        int flyingSibling;
        Vector2 flyingAnchored;
        Vector3 flyFrom, flyTo;
        float flyStart, flyEndScale = 0.15f;
        Action landed;
        WV_DoctrineOffer? queued;

        /// <summary>A card was clicked: its index in the hand.</summary>
        public event Action<int> CardChosen;

        /// <summary>The redraw button was clicked.</summary>
        public event Action RerollClicked;

        /// <summary>Whether a hand is waiting, open or folded.</summary>
        public bool Visible => (hand != null && hand.gameObject.activeSelf) || (pill != null && pill.gameObject.activeSelf);

        /// <summary>Whether the waiting hand is folded to its pill.</summary>
        public bool Folded => folded && pill != null && pill.gameObject.activeSelf;

        /// <summary>The open hand's rectangle, or null while it is folded or closed.</summary>
        public RectTransform OpenHand => hand != null && hand.gameObject.activeSelf ? hand : null;

        void Awake() {
            for (int i = 0; i < cards.Length; i++) {
                int index = i;
                if (cards[i] != null)
                    cards[i].Clicked += () => CardChosen?.Invoke(index);
            }
            if (rerollButton != null)
                rerollButton.onClick.AddListener(() => RerollClicked?.Invoke());
            if (foldButton != null)
                foldButton.onClick.AddListener(() => SetFolded(true));
            if (pillButton != null)
                pillButton.onClick.AddListener(() => SetFolded(false));
            popAt = new float[cards.Length];
            shown = new byte[cards.Length];
            ReadLayout();
            if (pillLabel != null)
                pillLabel.text = string.Format(pillFormat, KeyName());
        }

        void OnEnable() => foldInput?.Enable();

        void OnDisable() => foldInput?.Disable();

        string KeyName() =>
            foldInput != null && foldInput.bindings.Count > 0 ? foldInput.GetBindingDisplayString(0) : "N";

        void ReadLayout() {
            if (hand == null || cards.Length == 0 || cards[0] == null)
                return;
            fullSize = hand.sizeDelta;
            var first = (RectTransform)cards[0].transform;
            padding = first.anchoredPosition.x;
            cardWidth = first.sizeDelta.x;
            cardStep = cards.Length > 1 && cards[1] != null
                ? ((RectTransform)cards[1].transform).anchoredPosition.x - padding
                : cardWidth;
            rerollRow = rerollButton != null ? ((RectTransform)rerollButton.transform).sizeDelta.y + rerollGap : 0f;
            if (titleLabel != null) {
                titleLabel.fontSizeMax = titleLabel.fontSize;
                titleLabel.fontSizeMin = Mathf.Min(14f, titleLabel.fontSize);
                titleLabel.enableAutoSizing = true;
            }
        }

        /// <summary>Fits the hand to <paramref name="count"/> cards, with or without the redraw row.</summary>
        void FitTo(int count, bool withReroll) {
            if (hand == null || count <= 0)
                return;
            float width = Mathf.Min(fullSize.x, padding * 2f + cardWidth + (count - 1) * cardStep);
            float height = fullSize.y - (withReroll ? 0f : rerollRow);
            hand.sizeDelta = new Vector2(width, height);
            if (titleLabel != null) {
                var title = titleLabel.rectTransform;
                // The fold button sits in the title row's right end.
                float reserve = foldButton != null ? ((RectTransform)foldButton.transform).sizeDelta.x + 8f : 0f;
                title.sizeDelta = new Vector2(width - title.anchoredPosition.x - padding - reserve, title.sizeDelta.y);
            }
        }

        /// <summary>
        /// Lays out the hand in <paramref name="offer"/> and pops the cards in. A hand that arrives
        /// while a picked card is still flying waits for it to land.
        /// </summary>
        public void Show(WV_DoctrineOffer offer) {
            if (flying != null) {
                queued = offer;
                return;
            }
            ShowNow(offer);
        }

        void ShowNow(WV_DoctrineOffer offer) {
            queued = null;
            if (handGroup != null)
                handGroup.alpha = 1f;
            if (titleLabel != null)
                titleLabel.text = (offer.wavesHeld > 0 ? string.Format(titleFormat, offer.wavesHeld) : openingTitle)
                                  + (string.IsNullOrEmpty(offer.note) ? string.Empty : string.Format(noteFormat, offer.note));

            int count = offer.doctrines?.Length ?? 0;
            for (int i = 0; i < cards.Length; i++) {
                WV_DoctrineCard card = cards[i];
                if (card == null)
                    continue;
                WV_DoctrineDefinition doctrine = i < count ? WV_DoctrineCatalog.Get((WV_DoctrineId)offer.doctrines[i]) : null;
                if (doctrine == null) {
                    card.Hide();
                    shown[i] = byte.MaxValue;
                    continue;
                }
                int rank = offer.ranks != null && i < offer.ranks.Length ? offer.ranks[i] : 0;
                bool featured = offer.featuredSlot == i + 1;
                card.Show(doctrine, rank, icons != null ? icons.Get(doctrine.Icon) : null, featured);
                shown[i] = offer.doctrines[i];
                card.transform.localScale = Vector3.one * popFrom;
                popAt[i] = Time.unscaledTime + popStaggerSeconds * i;
            }

            if (rerollButton != null) {
                rerollButton.gameObject.SetActive(offer.rerolls > 0);
                rerollButton.interactable = offer.rerolls > 0;
            }
            FitTo(Mathf.Min(count, cards.Length), offer.rerolls > 0);
            if (rerollLabel != null)
                rerollLabel.text = string.Format(rerollFormat, offer.rerolls);
            // Every new hand opens, so it is seen; the commander folds it if it is in the way.
            folded = false;
            ApplyFold();
        }

        /// <summary>Locks the cards while a pick is on its way to the server, so it cannot be sent twice.</summary>
        public void Lock() {
            foreach (WV_DoctrineCard card in cards) {
                if (card != null && card.Button != null)
                    card.Button.interactable = false;
            }
            if (rerollButton != null)
                rerollButton.interactable = false;
        }

        /// <summary>Closes the hand and its pill at once (a round reset, or the hand settled for the commander).</summary>
        public void Hide() {
            Land(invoke: true);
            queued = null;
            folded = false;
            if (hand != null)
                hand.gameObject.SetActive(false);
            if (pill != null)
                pill.gameObject.SetActive(false);
        }

        /// <summary>Folds the open hand to its pill, or opens it again.</summary>
        public void SetFolded(bool value) {
            if (!Visible || flying != null)
                return;
            folded = value;
            ApplyFold();
            GameSfx.Play(SfxCue.UiClick);
        }

        void ApplyFold() {
            if (hand != null)
                hand.gameObject.SetActive(!folded);
            if (pill != null) {
                pill.gameObject.SetActive(folded);
                if (folded) {
                    Punch.Pop(pill);
                    nextPulse = Time.unscaledTime + pillPulseSeconds;
                }
            }
        }

        /// <summary>
        /// The server took the pick of <paramref name="doctrine"/>: the card that shows it lifts, then
        /// flies to <paramref name="target"/> (its slot on the campaign card) while the hand fades, and
        /// <paramref name="onLanded"/> runs when it arrives. Without that card on screen (a folded hand,
        /// a hand settled for the commander) the hand just closes and <paramref name="onLanded"/> runs now.
        /// </summary>
        public void PlayPick(byte doctrine, RectTransform target, Action onLanded) {
            Land(invoke: true);
            int index = Array.IndexOf(shown, doctrine);
            bool open = hand != null && hand.gameObject.activeSelf;
            if (!open || index < 0 || cards[index] == null || target == null) {
                Hide();
                onLanded?.Invoke();
                return;
            }

            flying = cards[index];
            landed = onLanded;
            var rect = (RectTransform)flying.transform;
            flyingSibling = rect.GetSiblingIndex();
            flyingAnchored = rect.anchoredPosition;
            popAt[index] = 0f;
            rect.localScale = Vector3.one;
            flyFrom = Center(rect);
            // Out of the hand, so it stays solid while the hand fades.
            rect.SetParent(transform, true);
            rect.SetAsLastSibling();
            flyTo = Center(target);
            float width = Mathf.Max(1f, rect.rect.width * rect.lossyScale.x);
            flyEndScale = Mathf.Clamp(target.rect.width * target.lossyScale.x / width, 0.05f, 1f);
            flyStart = Time.unscaledTime;
            GameSfx.Play(SfxCue.Whoosh);
        }

        static Vector3 Center(RectTransform rect) => rect.TransformPoint(rect.rect.center);

        void Land(bool invoke) {
            if (flying == null)
                return;
            var rect = (RectTransform)flying.transform;
            if (hand != null) {
                rect.SetParent(hand, false);
                rect.SetSiblingIndex(flyingSibling);
            }
            rect.anchoredPosition = flyingAnchored;
            rect.localScale = Vector3.one;
            flying = null;
            if (hand != null)
                hand.gameObject.SetActive(false);
            if (pill != null)
                pill.gameObject.SetActive(false);
            if (handGroup != null)
                handGroup.alpha = 1f;
            Action callback = landed;
            landed = null;
            if (invoke)
                callback?.Invoke();
        }

        void Update() {
            float now = Time.unscaledTime;
            if (foldInput != null && foldInput.WasPressedThisFrame() && Visible
                && (InputService.Instance == null || InputService.GetActionMapActive(RyanAssetsActionMap.Character)))
                SetFolded(!folded);

            // Staggered pop: each card waits its turn, then springs from popFrom to full size.
            for (int i = 0; i < cards.Length && i < popAt.Length; i++) {
                if (popAt[i] <= 0f || now < popAt[i] || cards[i] == null)
                    continue;
                popAt[i] = 0f;
                cards[i].transform.localScale = Vector3.one;
                Punch.Pop(cards[i].transform, popFrom, popSeconds);
            }

            if (Folded && now >= nextPulse) {
                nextPulse = now + pillPulseSeconds;
                Punch.Pop(pill, 0.9f, 0.3f);
            }

            if (flying != null)
                Fly(now);
        }

        void Fly(float now) {
            float t = Mathf.Clamp01((now - flyStart) / flySeconds);
            if (handGroup != null)
                handGroup.alpha = 1f - Mathf.Clamp01((now - flyStart) / fadeSeconds);
            var rect = (RectTransform)flying.transform;
            Vector3 center;
            float scale;
            if (t < liftShare) {
                float u = liftShare > 0f ? t / liftShare : 1f;
                scale = Mathf.Lerp(1f, liftScale, 1f - (1f - u) * (1f - u));
                center = flyFrom;
            } else {
                float u = liftShare < 1f ? (t - liftShare) / (1f - liftShare) : 1f;
                float eased = u * u * (3f - 2f * u);
                scale = Mathf.Lerp(liftScale, flyEndScale, eased);
                center = Vector3.Lerp(flyFrom, flyTo, eased);
            }
            rect.localScale = Vector3.one * scale;
            rect.position = center - rect.TransformVector(rect.rect.center);
            if (t < 1f)
                return;
            Land(invoke: true);
            if (queued.HasValue)
                ShowNow(queued.Value);
        }
    }
}
