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
    /// effect and price. One per lane per plot; the client points it at the lane's next item.
    /// </summary>
    public sealed class TY_ButtonView : MonoBehaviour {
        [Header("Sign")]
        public TMP_Text title;
        public TMP_Text effect;
        public TMP_Text price;
        public SpriteRenderer picture;
        [Header("Pad")]
        public Renderer pad;
        public Material affordable, expensive, locked, foreign;
        [Tooltip("Scaled in and out while the pad is affordable, to draw the eye.")]
        public Transform pulse;

        TY_ButtonLook look;

        public void Show(in TY_Item item, Sprite itemPicture, TY_ButtonLook state, string lockedNote) {
            look = state;
            title.text = item.name;
            effect.text = state == TY_ButtonLook.Locked && !string.IsNullOrEmpty(lockedNote) ? lockedNote : item.Effect;
            price.text = item.price > 0 ? $"${TY_Catalog.Money(item.price)}" : "FREE";
            price.color = state switch {
                TY_ButtonLook.Affordable => new Color(0.45f, 1f, 0.55f),
                TY_ButtonLook.Expensive => new Color(1f, 0.55f, 0.5f),
                _ => new Color(0.8f, 0.82f, 0.86f),
            };
            if (picture != null) {
                picture.sprite = itemPicture;
                picture.color = state is TY_ButtonLook.Locked or TY_ButtonLook.Foreign ? new Color(0.55f, 0.55f, 0.6f) : Color.white;
            }
            pad.sharedMaterial = state switch {
                TY_ButtonLook.Affordable => affordable,
                TY_ButtonLook.Expensive => expensive,
                TY_ButtonLook.Locked => locked,
                _ => foreign,
            };
        }

        void Update() {
            if (pulse == null) return;
            float s = look == TY_ButtonLook.Affordable ? 1f + 0.06f * Mathf.Sin(Time.time * 6f) : 1f;
            pulse.localScale = new Vector3(s, 1f, s);
        }
    }
}
