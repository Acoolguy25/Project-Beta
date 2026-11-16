using System;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>Tuning for <see cref="WV_OrderFeedback"/>, serialized on the client controller.</summary>
    [Serializable]
    public sealed class WV_OrderFeedbackTuning {
        [Tooltip("Ground ping for a plain move.")]
        public Color moveColor = new(0.45f, 0.85f, 1f, 1f);
        [Tooltip("Ground ping for an advance (attack-move).")]
        public Color advanceColor = new(1f, 0.78f, 0.3f, 1f);
        [Tooltip("Ping on an attacked target.")]
        public Color attackColor = new(1f, 0.32f, 0.26f, 1f);
        [Tooltip("Ping for a production building's new rally point.")]
        public Color rallyColor = new(0.55f, 1f, 0.6f, 1f);
        [Tooltip("Size of the ground ping ring; big enough to read from the commander's camera 40 m up.")]
        [Min(0.1f)] public float pingScale = 7f;
        [Tooltip("Size of the second, inner ring, as a share of the outer one.")]
        [Range(0.1f, 1f)] public float innerPing = 0.45f;
        [Tooltip("Volume of the acknowledgement blip.")]
        [Range(0f, 1f)] public float acknowledgeVolume = 0.8f;
        [Tooltip("How much each selection ring pulses when its unit takes an order.")]
        [Range(0f, 1f)] public float ringPulse = 0.3f;
        [Min(0.05f)] public float ringPulseSeconds = 0.3f;
    }

    /// <summary>
    /// Tells the commander their order was heard: a coloured ring pinged on the ground or the target,
    /// a short acknowledgement blip pitched by the kind of order, and a pulse through the selection
    /// rings of everything that took it. Client-only; the order itself is still validated by the
    /// server.
    /// </summary>
    public sealed class WV_OrderFeedback {
        /// <summary>A rally point's blip sits above every order's, so the two never sound alike.</summary>
        const float RallyPitchSemitones = 5f;

        readonly WV_OrderFeedbackTuning tuning;
        readonly WV_CommanderSelection selection;

        public WV_OrderFeedback(WV_OrderFeedbackTuning tuning, WV_CommanderSelection selection) {
            this.tuning = tuning ?? new WV_OrderFeedbackTuning();
            this.selection = selection;
        }

        /// <summary>An order was sent for the current selection; <paramref name="point"/> is its destination or target.</summary>
        public void Ordered(WV_OrderType orderType, Vector3? point) {
            if (point.HasValue)
                Ping(point.Value, GetColor(orderType));
            GameSfx.Play(SfxCue.UiClick, tuning.acknowledgeVolume, GetPitch(orderType));
            PulseRings();
        }

        /// <summary>Production buildings were given a new gather point.</summary>
        public void RallySet(Vector3 point) {
            Ping(point, tuning.rallyColor);
            GameSfx.Play(SfxCue.UiClick, tuning.acknowledgeVolume, GameSfx.Semitones(RallyPitchSemitones));
        }

        void Ping(Vector3 point, Color color) {
            GameFx.Play(FxKind.ShockwaveRing, point, color, tuning.pingScale);
            GameFx.Play(FxKind.ShockwaveRing, point, color, tuning.pingScale * tuning.innerPing);
        }

        void PulseRings() {
            if (selection == null)
                return;
            foreach (WV_Unit unit in selection.Units) {
                if (unit != null && unit.SelectionIndicator != null)
                    Punch.Scale(unit.SelectionIndicator.transform, tuning.ringPulse, tuning.ringPulseSeconds);
            }
            foreach (GameCharacter troop in selection.Troops) {
                if (selection.TryGetIndicator(troop, out GameObject ring))
                    Punch.Scale(ring.transform, tuning.ringPulse, tuning.ringPulseSeconds);
            }
        }

        Color GetColor(WV_OrderType orderType) => orderType switch {
            WV_OrderType.Attack => tuning.attackColor,
            WV_OrderType.AttackMove => tuning.advanceColor,
            _ => tuning.moveColor
        };

        /// <summary>Attack reads lower and firmer, a move brighter, a halt in between.</summary>
        static float GetPitch(WV_OrderType orderType) => orderType switch {
            WV_OrderType.Attack => GameSfx.Semitones(-4f),
            WV_OrderType.AttackMove => GameSfx.Semitones(-1f),
            WV_OrderType.Move => GameSfx.Semitones(3f),
            _ => 1f
        };
    }
}
