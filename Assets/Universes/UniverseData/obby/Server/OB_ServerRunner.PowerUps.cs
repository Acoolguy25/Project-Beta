using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using RyanAssets.Server.ServerCore;
using UnityEngine;

namespace Universes.UniverseData.obby.Server {
    // "?" blocks and their power-ups. The server judges every bump from the replicated position, holds each
    // racer's powers (OB_PowerState) and tells every client, so all of them can draw the bubble and the rainbow.
    // The owner's client applies the movement side (the extra jump, the speed) for the time the server says.
    public sealed partial class OB_ServerRunner {
        /// <summary>Each stage's "?" block (index = stage; a piece index, or -1 for none).</summary>
        readonly List<int> powerBlocks = new();

        OB_PowerUpRules PowerRules => course != null && course.rules?.powerUps != null ? course.rules.powerUps : new OB_PowerUpRules();

        /// <summary>Finds each stage's "?" block on a freshly built course (called from <see cref="IndexCourse"/>).</summary>
        void IndexPowerBlocks() {
            powerBlocks.Clear();
            for (int s = 0; s < layout.StageCount; s++) powerBlocks.Add(-1);
            for (int i = 0; i < layout.pieces.Count; i++) {
                OB_Piece piece = layout.pieces[i];
                if (piece.kind == OB_PieceKind.PowerBlock && piece.stage < powerBlocks.Count) powerBlocks[piece.stage] = i;
            }
        }

        /// <summary>
        /// Bumps the block of the stage the racer stands on (their own checkpoint's pad, or any reached stage they are
        /// over: a racer sent back can still use a block they skipped). Each block works once per racer per race.
        /// </summary>
        void CheckPowerBlocks(NetworkConnection connection, Runner runner, Bounds body) {
            if (powerBlocks.Count == 0) return;
            OB_PowerUpRules rules = PowerRules;
            if (!runner.power.HeadRose(body.max.y, rules.bumpMaxRise)) return;
            int s = layout.StageAt(runner.checkpoint, body.center);
            if (s < 0 || s >= powerBlocks.Count || powerBlocks[s] < 0 || runner.power.HasUsed(s)) return;
            OB_Piece block = layout.pieces[powerBlocks[s]];
            if (!rules.IsBump(body, block.position, block.yaw)) return;
            var power = (OB_PowerUp)block.group;
            if (!runner.power.TryUse(s, power, Time.time, rules)) return;
            retention.Count(connection, OB_Retention.PowerUpsStat);
            PlayerProgress.Modify<OB_SaveData>(connection, save => save.RecordPowerUp(power));
            BroadcastPower(connection, runner, OB_PowerEvent.Got, power, s);
        }

        void ShieldPopped(NetworkConnection connection, Runner runner) {
            retention.Count(connection, OB_Retention.ShieldSavesStat);
            PlayerProgress.Modify<OB_SaveData>(connection, save => save.shieldSaves++);
            BroadcastPower(connection, runner, OB_PowerEvent.ShieldPopped, OB_PowerUp.None, -1);
        }

        OB_PowerBroadcast PowerMessage(NetworkConnection connection, Runner runner, OB_PowerEvent evt, OB_PowerUp gained, int stage) {
            float now = Time.time;
            return new OB_PowerBroadcast {
                seed = layout?.seed ?? 0, clientId = connection.ClientId, evt = evt, gained = gained, stage = stage,
                shield = runner.power.shield, bootsLeft = runner.power.BootsLeft(now), rushLeft = runner.power.RushLeft(now),
                usedBlocks = runner.power.usedBlocks
            };
        }

        /// <summary>Tells everyone about one racer's power-ups.</summary>
        void BroadcastPower(NetworkConnection connection, Runner runner, OB_PowerEvent evt, OB_PowerUp gained, int stage) {
            if (layout == null) return;
            InstanceFinder.ServerManager.Broadcast(PowerMessage(connection, runner, evt, gained, stage));
        }

        /// <summary>Tells everyone every racer's power-ups (a new race clears them all).</summary>
        void BroadcastPowerStates() {
            foreach (var pair in runners)
                if (pair.Key.IsActive) BroadcastPower(pair.Key, pair.Value, OB_PowerEvent.Sync, OB_PowerUp.None, -1);
        }

        /// <summary>Tells one (joining) client every racer's power-ups.</summary>
        void SendPowerStates(NetworkConnection target) {
            if (layout == null || target == null || !target.IsActive) return;
            foreach (var pair in runners)
                if (pair.Key.IsActive)
                    InstanceFinder.ServerManager.Broadcast(target, PowerMessage(pair.Key, pair.Value, OB_PowerEvent.Sync, OB_PowerUp.None, -1));
        }
    }
}
