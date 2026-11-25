using RyanAssets.Characters.Client;
using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tycoon.Client {
    /// <summary>
    /// Shows what a buy pad would build: while the local player stands near one of their own pads, a see-through
    /// copy of its item hovers on the spot where it will go, tinted in the lane's colour. Without it a pad is
    /// only a name and a price; with it the player sees the Office appear on the back lot before paying.
    /// </summary>
    public sealed class TY_BuildPreview : MonoBehaviour {
        [Tooltip("How close (metres, on the ground plane) the player must be to a pad to see its preview.")]
        [SerializeField, Min(1f)] float showDistance = 7f;
        [Tooltip("Brightness swing of the preview's pulse, as a fraction of its alpha.")]
        [SerializeField, Range(0f, 0.5f)] float pulseDepth = 0.25f;
        [SerializeField, Min(0.1f)] float pulseSpeed = 2.4f;

        sealed class Slot {
            public int item = -1;
            public Transform pad;
            public GameObject ghost;
            public Material material;
        }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        Material template;
        Slot[] slots;

        public void Setup(Material ghostTemplate, int lanes) {
            template = ghostTemplate;
            slots = new Slot[lanes];
            for (int i = 0; i < lanes; i++) slots[i] = new Slot();
        }

        /// <summary>
        /// Points lane <paramref name="lane"/>'s preview at <paramref name="item"/> (-1 for none) on the plot
        /// whose item root is <paramref name="itemRoot"/>, with <paramref name="pad"/> as the thing the player walks to.
        /// </summary>
        public void Set(int lane, int item, GameObject prefab, Transform itemRoot, Transform pad, Color tint) {
            if (slots == null) return;
            Slot slot = slots[lane];
            if (slot.item == item && slot.pad == pad && (item < 0 || slot.ghost != null)) return;
            Clear(slot);
            slot.item = item;
            slot.pad = pad;
            if (item < 0 || prefab == null || template == null) return;

            TY_Item data = TY_Catalog.Items[item];
            slot.ghost = Instantiate(prefab, itemRoot);
            slot.ghost.name = $"Preview {data.name}";
            slot.ghost.transform.localPosition = data.itemPosition;
            slot.ghost.transform.localRotation = Quaternion.Euler(0f, data.itemYaw, 0f);
            slot.material = new Material(template);
            slot.material.SetColor(BaseColor, new Color(tint.r, tint.g, tint.b, 0.4f));
            slot.material.SetColor(EmissionColor, tint * 0.5f);
            Hologram(slot.ghost, slot.material);
            slot.ghost.SetActive(false);
        }

        /// <summary>Drops lane <paramref name="lane"/>'s preview if it belongs to <paramref name="pad"/> (the plot stopped being the player's).</summary>
        public void Release(int lane, Transform pad) {
            if (slots != null && slots[lane].pad == pad) Clear(slots[lane]);
        }

        public void Clear() {
            if (slots != null) foreach (Slot slot in slots) Clear(slot);
        }

        void Update() {
            if (slots == null) return;
            Transform player = LocalPlayer.Character != null ? LocalPlayer.Character.transform : null;
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseDepth;
            foreach (Slot slot in slots) {
                if (slot.ghost == null) continue;
                bool near = player != null && slot.pad != null && Flat(player.position - slot.pad.position).sqrMagnitude < showDistance * showDistance;
                if (slot.ghost.activeSelf != near) slot.ghost.SetActive(near);
                if (!near) continue;
                Color color = slot.material.GetColor(BaseColor);
                color.a = 0.4f * pulse;
                slot.material.SetColor(BaseColor, color);
            }
        }

        void OnDestroy() => Clear();

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        static void Clear(Slot slot) {
            if (slot.ghost != null) Destroy(slot.ghost);
            if (slot.material != null) Destroy(slot.material);
            slot.ghost = null;
            slot.material = null;
            slot.item = -1;
        }

        /// <summary>Turns a built item into a see-through copy: one material, no lights, signs, labels or shadows.</summary>
        static void Hologram(GameObject ghost, Material material) {
            foreach (Light light in ghost.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (TMP_Text label in ghost.GetComponentsInChildren<TMP_Text>(true)) label.gameObject.SetActive(false);
            foreach (SpriteRenderer sign in ghost.GetComponentsInChildren<SpriteRenderer>(true)) sign.gameObject.SetActive(false);
            foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true)) {
                if (renderer is ParticleSystemRenderer) { renderer.enabled = false; continue; }
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
        }
    }
}
