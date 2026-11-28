using UnityEngine;

namespace Universes.UniverseData.obby {
    // "?" blocks: one floats over every few checkpoint pads, beside the respawn point, high enough that only a
    // jump bumps it. They use their own random stream, so adding or tuning them never changes the stages.
    public sealed partial class OB_CourseGenerator {
        /// <summary>Mixed into the seed for the power-up stream, so it is independent of the stage stream.</summary>
        const int PowerUpSalt = 0x3F0B10C;

        void PlacePowerBlocks() {
            OB_PowerUpRules p = rules.powerUps;
            if (p == null || !p.enabled || layout.StageCount < 2) return;
            var stream = new System.Random(unchecked(layout.seed * 31 + PowerUpSalt));
            float Roll() => stream.Next(1 << 24) / (float)(1 << 24);

            // The pad of each stage (stage 0 is the lobby and has none).
            var pads = new int[layout.StageCount];
            for (int i = 0; i < pads.Length; i++) pads[i] = -1;
            for (int i = 0; i < layout.pieces.Count; i++)
                if (layout.pieces[i].kind == OB_PieceKind.CheckpointPad && layout.pieces[i].stage < pads.Length && pads[layout.pieces[i].stage] < 0)
                    pads[layout.pieces[i].stage] = i;

            float height = p.BlockCentreHeight(m);
            for (int s = Mathf.Max(1, p.firstStage); s < layout.StageCount; s += stream.Next(p.spacing.x, p.spacing.y + 1)) {
                if (pads[s] < 0) continue;
                OB_Piece pad = layout.pieces[pads[s]];
                Quaternion turn = Quaternion.Euler(0f, pad.yaw, 0f);
                Vector3 padTop = pad.position + Vector3.up * (pad.size.y * 0.5f);
                float side = Roll() < 0.5f ? -1f : 1f;
                Vector3 centre = padTop + turn * new Vector3(side * p.sideOffset, height, 0f);
                OB_Stage info = layout.stages[s];
                OB_PowerUp power = p.Pick(info.tier, Roll());
                layout.pieces.Add(new OB_Piece {
                    kind = OB_PieceKind.PowerBlock, stage = s, segment = -1, position = centre, yaw = pad.yaw,
                    size = Vector3.one * p.blockSize, group = (int)power
                });
                // The block is part of its stage: its region (and the fall-up limits drawn from it) reach over it.
                info.region.Encapsulate(new Bounds(centre, Vector3.one * p.blockSize));
                layout.ceilingY = Mathf.Max(layout.ceilingY, info.region.max.y + KillDepth);
                if (!float.IsPositiveInfinity(info.killTop)) info.killTop = Mathf.Max(info.killTop, info.region.max.y + KillDepth);
            }
        }
    }
}
