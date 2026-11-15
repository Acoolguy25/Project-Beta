using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tycoon.Client {
    /// <summary>
    /// One row of the HUD build list: a lane's next item with its picture, effect, price and how
    /// close the player is to affording it. Built by TY_Authoring; the HUD binds data to it.
    /// </summary>
    public sealed class TY_BuildCard : MonoBehaviour {
        public TY_ItemKind lane;
        public Image picture;
        public TMP_Text title, effect, price, step;
        [Tooltip("Fill showing cash / price.")]
        public Image progressFill;
        public GameObject lockedBadge, doneBadge, readyBadge;
        public CanvasGroup group;

        /// <summary>Shows the lane's next item.</summary>
        public void Show(int item, Sprite itemPicture, long cash, bool locked, string lockedNote, int built, int total) {
            gameObject.SetActive(true);
            var data = TY_Catalog.Items[item];
            picture.sprite = itemPicture;
            picture.enabled = itemPicture != null;
            title.text = data.name;
            effect.text = locked ? lockedNote : data.Effect;
            price.text = data.price > 0 ? $"${TY_Catalog.Money(data.price)}" : "FREE";
            step.text = $"{built}/{total}";
            float progress = data.price <= 0 ? 1f : Mathf.Clamp01((float)cash / data.price);
            progressFill.fillAmount = locked ? 0f : progress;
            bool ready = !locked && progress >= 1f;
            lockedBadge.SetActive(locked);
            readyBadge.SetActive(ready);
            doneBadge.SetActive(false);
            group.alpha = locked ? 0.6f : 1f;
        }

        /// <summary>Shows a finished lane.</summary>
        public void ShowDone(int total) {
            gameObject.SetActive(true);
            picture.enabled = false;
            title.text = $"{TY_Lanes.Title(lane)} complete";
            effect.text = "Every upgrade in this lane is built";
            price.text = "";
            step.text = $"{total}/{total}";
            progressFill.fillAmount = 1f;
            lockedBadge.SetActive(false);
            readyBadge.SetActive(false);
            doneBadge.SetActive(true);
            group.alpha = 0.75f;
        }
    }
}
