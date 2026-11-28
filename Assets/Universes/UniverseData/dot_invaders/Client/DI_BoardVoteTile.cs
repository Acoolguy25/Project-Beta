#if !UNITY_SERVER
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// One board in the board vote: a picture of the layout, its name, tagline and numbers, and the votes
    /// it has. Cloned from the template DI_Authoring builds; presentation only, the vote panel owns the logic.
    /// </summary>
    public sealed class DI_BoardVoteTile : MonoBehaviour {
        [SerializeField] Button button;
        [SerializeField] RawImage preview;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text tagline;
        [SerializeField] TMP_Text stats;
        [SerializeField] TMP_Text votes;
        [Tooltip("Outline shown round the board the local player voted for.")]
        [SerializeField] Image selectedFrame;
        [Tooltip("Accent stripe on the leading board (most votes).")]
        [SerializeField] Image leaderMark;

        Action<int> clicked;

        public int Option { get; private set; } = -1;

        public RectTransform Rect => (RectTransform)transform;

        void Awake() {
            if (button != null)
                button.onClick.AddListener(OnClick);
        }

        void OnDestroy() {
            if (button != null)
                button.onClick.RemoveListener(OnClick);
        }

        /// <summary>Shows <paramref name="layout"/> (null: an unknown board, by its vote title only).</summary>
        public void Bind(int option, string optionTitle, DI_BoardLayout layout, Texture picture, Action<int> onClick) {
            Option = option;
            clicked = onClick;
            gameObject.name = "Board " + optionTitle;
            title.text = layout != null ? layout.title : optionTitle;
            if (tagline != null) tagline.text = layout != null ? layout.tagline : string.Empty;
            if (stats != null) stats.text = layout != null ? layout.Stats : string.Empty;
            if (preview != null) {
                preview.texture = picture;
                preview.enabled = picture != null;
            }
        }

        public void SetState(bool selected, bool leading, bool canVote, string voteText, Color accent) {
            if (votes != null) votes.text = voteText;
            if (selectedFrame != null) {
                // The frame keeps its authored opacity: it tints the tile, it does not cover it.
                selectedFrame.enabled = selected;
                selectedFrame.color = new Color(accent.r, accent.g, accent.b, selectedFrame.color.a);
            }
            if (leaderMark != null) {
                leaderMark.enabled = leading;
                leaderMark.color = accent;
            }
            if (button != null) button.interactable = canVote;
        }

        void OnClick() {
            if (Option >= 0)
                clicked?.Invoke(Option);
        }
    }
}
#endif
