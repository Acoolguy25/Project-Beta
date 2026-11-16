using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>How a buy pad looks to the local player.</summary>
    public enum TY_ButtonLook : byte {
        /// <summary>Yours and you can afford it.</summary>
        Affordable,
        /// <summary>Yours but you need more cash.</summary>
        Expensive,
        /// <summary>Yours but another item must be built first.</summary>
        Locked,
        /// <summary>Someone else's plot.</summary>
        Foreign,
    }

    /// <summary>
    /// A lane's buy pad: the pad itself plus a floating sign with the next item's picture, name,
    /// effect, price and a savings bar. One per lane per plot; the client points it at the lane's next item.
    /// </summary>
    public sealed class TY_ButtonView : MonoBehaviour {
        [Header("Sign")]
        public TMP_Text title;
        public TMP_Text effect;
        public TMP_Text price;
        public SpriteRenderer picture;
        public Color affordablePrice = new(0.45f, 1f, 0.55f);
        public Color expensivePrice = new(1f, 0.55f, 0.5f);
        public Color mutedPrice = new(0.8f, 0.82f, 0.86f);
        [Tooltip("Picture tint while the item is locked or on someone else's plot.")]
        public Color mutedPicture = new(0.55f, 0.55f, 0.6f);

        [Header("Savings bar")]
        [Tooltip("Shown while the item is yours but still too expensive.")]
        public GameObject saveBar;
        [Tooltip("Scaled along X from 0 to 1 by cash / price; its origin sits on the bar's left end.")]
        public Transform saveFill;

        [Header("Pad")]
        public Renderer pad;
        public Material affordable, expensive, locked, foreign;
        [Tooltip("Scaled in and out while the pad is affordable, to draw the eye.")]
        public Transform pulse;
        [Tooltip("How much the pad grows at the top of a pulse (0.06 = 6%).")]
        [Min(0f)] public float pulseAmount = 0.06f;
        [Tooltip("Pulse speed in radians per second.")]
        [Min(0f)] public float pulseSpeed = 6f;

        TY_ButtonLook look;

        /// <summary>The look of the last <see cref="Show"/>.</summary>
        public TY_ButtonLook Look => look;

        /// <param name="saved">Cash / price, 0..1: how far the owner has saved towards this item.</param>
        public void Show(in TY_Item item, Sprite itemPicture, TY_ButtonLook state, string lockedNote, float saved) {
            look = state;
            title.text = item.name;
            effect.text = state == TY_ButtonLook.Locked && !string.IsNullOrEmpty(lockedNote) ? lockedNote : item.Effect;
            price.text = item.price > 0 ? $"${TY_Catalog.Money(item.price)}" : "FREE";
            price.color = state switch {
                TY_ButtonLook.Affordable => affordablePrice,
                TY_ButtonLook.Expensive => expensivePrice,
                _ => mutedPrice,
            };
            if (picture != null) {
                picture.sprite = itemPicture;
                picture.color = state is TY_ButtonLook.Locked or TY_ButtonLook.Foreign ? mutedPicture : Color.white;
            }
            pad.sharedMaterial = state switch {
                TY_ButtonLook.Affordable => affordable,
                TY_ButtonLook.Expensive => expensive,
                TY_ButtonLook.Locked => locked,
                _ => foreign,
            };
            bool saving = state == TY_ButtonLook.Expensive;
            if (saveBar != null && saveBar.activeSelf != saving) saveBar.SetActive(saving);
            if (saving && saveFill != null) saveFill.localScale = new Vector3(Mathf.Clamp01(saved), 1f, 1f);
        }

        void Update() {
            if (pulse == null) return;
            float s = look == TY_ButtonLook.Affordable ? 1f + pulseAmount * Mathf.Sin(Time.time * pulseSpeed) : 1f;
            pulse.localScale = new Vector3(s, 1f, s);
        }
    }
}
