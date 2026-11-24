using System.Collections.Generic;
using RyanAssets.Characters.Shared;
using RyanAssets.UI;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// Clears the stage while a cutscene plays: every networked character in the scene (each investigator with their
    /// tool and its light, the local one included, and the real Father Crane wherever the server froze him) is switched
    /// off so only the puppets act, and the shared player list and chat step out of the letterbox. Afterwards exactly
    /// what was switched off comes back; anything off for another reason (a teammate in a wardrobe) stays off.
    /// </summary>
    public sealed class CH_CutsceneStage {
        static readonly HudPanelId[] SharedPanels = { HudPanelId.PlayerList, HudPanelId.Chat };

        readonly HashSet<Renderer> renderersOff = new();
        readonly HashSet<Light> lightsOff = new();
        readonly List<HudPanelId> panelsOff = new();

        public bool Active { get; private set; }

        /// <summary>
        /// Clears the stage. Later calls also catch anyone who spawned since (a late joiner, a respawn) and anything that
        /// switched itself on (a spawn-protection glow, a tool drawn mid-scene).
        /// </summary>
        public void Clear() {
            if (!Active) {
                Active = true;
                foreach (HudPanelId panel in SharedPanels)
                    if (HudPanel.IsVisible(panel)) { HudPanel.Hide(panel, true); panelsOff.Add(panel); }
            }
            foreach (GameCharacter character in Object.FindObjectsByType<GameCharacter>(FindObjectsSortMode.None)) {
                foreach (Renderer part in character.GetComponentsInChildren<Renderer>(true))
                    if (part.enabled) { part.enabled = false; renderersOff.Add(part); }
                foreach (Light light in character.GetComponentsInChildren<Light>(true))
                    if (light.enabled) { light.enabled = false; lightsOff.Add(light); }
            }
        }

        /// <summary>Puts back what <see cref="Clear"/> took off the stage.</summary>
        public void Restore() {
            if (!Active) return;
            Active = false;
            foreach (Renderer part in renderersOff) if (part != null) part.enabled = true;
            foreach (Light light in lightsOff) if (light != null) light.enabled = true;
            foreach (HudPanelId panel in panelsOff) HudPanel.Show(panel, true);
            renderersOff.Clear();
            lightsOff.Clear();
            panelsOff.Clear();
        }
    }
}
