# RyanAssets.Platformer

Reusable platformer pieces for obby-style games. Movement in this project is client-authoritative,
so pieces that change how a character moves act on the **local** character only (via
`CharacterMovement.Local`), and pieces that decide outcomes (hazards, checkpoints) are checked by the
game's server with `PlatformerQuery` against replicated positions.

| Component | What it does | Needs |
| --- | --- | --- |
| `LaunchPad` | Spring / boost panel: launches with a pad-space velocity | Trigger collider |
| `GravityZone` | Scales gravity (moon jumps, heavy falls) or turns it toward a wall or ceiling, so the character walks on it | Trigger collider; character needs `CharacterGravity` |
| `ConveyorSurface` | Pushes standing characters; keeps momentum when they step off | Solid collider |
| `WallJumpSurface` | Lets airborne characters kick off with a fresh jump press | Solid collider |
| `CrumbleBlock` | Shakes, drops away and returns after the local player stands on it | Solid collider |
| `TimedToggleBlock` | Solid for part of a cycle on the shared network clock (beat blocks) | Solid collider |
| `HazardVolume` | Marks lava/spikes; the server sends touching characters back | Any collider |
| `CheckpointMarker` | Flag presentation for a reached checkpoint | Renderers |
| `Spinner` | Visual spin/bob for collectibles | — |

Moving and spinning platforms reuse `RyanAssets.Items.Platform.NetworkPlatform` (call `Configure` on
runtime-spawned ones) with `GroundMotionTransfer` so riders are carried.

`CharacterMovement` exposes the hooks these use: `Launch`, `Gravity`, `Up`, `ExternalVelocity`,
`ISurfaceVelocity` and `IWallJumpSurface` (in `RyanAssets.Characters.Shared`). `LocalCharacter.TeleportTo`
moves a living character on every peer without a death.

## Gravity direction

`CharacterGravity` (on the character prefab) is the single source of truth for which way is down.
Movement, `GroundPhysics`, `CharacterCollisionRelay`, `CharacterAnimator` and the third-person camera
all work in that frame, so a `GravityZone` in Direction mode lets the player fall onto a wall or
ceiling and then walk and jump on it. Design notes for gravity rooms:

- Make the volume end at the surface it pulls toward; if it reaches past an opening, a player can
  fall through, leave the volume, fall back in and bounce forever.
- Leaving a volume restores default gravity, so put a landing under the exit.
- The character turns over about its middle (`CharacterMovement` > Turn Pivot Height), so the feet
  swing out about half its height while turning. Keep the volume's entry that far from the surface it
  pulls toward, or the turn clips into it.
- A character lying on a wall has much larger axis-aligned bounds than its real box. Server rules
  should test the oriented box (`PlatformerQuery.GetBox` plus the oriented `Overlap`), not
  `Collider.bounds`.
