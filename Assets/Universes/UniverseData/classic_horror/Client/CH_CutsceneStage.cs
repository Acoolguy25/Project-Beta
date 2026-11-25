using System.Collections.Generic;
using RyanAssets.Characters.Shared;
using RyanAssets.UI;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// Clears the stage while a cutscene plays: every networked character in the scene (each investigator with their
    /// tool and its light, the local one included, and the real Father Crane wherever the server froze him) is switched
    /// off so only the puppets act, and the shared player list and chat fade out of the letterbox. Afterwards exactly
    /// what was switched off comes back; anything off for another reason (a teammate in a wardrobe) stays off. The panels
    /// are faded through their own canvas group rather than closed, so nothing reads it as the player opening the chat.
    /// </summary>
    public sealed class CH_CutsceneStage {
        static readonly HudPanelId[] SharedPanels = { HudPanelId.PlayerList, HudPanelId.Chat };

        readonly HashSet<Renderer> renderersOff = new();
        readonly HashSet<Light> lightsOff = new();
        readonly List<(CanvasGroup group, float alpha, bool blocks)> panelsFaded = new();
        readonly List<Renderer> renderers = new();
        readonly List<Light> lights = new();

        public bool Active { get; private set; }

        /// <summary>
        /// Clears the stage. Later calls also catch anyone who spawned since (a late joiner, a respawn) and anything that
        /// switched itself on (a spawn-protection glow, a tool drawn mid-scene).
        /// </summary>
        public void Clear() {
            if (!Active) {
                Active = true;
                foreach (HudPanelId id in SharedPanels) {
                    if (!HudPanel.TryGet(id, out HudPanel panel)) continue;
                    if (!panel.TryGetComponent(out CanvasGroup group)) group = panel.gameObject.AddComponent<CanvasGroup>();
                    panelsFaded.Add((group, group.alpha, group.blocksRaycasts));
                    group.alpha = 0f;
                    group.blocksRaycasts = false;
                }
            }
            foreach (GameCharacter character in Object.FindObjectsByType<GameCharacter>(FindObjectsSortMode.None)) {
                character.GetComponentsInChildren(true, renderers);
                foreach (Renderer part in renderers)
                    if (part.enabled) { part.enabled = false; renderersOff.Add(part); }
                character.GetComponentsInChildren(true, lights);
                foreach (Light light in lights)
                    if (light.enabled) { light.enabled = false; lightsOff.Add(light); }
            }
        }

        /// <summary>Puts back what <see cref="Clear"/> took off the stage.</summary>
        public void Restore() {
            if (!Active) return;
            Active = false;
            foreach (Renderer part in renderersOff) if (part != null) part.enabled = true;
            foreach (Light light in lightsOff) if (light != null) light.enabled = true;
            foreach (var (group, alpha, blocks) in panelsFaded)
                if (group != null) { group.alpha = alpha; group.blocksRaycasts = blocks; }
            renderersOff.Clear();
            lightsOff.Clear();
            panelsFaded.Clear();
        }
    }
}
