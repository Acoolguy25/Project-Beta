using System;
using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.UI {
    /// <summary>The shared panels the top bar (and keyboard shortcuts) can open and close.</summary>
    public enum HudPanelId {
        Chat,
        PlayerList,
        GameMenu,
    }

    /// <summary>
    /// A shared panel that can be shown or hidden by id, from anywhere.
    /// <para>
    /// The top bar, chat, player list and game menu are separate prefabs. Instead of wiring them to
    /// each other through serialized references (which break whenever one prefab is rebuilt), each
    /// panel registers itself here and the others talk to it by <see cref="HudPanelId"/>.
    /// </para>
    /// Put this on an object that stays active; <see cref="content"/> is the part that fades and
    /// deactivates.
    /// </summary>
    [DisallowMultipleComponent]
    public class HudPanel : MonoBehaviour {
        [SerializeField] HudPanelId id;
        [Tooltip("The faded part of the panel. It is deactivated while hidden.")]
        [SerializeField] CanvasGroupController content;
        [SerializeField] bool visibleOnStart = true;
        [Tooltip("Reachable by id (top bar, shortcuts). Turn off for a private copy, such as the main menu's settings screen.")]
        [SerializeField] bool registerById = true;
        [Tooltip("Seconds to fade in or out. Negative uses the theme's fade duration.")]
        [SerializeField] float fadeDuration = -1f;
        [Tooltip("Pop the content in from the theme's panel pop scale when it opens.")]
        [SerializeField] bool openPop = true;

        static readonly Dictionary<HudPanelId, HudPanel> panels = new();

        /// <summary>Raised when any panel is shown or hidden: (panel, visible).</summary>
        public static event Action<HudPanelId, bool> VisibilityChanged;

        /// <summary>Raised when this panel is shown (true) or hidden (false).</summary>
        public event Action<bool> Toggled;

        public HudPanelId Id => id;
        public bool Visible => content != null && content.isVisible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            panels.Clear();
            VisibilityChanged = null;
        }

        void Awake() {
            if (registerById)
                panels[id] = this;
            if (content != null)
                content.SetVisible(visibleOnStart);
        }

        void OnDestroy() {
            if (panels.TryGetValue(id, out HudPanel registered) && registered == this)
                panels.Remove(id);
        }

        public void SetVisible(bool visible, bool instant = false) {
            if (content == null || content.isVisible == visible)
                return;
            float duration = instant ? 0f : fadeDuration >= 0f ? fadeDuration : UITheme.Active.fadeDuration;
            content.popFrom = openPop ? UITheme.Active.panelPopScale : 1f;
            content.SetVisible(visible, duration);
            Toggled?.Invoke(visible);
            if (registerById)
                VisibilityChanged?.Invoke(id, visible);
        }

        public void Toggle() => SetVisible(!Visible);

        // --- Static access by id ------------------------------------------------

        public static bool TryGet(HudPanelId id, out HudPanel panel) =>
            panels.TryGetValue(id, out panel) && panel != null;

        public static bool IsVisible(HudPanelId id) => TryGet(id, out HudPanel panel) && panel.Visible;

        public static void Show(HudPanelId id, bool instant = false) {
            if (TryGet(id, out HudPanel panel))
                panel.SetVisible(true, instant);
        }

        public static void Hide(HudPanelId id, bool instant = false) {
            if (TryGet(id, out HudPanel panel))
                panel.SetVisible(false, instant);
        }

        public static void Toggle(HudPanelId id) {
            if (TryGet(id, out HudPanel panel))
                panel.Toggle();
        }

        // Inspector-friendly hooks for buttons wired in prefabs.
        public void Show() => SetVisible(true);
        public void Hide() => SetVisible(false);
    }
}
