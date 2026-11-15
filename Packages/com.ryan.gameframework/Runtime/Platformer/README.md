# RyanAssets.Platformer

Reusable platformer pieces for obby-style games. Movement in this project is client-authoritative,
so pieces that change how a character moves act on the **local** character only (via
`CharacterMovement.Local`), and pieces that decide outcomes (hazards, checkpoints) are checked by the
game's server with `PlatformerQuery` against replicated positions.

| Component | What it does | Needs |
| --- | --- | --- |
| `LaunchPad` | Spring / boost panel: launches with a pad-space velocity | Trigger collider |
| `GravityZone` | Scales gravity while inside (moon jumps or heavy falls) | Trigger collider |
| `ConveyorSurface` | Pushes standing characters; keeps momentum when they step off | Solid collider |
| `WallJumpSurface` | Lets airborne characters kick off with a fresh jump press | Solid collider |
| `CrumbleBlock` | Shakes, drops away and returns after the local player stands on it | Solid collider |
| `TimedToggleBlock` | Solid for part of a cycle on the shared network clock (beat blocks) | Solid collider |
| `HazardVolume` | Marks lava/spikes; the server sends touching characters back | Any collider |
| `CheckpointMarker` | Flag presentation for a reached checkpoint | Renderers |
| `Spinner` | Visual spin/bob for collectibles | — |

Moving and spinning platforms reuse `RyanAssets.Items.Platform.NetworkPlatform` (call `Configure` on
runtime-spawned ones) with `GroundMotionTransfer` so riders are carried.

`CharacterMovement` exposes the hooks these use: `Launch`, `SetGravityScale`, `ExternalVelocity`,
`ISurfaceVelocity` and `IWallJumpSurface` (in `RyanAssets.Characters.Shared`). `LocalCharacter.TeleportTo`
moves a living character on every peer without a death.
