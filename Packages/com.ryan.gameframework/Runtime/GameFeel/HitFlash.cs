using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Flashes a set of renderers towards a colour (white by default, overbright so textured
    /// materials visibly light up) and back, through MaterialPropertyBlocks: no material copies, and colours other scripts set through their own
    /// blocks are respected. Put it on a prefab (renderers are gathered from children when the list
    /// is empty) or let <see cref="On"/> add it.
    /// <code>
    /// GetComponent&lt;HitFlash&gt;().Flash();
    /// HitFlash.On(enemy, Color.red, 0.15f);   // finds or adds the component on enemy's root
    /// </code>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitFlash : MonoBehaviour {
        [Tooltip("Renderers to flash. Empty = every renderer under this object (particles and UI excluded).")]
        [SerializeField] Renderer[] renderers;
        [Tooltip("Colour the materials are pushed towards at the peak.")]
        [SerializeField] Color flashColor = Color.white;
        [Tooltip("How far towards the flash colour the peak goes (1 = fully).")]
        [SerializeField, Range(0f, 1f)] float peak = 0.85f;
        [Tooltip("HDR multiplier on the flash colour. The base colour multiplies the texture, so most textured materials (white base colour) only brighten above 1.")]
        [SerializeField, Range(1f, 8f)] float brightness = 3f;
        [Tooltip("Seconds from the peak back to normal.")]
        [SerializeField, Min(0.01f)] float duration = 0.14f;
        [Tooltip("Colour properties flashed on every material that has them. Recolouring shaders that paint over the base colour (the robot's _RobotPrimaryColor) need theirs listed too.")]
        [SerializeField] string[] colorProperties = { "_BaseColor", "_Color", "_RobotPrimaryColor" };

        struct Slot {
            public Renderer renderer;
            public int materialIndex;
            public int propertyId;
            public Color original;
            public bool blockWasEmpty;
        }

        Slot[] slots;
        MaterialPropertyBlock block;
        Color activeColor;
        float activePeak;
        float activeDuration;
        float age;
        bool flashing;

        /// <summary>Finds a HitFlash on <paramref name="target"/> or its parents, or adds one to the target's root, and flashes it.</summary>
        public static HitFlash On(Component target, Color? color = null, float seconds = -1f) {
            if (target == null || !Application.isPlaying)
                return null;
            HitFlash flash = target.GetComponentInParent<HitFlash>();
            if (flash == null)
                flash = target.transform.root.gameObject.AddComponent<HitFlash>();
            flash.Flash(color ?? flash.flashColor, seconds > 0f ? seconds : flash.duration);
            return flash;
        }

        /// <summary>Flashes with the component's own colour and duration.</summary>
        public void Flash() => Flash(flashColor, duration);

        /// <summary>Flashes towards <paramref name="color"/>, fading back over <paramref name="seconds"/>.</summary>
        public void Flash(Color color, float seconds) {
            if (!Application.isPlaying)
                return;
            if (slots == null)
                Collect();
            if (!flashing)
                Capture();
            activeColor = color;
            activePeak = peak;
            activeDuration = Mathf.Max(0.01f, seconds);
            age = 0f;
            flashing = true;
            enabled = true;
            Apply(1f);
        }

        void Awake() {
            enabled = false;
        }

        void Collect() {
            block = new MaterialPropertyBlock();
            if (renderers == null || renderers.Length == 0) {
                renderers = GetComponentsInChildren<Renderer>(true);
            }
            var ids = new int[colorProperties.Length];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = Shader.PropertyToID(colorProperties[i]);
            // _BaseColor and _Color are alternatives (the first found wins); any other listed property is
            // flashed as well, since shaders that recolour paint over the base colour.
            var list = new System.Collections.Generic.List<Slot>();
            foreach (Renderer r in renderers) {
                if (!Usable(r))
                    continue;
                Material[] materials = r.sharedMaterials;
                for (int m = 0; m < materials.Length; m++) {
                    Material material = materials[m];
                    if (material == null)
                        continue;
                    bool baseFound = false;
                    for (int i = 0; i < ids.Length; i++) {
                        bool isBase = colorProperties[i] == "_BaseColor" || colorProperties[i] == "_Color";
                        if ((isBase && baseFound) || !material.HasProperty(ids[i]))
                            continue;
                        baseFound |= isBase;
                        list.Add(new Slot { renderer = r, materialIndex = m, propertyId = ids[i] });
                    }
                }
            }
            slots = list.ToArray();
        }

        static bool Usable(Renderer r) => r != null && r is not ParticleSystemRenderer && r is not TrailRenderer && r is not LineRenderer;

        /// <summary>Remembers each slot's colour as it is right now (block value first, else material).</summary>
        void Capture() {
            for (int i = 0; i < slots.Length; i++) {
                ref Slot slot = ref slots[i];
                if (slot.propertyId < 0 || slot.renderer == null)
                    continue;
                slot.renderer.GetPropertyBlock(block, slot.materialIndex);
                slot.blockWasEmpty = block.isEmpty;
                if (!block.isEmpty && block.HasColor(slot.propertyId)) {
                    slot.original = block.GetColor(slot.propertyId);
                } else {
                    Material material = slot.renderer.sharedMaterials[slot.materialIndex];
                    slot.original = material != null ? material.GetColor(slot.propertyId) : Color.white;
                }
            }
        }

        void Apply(float strength) {
            float k = strength * activePeak;
            for (int i = 0; i < slots.Length; i++) {
                Slot slot = slots[i];
                if (slot.propertyId < 0 || slot.renderer == null)
                    continue;
                slot.renderer.GetPropertyBlock(block, slot.materialIndex);
                Color target = activeColor * brightness;
                target.a = slot.original.a;
                block.SetColor(slot.propertyId, Color.LerpUnclamped(slot.original, target, k));
                slot.renderer.SetPropertyBlock(block, slot.materialIndex);
            }
        }

        void Restore() {
            if (slots == null)
                return;
            for (int i = 0; i < slots.Length; i++) {
                Slot slot = slots[i];
                if (slot.propertyId < 0 || slot.renderer == null)
                    continue;
                if (slot.blockWasEmpty) {
                    // Nothing else used a block here: drop ours so material edits show through again.
                    slot.renderer.SetPropertyBlock(null, slot.materialIndex);
                } else {
                    slot.renderer.GetPropertyBlock(block, slot.materialIndex);
                    block.SetColor(slot.propertyId, slot.original);
                    slot.renderer.SetPropertyBlock(block, slot.materialIndex);
                }
            }
        }

        void Update() {
            age += Time.unscaledDeltaTime;
            float t = age / activeDuration;
            if (t >= 1f) {
                Restore();
                flashing = false;
                enabled = false;
                return;
            }
            float u = 1f - t;
            Apply(u * u);
        }

        void OnDisable() {
            if (flashing) {
                Restore();
                flashing = false;
            }
        }
    }
}
