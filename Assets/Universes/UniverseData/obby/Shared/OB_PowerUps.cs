using System;
using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// What a "?" block gives when bumped from below. Values go over the network and into piece data
    /// (<see cref="OB_Piece.group"/> of a <see cref="OB_PieceKind.PowerBlock"/>): keep new kinds at the end.
    /// </summary>
    public enum OB_PowerUp : byte {
        None,
        /// <summary>Bubble Shield: the next hazard that touches you pops the bubble instead of sending you back.</summary>
        Shield,
        /// <summary>Cloud Boots: one extra jump in mid-air, for a while.</summary>
        Boots,
        /// <summary>Rainbow Rush: a short burst of speed during which hazards cannot touch you.</summary>
        Rush
    }

    /// <summary>What happened to a racer's power-ups (see <see cref="OB_PowerBroadcast"/>).</summary>
    public enum OB_PowerEvent : byte { Sync, Got, ShieldPopped }

    /// <summary>
    /// Where "?" blocks go and what their power-ups do. Part of <see cref="OB_CourseRules"/>, so the
    /// server and every client place the same blocks (and the rules hash changes with them).
    /// </summary>
    [Serializable]
    public sealed class OB_PowerUpRules {
        [Tooltip("Place \"?\" blocks at all.")]
        public bool enabled = true;
        [Tooltip("First stage whose checkpoint pad can hold a block (stage 0 is the lobby).")]
        [Min(1)] public int firstStage = 2;
        [Tooltip("Stages between two blocks (inclusive range), so blocks come every few checkpoints.")]
        public Vector2Int spacing = new(2, 4);
        [Tooltip("Relative chance of each power-up: shield, boots, rush.")]
        public Vector3 weights = new(0.45f, 0.35f, 0.2f);
        [Tooltip("Rainbow Rush (hazard-proof) only appears from this tier on, where the hazards are worth skipping.")]
        public OB_Tier rushFromTier = OB_Tier.Medium;
        [Tooltip("Side length of the block, metres.")]
        [Min(0.5f)] public float blockSize = 2.4f;
        [Tooltip("How far beside the pad's centre (the respawn point) the block floats, metres.")]
        [Min(0f)] public float sideOffset = 1.5f;
        [Tooltip("Height of the block's underside above standing head height, as a share of the jump height: a jump bumps it, standing never does.")]
        [Range(0.3f, 0.95f)] public float bumpReach = 0.7f;
        [Tooltip("How far below the block's underside a head still counts as a bump (server tolerance for replicated positions), metres.")]
        [Min(0.1f)] public float bumpDepth = 0.9f;
        [Tooltip("Most a head may rise between two server samples and still count as jumping into a block (more is a teleport, such as a respawn above the pad).")]
        [Min(0.1f)] public float bumpMaxRise = 1f;
        [Tooltip("Seconds of hazard immunity after the bubble pops, so the hazard that popped it cannot send you back at once.")]
        [Min(0f)] public float shieldGraceSeconds = 1.5f;
        [Tooltip("Seconds Cloud Boots last.")]
        [Min(1f)] public float bootsSeconds = 15f;
        [Tooltip("Seconds Rainbow Rush lasts.")]
        [Min(1f)] public float rushSeconds = 6f;
        [Tooltip("Walking (and dashing) speed multiplier during Rainbow Rush.")]
        [Range(1f, 1.6f)] public float rushSpeed = 1.3f;

        public void Validate() {
            if (spacing.x < 1 || spacing.y < spacing.x) throw new ArgumentException("Power-up spacing must be a range starting at 1 or more.");
            if (weights.x < 0f || weights.y < 0f || weights.z < 0f || weights.x + weights.y + weights.z <= 0f)
                throw new ArgumentException("Power-up weights must be non-negative and not all zero.");
        }

        /// <summary>Picks a power-up for a block on a stage of <paramref name="tier"/> from a 0..1 roll.</summary>
        public OB_PowerUp Pick(OB_Tier tier, float roll) {
            float rush = tier >= rushFromTier ? weights.z : 0f;
            float total = weights.x + weights.y + rush;
            if (total <= 0f) return OB_PowerUp.Shield;
            float r = Mathf.Clamp01(roll) * total;
            if (r < weights.x) return OB_PowerUp.Shield;
            if (r < weights.x + weights.y || rush <= 0f) return OB_PowerUp.Boots;
            return OB_PowerUp.Rush;
        }

        /// <summary>Height of a block's centre above the pad top it floats over.</summary>
        public float BlockCentreHeight(in OB_Movement movement) => movement.bodyHeight + movement.jumpHeight * bumpReach + blockSize * 0.5f;

        /// <summary>
        /// Whether a body box (world bounds, upright gravity) is bumping the block centred at <paramref name="centre"/>
        /// turned by <paramref name="yaw"/>: the head is just under the block's underside and the body's middle is under its footprint.
        /// </summary>
        public bool IsBump(Bounds body, Vector3 centre, float yaw) {
            float half = blockSize * 0.5f;
            float bottom = centre.y - half;
            if (body.max.y < bottom - bumpDepth || body.max.y > bottom + half) return false;
            Vector3 local = Quaternion.Euler(0f, -yaw, 0f) * (body.center - centre);
            float reach = half + Mathf.Min(body.extents.x, body.extents.z) * 0.5f;
            return Mathf.Abs(local.x) <= reach && Mathf.Abs(local.z) <= reach;
        }
    }

    /// <summary>
    /// One racer's power-ups on the server (pure, so tests can drive it): the shield held, when the timed
    /// powers run out, and which stages' blocks they already bumped (one block per stage, once per race).
    /// </summary>
    public sealed class OB_PowerState {
        public bool shield;
        public float bootsUntil, rushUntil, graceUntil;
        /// <summary>Stages whose "?" block this racer has used, one bit per stage.</summary>
        public ulong usedBlocks;
        /// <summary>Head height at the last server sample (negative infinity: none since the last teleport).</summary>
        public float lastHeadY = float.NegativeInfinity;

        public void Clear() {
            shield = false;
            bootsUntil = rushUntil = graceUntil = 0f;
            usedBlocks = 0UL;
            lastHeadY = float.NegativeInfinity;
        }

        /// <summary>
        /// Records the head height and returns whether it rose since the last sample by a jump's step (not more than
        /// <paramref name="maxRise"/>): only a head moving up bumps a block, never one dropped next to it by a respawn.
        /// </summary>
        public bool HeadRose(float headY, float maxRise) {
            float rise = headY - lastHeadY;
            lastHeadY = headY;
            return rise > 0.01f && rise <= maxRise;
        }

        /// <summary>Forgets the last head height (call on a teleport, so the jump to the new place is not a rise).</summary>
        public void ForgetHead() => lastHeadY = float.NegativeInfinity;

        public bool HasUsed(int stage) => stage is >= 0 and < 64 && (usedBlocks & (1UL << stage)) != 0;

        /// <summary>Uses the block of <paramref name="stage"/> and grants its power. False when it was used already.</summary>
        public bool TryUse(int stage, OB_PowerUp power, float now, OB_PowerUpRules rules) {
            if (stage is < 0 or >= 64 || HasUsed(stage)) return false;
            usedBlocks |= 1UL << stage;
            Grant(power, now, rules);
            return true;
        }

        public void Grant(OB_PowerUp power, float now, OB_PowerUpRules rules) {
            switch (power) {
                case OB_PowerUp.Shield: shield = true; break;
                case OB_PowerUp.Boots: bootsUntil = now + rules.bootsSeconds; break;
                case OB_PowerUp.Rush: rushUntil = now + rules.rushSeconds; break;
            }
        }

        /// <summary>Hazards cannot touch this racer right now (Rainbow Rush, or the moment after a bubble popped).</summary>
        public bool Protects(float now) => now < rushUntil || now < graceUntil;

        /// <summary>Pops the shield against a hazard. False when there is no shield to pop.</summary>
        public bool TryAbsorb(float now, OB_PowerUpRules rules) {
            if (!shield) return false;
            shield = false;
            graceUntil = now + rules.shieldGraceSeconds;
            return true;
        }

        public float BootsLeft(float now) => Mathf.Max(0f, bootsUntil - now);
        public float RushLeft(float now) => Mathf.Max(0f, rushUntil - now);
    }

    /// <summary>Display names, hints and colours for power-ups (shared so the server's chat and the client's HUD agree).</summary>
    public static class OB_PowerUpInfo {
        public static string Name(OB_PowerUp power) => power switch {
            OB_PowerUp.Shield => "Bubble Shield",
            OB_PowerUp.Boots => "Cloud Boots",
            OB_PowerUp.Rush => "Rainbow Rush",
            _ => ""
        };

        public static string Hint(OB_PowerUp power) => power switch {
            OB_PowerUp.Shield => "Blocks the next hazard",
            OB_PowerUp.Boots => "Jump again in mid-air",
            OB_PowerUp.Rush => "Faster, and hazards can't touch you",
            _ => ""
        };

        public static Color Colour(OB_PowerUp power) => power switch {
            OB_PowerUp.Shield => OB_Palette.Gravity,
            OB_PowerUp.Boots => OB_Palette.Cloud,
            OB_PowerUp.Rush => OB_Palette.MushroomCap,
            _ => OB_Palette.Paper
        };
    }
}
