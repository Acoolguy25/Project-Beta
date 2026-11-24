# RyanAssets.Platformer

Reusable platformer pieces for obby-style games. Movement in this project is client-authoritative,
so pieces that change how a character moves act on the **local** character only (via
`CharacterMovement.Local`), and pieces that decide outcomes (hazards, checkpoints) are checked by the
game's server with `PlatformerQuery` against replicated positions.

| Component | What it does | Needs |
| --- | --- | --- |
| `LaunchPad` | Spring / boost panel / cannon / mushroom cap: launches with a pad-space velocity; an optional `aimVisual` (cannon barrel) turns to the launch direction | Trigger collider |
| `GravityZone` | Scales gravity (moon jumps, heavy falls) or turns it toward a wall or ceiling, so the character walks on it | Trigger collider; character needs `CharacterGravity` |
| `ConveyorSurface` | Pushes standing characters; keeps momentum when they step off | Solid collider |
| `WallJumpSurface` | Lets airborne characters kick off with a fresh jump press | Solid collider |
| `CrumbleBlock` | Shakes, drops away and returns after the local player stands on it | Solid collider |
| `TimedToggleBlock` | Solid for part of a cycle on the shared network clock (beat blocks); with a trigger and a `HazardVolume` it times laser gates instead | Solid collider, or trigger + `HazardVolume` |
| `Pendulum` | Swinging hammer or axe about its pivot on the shared clock | `KnockbackSurface` on the head |
| `Crusher` | Thwomp: waits, shakes (`warnShake`, `warnShakeFrequency`), slams down onto its optional `landingMark` (moved out from under the block onto the landing spot once, in `Configure`), `IsDeadlyAt`/`PositionAt` for judging at a past moment, holds, rises; its crush zone is live only while down, on the server too (`Slammed` event for sound) | Solid collider + child trigger with `HazardVolume` |
| `RollingLog` | A spinning log whose surface carries walkers along the spin (balance across it, or a treadmill) | Solid box along the log's top; `spinVisual` turns |
| `Turntable` | Sizes a runtime-spawned disc and stands bumper posts on it (spin from `NetworkPlatform`) | `NetworkPlatform`, post prefab with `KnockbackSurface` |
| `OnOffChannels` | Static per-client state of on/off channels; resets on a local teleport | — |
| `OnOffBlock` | Solid in one channel state, a ghost outline in the other (two colours swap) | Solid collider |
| `OnOffSwitch` | Floor button or "!" block bumped from below that flips its channel (`Pressed` event); `pressVisual` moves along `pressDirection` when hit (down for a button, up for a bumped block) | Trigger collider |
| `GravityFlipPad` | Flip arrow or orb: turns the local character's gravity to a set direction until the next pad or a respawn (`Flipped` event) | Trigger collider; character needs `CharacterGravity` |
| `RisingLava` | Per-player lava chase: starts when the local player is over its footprint, rises at a set speed to a set height; `Rise` and `IsOver` are static so the server runs the same chase (`ChaseStarted` event) | `surface` transform; the server decides deaths |
| `IceSurface` | Slippery ground: its `traction` (0-1) limits how fast a standing character can change velocity, so it slides and keeps its speed | Solid collider (found from the ground with `GetComponentInParent`) |
| `WindVolume` | Pushes the local character along `localDirection` while inside, at `speed` plus a cosine gust on `PlatformerClock`; sizes its optional streak particles to the box (`Resize`, `Entered` event) | Trigger collider |
| `HazardVolume` | Marks lava/spikes; the server sends touching characters back | Any collider |
| `KnockbackSurface` | Bumper / sweeper: a hit flings the character along the obstacle's motion (hard hits ragdoll it) | Solid collider on a moving object |
| `CheckpointMarker` | Flag presentation for a reached checkpoint | Renderers |
| `Spinner` | Visual spin/bob for collectibles | — |

Every timed piece (`TimedToggleBlock`, `Pendulum`, `Crusher`, `RollingLog`) runs on `PlatformerClock`:
the shared network motion clock once it is ready (local time before that), so the server and every
client see the same phase. Pieces that react to "the local player" (`OnOffSwitch`, `RisingLava`)
compare against `LocalPlayer.Character`, which is why the assembly references
`RyanAssets.Shared.Component`.

`TimedHazards` is the server-side judge for the pieces that are deadly only part of the time (a
`TimedToggleBlock` laser with a `HazardVolume`, and `Crusher`s). A position reaches the server after the
player saw it, and clocks can differ slightly, so judging these on the server's current physics would
send back a player who visibly cleared them. `Collect(root)` after building the level, skip
`Contains(volume)` results from the plain `Overlap`, and call `Hits(box, time, grace)` with the
shared-clock moment the player saw: each piece is evaluated analytically at that moment (switched-off
colliders included, via `PlatformerQuery.BoxesOverlap`) and counts only while it is deadly `grace`
seconds before and after it.

Moving and spinning platforms reuse `RyanAssets.Items.Platform.NetworkPlatform` (call `Configure` on
runtime-spawned ones) with `GroundMotionTransfer` so riders are carried.

`CharacterMovement` exposes the hooks these use: `Launch`, `Gravity`, `Up`, `ExternalVelocity`,
`AddPush` (a velocity added for one physics step, for fields such as wind; call it every step while it
applies), `Traction` (the current ground's grip) and the surface interfaces `ISurfaceVelocity`,
`ISurfaceTraction` and `IWallJumpSurface` (in `RyanAssets.Characters.Shared`). Below full traction the
per-step velocity change is capped at `Traction` x Slip Acceleration (an Inspector setting). `LocalCharacter.TeleportTo`
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

## Parkour and ragdoll

`CharacterParkour` (Characters.Client, add it next to the game's client controller) plugs into
`CharacterMovement` as an `IMovementMode` and gives the local player ledge grabs (shimmy, climb, kick
off, drop), mantling, sliding (Crouch while running, jump out of it for a boost), wall runs (Sprint
along a wall in the air), landing rolls (Crouch just before a hard landing) and ragdolls (very hard
landings, `KnockbackSurface` hits, or `Tumble` from game code such as a hazard). `CharacterStance` on
the character prefab shares the stance with every client and draws it: a procedural pose over the
animator, or the physics ragdoll falling in the character's own gravity. Every setting is on the two
components in the Inspector.

## Not covered yet

- `OnOffSwitch` latches on trigger entry (a switch fires once per touch, not while held).
